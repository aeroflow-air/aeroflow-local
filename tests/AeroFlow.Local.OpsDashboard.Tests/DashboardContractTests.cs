using AeroFlow.Local.OpsDashboard.Live;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

/// <summary>
/// Dashboard consumer contract tests (ADR-0019, platform-handbook#111). The JSON files under
/// Contracts/ are pinned copies of the producers' canonical examples (svc-gate-allocation
/// contracts/gate-events, svc-baggage-reclaim contracts/baggage-events). Do not edit the pins here:
/// the producer changes first, then the new examples are copied over them. Flight-events needs no
/// copy here — the dashboard uses the producer's AeroFlow.Local.FlightEvents project directly,
/// covered by FlightEventContractTests.
/// </summary>
public sealed class DashboardContractTests
{
    public static TheoryData<string, GateEventType> GateExamples => new()
    {
        { "gate-assigned", GateEventType.GateAssigned },
        { "gate-changed", GateEventType.GateChanged },
        { "gate-released", GateEventType.GateReleased },
    };

    public static TheoryData<string, BaggageEventType> BagExamples => new()
    {
        { "belt-assigned", BaggageEventType.BeltAssigned },
        { "bags-on-belt", BaggageEventType.BagsOnBelt },
        { "last-bag-on-belt", BaggageEventType.LastBagOnBelt },
        { "reclaim-complete", BaggageEventType.ReclaimComplete },
        { "bag-delayed", BaggageEventType.BagDelayed },
    };

    [Theory]
    [MemberData(nameof(GateExamples))]
    public void Canonical_gate_example_reads_into_the_dashboard_copy(string name, GateEventType type)
    {
        var evt = ReadGate(name);

        // FlightId is non-nullable, matching the producer — the #71-survey drift is pinned shut here.
        Assert.Equal((type, "SIM-00001", "BA123"), (evt.EventType, evt.FlightId, evt.FlightNumber));
        Assert.False(string.IsNullOrWhiteSpace(evt.Gate));
    }

    [Theory]
    [MemberData(nameof(BagExamples))]
    public void Canonical_baggage_example_reads_into_the_dashboard_copy(string name, BaggageEventType type)
    {
        var evt = ReadBag(name);

        Assert.Equal((type, "SIM-00002", "KL456", "3"), (evt.EventType, evt.FlightId, evt.FlightNumber, evt.Belt));
    }

    [Fact]
    public void Every_gate_and_baggage_event_type_has_a_pinned_example()
    {
        Assert.Equal(Enum.GetValues<GateEventType>().ToHashSet(), GateExamples.Select(r => (GateEventType)r[1]!).ToHashSet());
        Assert.Equal(Enum.GetValues<BaggageEventType>().ToHashSet(), BagExamples.Select(r => (BaggageEventType)r[1]!).ToHashSet());
    }

    [Fact]
    public void Unknown_extra_fields_are_tolerated()
    {
        var gate = WithExtra("gate-events", "gate-assigned");
        Assert.Equal(GateEventType.GateAssigned, TopicListener<GateEventMessage>.TryRead(BinaryData.FromString(gate))!.EventType);
        var bag = WithExtra("baggage-events", "bags-on-belt");
        Assert.Equal(BaggageEventType.BagsOnBelt, TopicListener<BaggageEventMessage>.TryRead(BinaryData.FromString(bag))!.EventType);
    }

    [Fact]
    public void Unknown_event_types_do_not_read_and_would_dead_letter()
    {
        // ADR-0019: new event types are consumer-first. TopicListener dead-letters bodies TryRead
        // cannot read, so an unknown type must keep returning null here rather than being accepted.
        var gate = Text("gate-events", "gate-assigned").Replace("\"GateAssigned\"", "\"GateMoved\"");
        Assert.Null(TopicListener<GateEventMessage>.TryRead(BinaryData.FromString(gate)));
        var bag = Text("baggage-events", "bags-on-belt").Replace("\"BagsOnBelt\"", "\"BagsSorted\"");
        Assert.Null(TopicListener<BaggageEventMessage>.TryRead(BinaryData.FromString(bag)));
    }

    private static GateEventMessage ReadGate(string name) =>
        TopicListener<GateEventMessage>.TryRead(BinaryData.FromString(Text("gate-events", name)))!;

    private static BaggageEventMessage ReadBag(string name) =>
        TopicListener<BaggageEventMessage>.TryRead(BinaryData.FromString(Text("baggage-events", name)))!;

    private static string WithExtra(string topic, string name)
    {
        var json = Text(topic, name);
        return json.Insert(json.TrimEnd().Length - 1, ", \"futureField\": {\"anything\": 1}");
    }

    private static string Text(string topic, string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", topic, $"{name}.json"));
}
