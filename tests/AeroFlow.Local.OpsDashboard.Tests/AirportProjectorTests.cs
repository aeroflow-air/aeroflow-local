using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Live;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class AirportProjectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Flight_events_build_the_board()
    {
        var view = AirportProjector.Apply(AirportView.Empty, Flight(FlightEventType.Scheduled));
        view = AirportProjector.Apply(view, Flight(FlightEventType.Delayed) with { DelayMinutes = 20 });

        var row = view.Flights["SIM-1"];
        Assert.Equal((FlightEventType.Delayed, 20, "BA123"), (row.Status, row.DelayMinutes, row.FlightNumber));
        Assert.Equal(2, view.Stream.Count);
        Assert.Equal("Delayed", view.Stream[0].Type);
    }

    [Fact]
    public void Duplicate_event_ids_are_ignored()
    {
        var e = Flight(FlightEventType.Scheduled);
        var once = AirportProjector.Apply(AirportView.Empty, e);

        Assert.Same(once, AirportProjector.Apply(once, e));
    }

    [Fact]
    public void Gate_events_fill_gates_and_the_board()
    {
        var view = AirportProjector.Apply(AirportView.Empty, Flight(FlightEventType.Scheduled));
        view = AirportProjector.Apply(view, Gate(GateEventType.GateAssigned, "12"));

        Assert.Equal("BA123", view.Gates["12"].FlightNumber);
        Assert.Equal("12", view.Flights["SIM-1"].Gate);

        view = AirportProjector.Apply(view, Gate(GateEventType.GateChanged, "14") with { PreviousGate = "12" });
        Assert.Null(view.Gates["12"].FlightNumber);
        Assert.Equal("BA123", view.Gates["14"].FlightNumber);
        Assert.Equal("14", view.Flights["SIM-1"].Gate);

        view = AirportProjector.Apply(view, Gate(GateEventType.GateReleased, "14"));
        Assert.Null(view.Gates["14"].FlightNumber);
    }

    [Fact]
    public void Gates_sort_numerically()
    {
        var view = AirportView.Empty;
        foreach (var g in new[] { "21", "7", "55A", "12", "R1" })
        {
            view = AirportProjector.Apply(view, Gate(GateEventType.GateAssigned, g));
        }

        Assert.Equal(["7", "12", "21", "55A", "R1"], view.Gates.Keys);
    }

    [Fact]
    public void Baggage_events_move_a_reclaim_through_its_stages()
    {
        var view = AirportProjector.Apply(AirportView.Empty, Flight(FlightEventType.Landed) with { Origin = "AMS", Destination = "LHR" });
        view = AirportProjector.Apply(view, Bag(BaggageEventType.BeltAssigned));
        Assert.Equal(ReclaimStage.Assigned, view.Reclaims["SIM-1"].Stage);
        Assert.Equal("3", view.Flights["SIM-1"].Belt);

        view = AirportProjector.Apply(view, Bag(BaggageEventType.BagsOnBelt) with { BagCount = 120 });
        view = AirportProjector.Apply(view, Bag(BaggageEventType.BagDelayed) with { BagTag = "BA000001" });
        view = AirportProjector.Apply(view, Bag(BaggageEventType.LastBagOnBelt));
        view = AirportProjector.Apply(view, Bag(BaggageEventType.ReclaimComplete) with { BagCount = 120, BagsCollected = 119, BagsMissing = 1 });

        var r = view.Reclaims["SIM-1"];
        Assert.Equal((ReclaimStage.Complete, 120, 119, 1), (r.Stage, r.BagCount!.Value, r.BagsCollected!.Value, r.BagsDelayed));
        Assert.NotNull(r.FirstBagAt);
        Assert.NotNull(r.LastBagAt);
        Assert.Contains("119/120", view.Stream[0].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Stream_is_capped_newest_first()
    {
        var view = AirportView.Empty;
        FlightEvent last = Flight(FlightEventType.Scheduled);
        for (var i = 0; i < AirportView.StreamLength + 25; i++)
        {
            last = Flight(FlightEventType.Scheduled) with { FlightId = $"SIM-{i}" };
            view = AirportProjector.Apply(view, last);
        }

        Assert.Equal(AirportView.StreamLength, view.Stream.Count);
        Assert.Equal(last.EventId, view.Stream[0].EventId);
    }

    [Fact]
    public void Expire_drops_finished_flights_and_cleared_belts()
    {
        var view = AirportProjector.Apply(AirportView.Empty, Flight(FlightEventType.Departed));
        view = AirportProjector.Apply(view, Bag(BaggageEventType.ReclaimComplete) with { FlightId = "SIM-2" });

        Assert.Same(view, AirportProjector.Expire(view, T0.AddMinutes(1)));
        var later = AirportProjector.Expire(view, T0.AddMinutes(11));
        Assert.Empty(later.Flights);
        Assert.Empty(later.Reclaims);
    }

    [Fact]
    public void Unreadable_bodies_are_rejected_and_producer_json_reads()
    {
        Assert.Null(TopicListener<GateEventMessage>.TryRead(BinaryData.FromString("not json")));

        var gate = TopicListener<GateEventMessage>.TryRead(BinaryData.FromString(
            """{"eventId":"8c6f2f1e-6f7e-4a52-9a43-5b9e0a3c2d11","eventType":"GateAssigned","flightNumber":"BA123","flightId":"SIM-1","gate":"12","occurredAt":"2026-10-12T18:00:00+00:00","onBlock":"2026-10-12T18:15:00+00:00","offBlock":"2026-10-12T19:00:00+00:00"}"""));
        Assert.Equal((GateEventType.GateAssigned, "12", "SIM-1"), (gate!.EventType, gate.Gate, gate.FlightId));

        var bag = TopicListener<BaggageEventMessage>.TryRead(BinaryData.FromString(
            """{"eventId":"8c6f2f1e-6f7e-4a52-9a43-5b9e0a3c2d12","eventType":"BagsOnBelt","flightId":"SIM-1","flightNumber":"U2123","belt":"4","occurredAt":"2026-10-12T18:00:00+00:00","bagCount":90}"""));
        Assert.Equal((BaggageEventType.BagsOnBelt, 90), (bag!.EventType, bag.BagCount!.Value));
    }

    private static FlightEvent Flight(FlightEventType type) =>
        new(Guid.NewGuid(), "SIM-1", "BA123", type, "LHR", "AMS", T0.AddHours(1), T0);

    private static GateEventMessage Gate(GateEventType type, string gate) =>
        new(Guid.NewGuid(), type, "BA123", "SIM-1", gate, T0, null, T0.AddMinutes(15), T0.AddHours(1));

    private static BaggageEventMessage Bag(BaggageEventType type) =>
        new(Guid.NewGuid(), type, "SIM-1", "BA123", "3", T0);
}
