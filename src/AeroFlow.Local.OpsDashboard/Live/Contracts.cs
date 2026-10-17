using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroFlow.Local.OpsDashboard.Live;

// Local copies of the producers' wire contracts (svc-baggage-reclaim BaggageEvent, svc-gate-allocation GateEvent),
// kept per ADR-0019 (producer-owned contracts, consumer copies) and pinned by the producer-JSON tests
// (platform-handbook#111). FlightEvent comes from AeroFlow.Local.FlightEvents.

[JsonConverter(typeof(JsonStringEnumConverter<BaggageEventType>))]
public enum BaggageEventType
{
    BeltAssigned,
    BagsOnBelt,
    LastBagOnBelt,
    ReclaimComplete,
    BagDelayed,
}

public sealed record BaggageEventMessage(
    Guid EventId,
    BaggageEventType EventType,
    string FlightId,
    string FlightNumber,
    string Belt,
    DateTimeOffset OccurredAt,
    int? BagCount = null,
    int? BagsCollected = null,
    int? BagsMissing = null,
    string? BagTag = null,
    string? Reason = null);

[JsonConverter(typeof(JsonStringEnumConverter<GateEventType>))]
public enum GateEventType
{
    GateAssigned,
    GateChanged,
    GateReleased,
}

public sealed record GateEventMessage(
    Guid EventId,
    GateEventType EventType,
    string FlightNumber,
    string FlightId,
    string Gate,
    DateTimeOffset OccurredAt,
    string? PreviousGate = null,
    DateTimeOffset? OnBlock = null,
    DateTimeOffset? OffBlock = null);

public static class LiveJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
