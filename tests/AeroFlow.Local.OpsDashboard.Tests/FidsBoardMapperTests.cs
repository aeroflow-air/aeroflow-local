using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Live;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class FidsBoardMapperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Departures_are_home_origin_ordered_by_schedule_and_capped()
    {
        var view = AirportView.Empty;
        view = ApplyFlight(view, "SIM-1", "BA100", "LHR", "AMS", T0.AddHours(2), FlightEventType.Scheduled);
        view = ApplyFlight(view, "SIM-2", "KL200", "AMS", "LHR", T0, FlightEventType.Landed);
        view = ApplyFlight(view, "SIM-3", "AF300", "lhr", "CDG", T0.AddHours(1), FlightEventType.Boarding, gate: "7");
        view = ApplyFlight(view, "SIM-4", "LH400", "LHR", "FRA", T0.AddMinutes(30), FlightEventType.Delayed, delay: 35, gate: "22");

        var rows = FidsBoardMapper.Departures(view, maxRows: 2);

        Assert.Equal(["LH400", "AF300"], rows.Select(r => r.FlightNumber));
        Assert.Equal(["FRA", "CDG"], rows.Select(r => r.Place));
        Assert.Equal(["22", "07"], rows.Select(r => r.Slot));
        Assert.Equal(FidsStatusKind.Delayed, rows[0].Status);
        Assert.Equal("DELAYED +35", rows[0].StatusLabel);
        Assert.Equal(FidsStatusKind.Boarding, rows[1].Status);
        Assert.Equal("BOARDING", rows[1].StatusLabel);
    }

    [Fact]
    public void Arrivals_use_origin_and_belt_and_tba_when_unassigned()
    {
        var view = AirportView.Empty;
        view = ApplyFlight(view, "SIM-2", "KL200", "AMS", "LHR", T0, FlightEventType.Landed, belt: "3");
        view = ApplyFlight(view, "SIM-5", "EI178", "DUB", "LHR", T0.AddMinutes(10), FlightEventType.Scheduled);
        view = ApplyFlight(view, "SIM-1", "BA100", "LHR", "AMS", T0, FlightEventType.Scheduled);

        var rows = FidsBoardMapper.Arrivals(view);

        Assert.Equal(["KL200", "EI178"], rows.Select(r => r.FlightNumber));
        Assert.Equal(["AMS", "DUB"], rows.Select(r => r.Place));
        Assert.Equal(["03", "TBA"], rows.Select(r => r.Slot));
        Assert.Equal("LANDED", rows[0].StatusLabel);
        Assert.Equal("SCHEDULED", rows[1].StatusLabel);
    }

    [Theory]
    [InlineData(FlightEventType.Scheduled, 0, "SCHEDULED", FidsStatusKind.Scheduled)]
    [InlineData(FlightEventType.Scheduled, 10, "SCHEDULED +10", FidsStatusKind.Scheduled)]
    [InlineData(FlightEventType.Boarding, 0, "BOARDING", FidsStatusKind.Boarding)]
    [InlineData(FlightEventType.Departed, 0, "DEPARTED", FidsStatusKind.Departed)]
    [InlineData(FlightEventType.Landed, 0, "LANDED", FidsStatusKind.Landed)]
    [InlineData(FlightEventType.Delayed, 35, "DELAYED +35", FidsStatusKind.Delayed)]
    [InlineData(FlightEventType.Delayed, 0, "DELAYED", FidsStatusKind.Delayed)]
    [InlineData(FlightEventType.Cancelled, 0, "CANCELLED", FidsStatusKind.Cancelled)]
    public void Status_label_and_kind_match_the_flight(FlightEventType type, int delay, string label, FidsStatusKind kind)
    {
        var flight = Row("SIM-1", "BA1", "LHR", "AMS", type, delay);

        Assert.Equal(label, FidsBoardMapper.StatusLabel(flight));
        Assert.Equal(kind, FidsBoardMapper.StatusKind(type));
    }

    [Fact]
    public void Reclaims_follow_belt_order_and_map_stages()
    {
        var view = AirportView.Empty;
        view = AddReclaim(view, "SIM-1", "KL456", "3", ReclaimStage.LastBag, T0, T0.AddMinutes(4), T0.AddMinutes(11));
        view = AddReclaim(view, "SIM-2", "EI178", "1", ReclaimStage.OnBelt, T0.AddMinutes(1), T0.AddMinutes(8), null);
        view = AddReclaim(view, "SIM-3", "U2302", "4", ReclaimStage.Complete, T0.AddMinutes(2), T0.AddMinutes(3), T0.AddMinutes(9));
        view = AddReclaim(view, "SIM-4", "BA9", "2", ReclaimStage.Assigned, T0.AddMinutes(3), null, null);
        view = AddReclaim(view, "SIM-5", "AF1", "5", ReclaimStage.Assigned, T0.AddMinutes(4), null, null);

        var rows = FidsBoardMapper.Reclaims(view, maxRows: 4, zone: Utc);

        Assert.Equal(["EI178", "BA9", "KL456", "U2302"], rows.Select(r => r.FlightNumber));
        Assert.Equal(["1", "2", "3", "4"], rows.Select(r => r.Belt));
        Assert.Equal(
            [FidsReclaimKind.FirstBag, FidsReclaimKind.Waiting, FidsReclaimKind.LastBag, FidsReclaimKind.Closed],
            rows.Select(r => r.Stage));
        Assert.Equal("FIRST BAG", rows[0].StageLabel);
        Assert.Equal("WAITING", rows[1].StageLabel);
        Assert.Equal("LAST BAG", rows[2].StageLabel);
        Assert.Equal("CLOSED", rows[3].StageLabel);
        Assert.Equal("FIRST 09:08", rows[0].Times);
        Assert.Null(rows[1].Times);
        Assert.Equal("FIRST 09:04 · LAST 09:11", rows[2].Times);
    }

    [Fact]
    public void Clock_formats_in_the_given_time_zone()
    {
        var utc = new DateTimeOffset(2026, 11, 21, 9, 7, 42, TimeSpan.Zero);

        Assert.Equal("09:07", FidsBoardMapper.Clock(utc, Utc));
        Assert.Equal(("09:07:42", "SAT 21 NOV 2026"), FidsClockFormat.Format(utc, Utc));
    }

    [Fact]
    public void Clock_format_converts_from_utc_into_the_zone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("demo", TimeSpan.FromHours(1), "demo", "demo");
        var utc = new DateTimeOffset(2026, 11, 21, 23, 15, 4, TimeSpan.Zero);

        Assert.Equal(("00:15:04", "SUN 22 NOV 2026"), FidsClockFormat.Format(utc, zone));
    }

    private static AirportView ApplyFlight(
        AirportView view,
        string id,
        string number,
        string origin,
        string destination,
        DateTimeOffset scheduled,
        FlightEventType status,
        int delay = 0,
        string? gate = null,
        string? belt = null)
    {
        var next = AirportProjector.Apply(
            view,
            new FlightEvent(Guid.NewGuid(), id, number, FlightEventType.Scheduled, origin, destination, scheduled, T0));
        if (status != FlightEventType.Scheduled || delay > 0)
        {
            var type = status == FlightEventType.Scheduled ? FlightEventType.Delayed : status;
            next = AirportProjector.Apply(
                next,
                new FlightEvent(Guid.NewGuid(), id, number, type, origin, destination, scheduled, T0, delay > 0 ? delay : null));
        }

        var row = next.Flights[id];
        return next with
        {
            Flights = next.Flights.SetItem(id, row with { Gate = gate, Belt = belt, DelayMinutes = delay, Status = status }),
            Version = next.Version + 1,
        };
    }

    private static AirportView AddReclaim(
        AirportView view,
        string id,
        string number,
        string belt,
        ReclaimStage stage,
        DateTimeOffset assigned,
        DateTimeOffset? first,
        DateTimeOffset? last)
    {
        var reclaim = new BeltFlight(id, number, belt, stage, 100, stage == ReclaimStage.Complete ? 100 : null, 0, assigned, first, last, assigned);
        return view with { Reclaims = view.Reclaims.SetItem(id, reclaim), Version = view.Version + 1 };
    }

    private static FlightRow Row(string id, string number, string origin, string destination, FlightEventType status, int delay) =>
        new(id, number, origin, destination, T0, status, delay, null, null, T0);
}
