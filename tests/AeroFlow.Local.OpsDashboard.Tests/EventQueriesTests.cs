using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Live;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class EventQueriesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Filter_matches_topic_and_exact_flight_case_insensitively()
    {
        var view = View();

        Assert.Equal(2, EventQueries.Filter(view, "gate-events", null).Count());
        Assert.Equal(3, EventQueries.Filter(view, null, "ba123").Count());
        Assert.Single(EventQueries.Filter(view, "baggage-events", "BA123"));
        Assert.Empty(EventQueries.Filter(view, null, "BA1"));
        Assert.Equal(4, EventQueries.Filter(view, "", " ").Count());
    }

    [Fact]
    public void Journey_is_chronological_across_topics_for_one_flight_only()
    {
        var journey = EventQueries.Journey(View(), "BA123");

        Assert.Equal(["Scheduled", "GateAssigned", "BeltAssigned"], journey.Select(e => e.Type));
        Assert.Equal(T0, journey[0].At);
    }

    [Fact]
    public void Belt_progress_uses_counts_when_known_and_stage_otherwise()
    {
        BeltFlight Reclaim(ReclaimStage stage, int? count, int? collected) =>
            new("SIM-1", "BA123", "3", stage, count, collected, 0, T0, null, null, T0);

        Assert.Equal(25, Reclaim(ReclaimStage.OnBelt, 200, 50).ProgressPercent);
        Assert.Equal(100, Reclaim(ReclaimStage.Complete, 200, 200).ProgressPercent);
        Assert.Equal(40, Reclaim(ReclaimStage.OnBelt, null, null).ProgressPercent);
        Assert.Equal(5, Reclaim(ReclaimStage.Assigned, 200, null).ProgressPercent);
    }

    private static AirportView View()
    {
        var view = AirportView.Empty;
        view = AirportProjector.Apply(view, new FlightEvent(Guid.NewGuid(), "SIM-1", "BA123", FlightEventType.Scheduled, "LHR", "AMS", T0, T0));
        view = AirportProjector.Apply(view, new GateEventMessage(Guid.NewGuid(), GateEventType.GateAssigned, "BA123", "SIM-1", "12", T0.AddMinutes(1)));
        view = AirportProjector.Apply(view, new BaggageEventMessage(Guid.NewGuid(), BaggageEventType.BeltAssigned, "SIM-1", "BA123", "3", T0.AddMinutes(2)));
        view = AirportProjector.Apply(view, new GateEventMessage(Guid.NewGuid(), GateEventType.GateAssigned, "KL9", "SIM-2", "14", T0.AddMinutes(3)));
        return view;
    }
}
