using System.Collections.Immutable;
using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Live;
using AeroFlow.Local.OpsDashboard.Snapshot;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class SnapshotTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Departures_and_arrivals_map_to_snapshot_rows_and_diverted_is_skipped()
    {
        var departures = SnapshotMapper.Departures(new DeparturesBoardDto("LHR", T0,
        [
            new DepartureDto("BA1", "AMS", T0.AddHours(1), "12", "Boarding", 0),
            new DepartureDto("BA2", "CDG", T0.AddHours(2), null, "Diverted", 0),
        ]));
        var arrivals = SnapshotMapper.Arrivals(new ArrivalsBoardDto("LHR", T0, [new ArrivalDto("KL3", "AMS", T0.AddHours(-1), "Delayed", 25)]));

        var row = Assert.Single(departures);
        Assert.Equal(("api:BA1", "LHR", "AMS", FlightEventType.Boarding, "12", true), (row.FlightId, row.Origin, row.Destination, row.Status, row.Gate, row.FromSnapshot));
        var arrival = Assert.Single(arrivals);
        Assert.Equal(("AMS", "LHR", FlightEventType.Delayed, 25), (arrival.Origin, arrival.Destination, arrival.Status, arrival.DelayMinutes));
    }

    [Fact]
    public void Gate_tile_shows_the_flight_on_block_else_the_next_one_else_free_and_closed_gates_are_left_off()
    {
        var tiles = SnapshotMapper.Gates(
            [
                new GateDto("1", "Open", [Alloc("BA1", "1", -30, 30), Alloc("BA2", "1", 60, 120)]),
                new GateDto("2", "Open", [Alloc("BA3", "2", -120, -60), Alloc("BA4", "2", 90, 150), Alloc("BA5", "2", 20, 80)]),
                new GateDto("3", "Open", []),
                new GateDto("4", "Closed", []),
            ],
            T0);

        Assert.Equal(["1", "2", "3"], tiles.Select(t => t.Gate));
        Assert.Equal(["BA1", "BA5", null], tiles.Select(t => t.FlightNumber));
        Assert.All(tiles, t => Assert.True(t.FromSnapshot));
    }

    [Fact]
    public void Belts_map_each_reclaim_with_its_collected_count()
    {
        var reclaims = SnapshotMapper.Belts(
            [
                new BeltDto("1", [new BeltReclaimDto("SIM-1", "BA1", T0, "OnBelt", 100, 40, 2)]),
                new BeltDto("2", [new BeltReclaimDto("SIM-2", "BA2", T0, "Complete", 80, 78, 0)]),
            ],
            T0);

        Assert.Equal((ReclaimStage.OnBelt, 100, (int?)40, 2), (reclaims[0].Stage, reclaims[0].BagCount!.Value, reclaims[0].BagsCollected, reclaims[0].BagsDelayed));
        Assert.Equal(40, reclaims[0].ProgressPercent);
        Assert.Equal(97, reclaims[1].ProgressPercent);
        Assert.Equal((ReclaimStage.Complete, (int?)78, "2"), (reclaims[1].Stage, reclaims[1].BagsCollected, reclaims[1].Belt));
    }

    [Fact]
    public void Hydrate_fills_an_empty_view_and_gives_arrivals_their_belt()
    {
        var view = AirportProjector.Hydrate(AirportView.Empty, Snapshot(
            arrivals: [Row("KL3", "AMS", "LHR")],
            belts: [Reclaim("SIM-9", "KL3", "4")],
            gates: [Tile("12", "BA1")]));

        Assert.Equal("4", view.Flights["api:KL3"].Belt);
        Assert.Equal("BA1", view.Gates["12"].FlightNumber);
        Assert.Contains("SIM-9", view.Reclaims.Keys);
        Assert.Empty(view.Stream);
        Assert.Equal(T0, view.Snapshot.At);
        Assert.Empty(view.Snapshot.Failed);
    }

    [Fact]
    public void Live_events_win_over_the_snapshot()
    {
        var live = AirportProjector.Apply(AirportView.Empty, new FlightEvent(Guid.NewGuid(), "SIM-1", "BA1", FlightEventType.Departed, "LHR", "AMS", T0, T0));
        live = AirportProjector.Apply(live, new GateEventMessage(Guid.NewGuid(), GateEventType.GateReleased, "BA1", "SIM-1", "12", T0));
        live = AirportProjector.Apply(live, new BaggageEventMessage(Guid.NewGuid(), BaggageEventType.ReclaimComplete, "SIM-7", "KL7", "2", T0, 50, 50));

        var view = AirportProjector.Hydrate(live, Snapshot(
            departures: [Row("BA1", "LHR", "AMS") with { Status = FlightEventType.Boarding }],
            gates: [Tile("12", "BA1"), Tile("14", "BA8")],
            belts: [Reclaim("SIM-7", "KL7", "2") with { Stage = ReclaimStage.OnBelt }]));

        Assert.Equal(FlightEventType.Departed, Assert.Single(view.Flights.Values).Status);
        Assert.Null(view.Gates["12"].FlightNumber);
        Assert.Equal("BA8", view.Gates["14"].FlightNumber);
        Assert.Equal(ReclaimStage.Complete, view.Reclaims["SIM-7"].Stage);
    }

    [Fact]
    public void The_first_event_for_a_snapshot_flight_takes_over_its_row()
    {
        var view = AirportProjector.Hydrate(AirportView.Empty, Snapshot(departures: [Row("BA1", "LHR", "AMS") with { Gate = "12", DelayMinutes = 15 }]));

        view = AirportProjector.Apply(view, new FlightEvent(Guid.NewGuid(), "SIM-1", "BA1", FlightEventType.Boarding, "LHR", "AMS", T0, T0.AddMinutes(1)));

        var row = Assert.Single(view.Flights.Values);
        Assert.Equal(("SIM-1", FlightEventType.Boarding, "12", 15, false), (row.FlightId, row.Status, row.Gate, row.DelayMinutes, row.FromSnapshot));
    }

    [Fact]
    public void Baggage_events_set_the_belt_on_a_snapshot_arrival()
    {
        var view = AirportProjector.Hydrate(AirportView.Empty, Snapshot(arrivals: [Row("KL3", "AMS", "LHR")]));

        view = AirportProjector.Apply(view, new BaggageEventMessage(Guid.NewGuid(), BaggageEventType.BeltAssigned, "SIM-3", "KL3", "5", T0));

        Assert.Equal("5", view.Flights["api:KL3"].Belt);
    }

    [Fact]
    public void A_refresh_replaces_earlier_snapshot_rows_and_a_failed_source_leaves_its_part_alone()
    {
        var first = AirportProjector.Hydrate(AirportView.Empty, Snapshot(
            departures: [Row("BA1", "LHR", "AMS"), Row("BA2", "LHR", "CDG")],
            gates: [Tile("12", "BA1")]));

        var second = AirportProjector.Hydrate(first, Snapshot(departures: [Row("BA2", "LHR", "CDG")], gatesFail: "gate-allocation down"));

        Assert.Equal(["api:BA2"], second.Flights.Keys);
        Assert.Equal("BA1", second.Gates["12"].FlightNumber);
        Assert.Equal(["gates"], second.Snapshot.Failed.Keys);
        Assert.DoesNotContain("gates", second.Snapshot.Loaded);
    }

    private static AirportSnapshot Snapshot(
        ImmutableList<FlightRow>? departures = null,
        ImmutableList<FlightRow>? arrivals = null,
        ImmutableList<GateTile>? gates = null,
        ImmutableList<BeltFlight>? belts = null,
        string? gatesFail = null) =>
        new(
            T0,
            "LHR",
            new SourceLoad<ImmutableList<FlightRow>>.Loaded(departures ?? []),
            new SourceLoad<ImmutableList<FlightRow>>.Loaded(arrivals ?? []),
            gatesFail is null ? new SourceLoad<ImmutableList<GateTile>>.Loaded(gates ?? []) : new SourceLoad<ImmutableList<GateTile>>.Failed(gatesFail),
            new SourceLoad<ImmutableList<BeltFlight>>.Loaded(belts ?? []));

    private static FlightRow Row(string number, string origin, string destination) =>
        new(FlightRow.SnapshotKey(number), number, origin, destination, T0, FlightEventType.Scheduled, 0, null, null, T0) { FromSnapshot = true };

    private static GateTile Tile(string gate, string flight) => new(gate, flight, T0, T0.AddHours(1), T0) { FromSnapshot = true };

    private static BeltFlight Reclaim(string id, string number, string belt) =>
        new(id, number, belt, ReclaimStage.Assigned, 50, null, 0, T0, null, null, T0) { FromSnapshot = true };

    private static AllocationDto Alloc(string flight, string gate, int fromMinutes, int toMinutes) =>
        new(flight, gate, T0.AddMinutes(fromMinutes), T0.AddMinutes(toMinutes));
}
