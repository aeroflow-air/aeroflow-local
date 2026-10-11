using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroFlow.Local.FlightEvents;

/// <summary>Lifecycle stages published on the <c>flight-events</c> topic.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlightEventType>))]
public enum FlightEventType
{
    Scheduled,
    Boarding,
    Departed,
    Landed,
    Delayed,
    Cancelled,
}

/// <summary>
/// Wire contract for a flight lifecycle event (JSON body of a Service Bus message).
/// Message <c>Subject</c> carries the event type; <c>CorrelationId</c> carries the flight id.
/// </summary>
public sealed record FlightEvent(
    Guid EventId,
    string FlightId,
    string FlightNumber,
    FlightEventType EventType,
    string Origin,
    string Destination,
    DateTimeOffset ScheduledDeparture,
    DateTimeOffset OccurredAt,
    int? DelayMinutes = null,
    string? Reason = null);

public static class FlightEventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class FlightEventTopics
{
    public const string ConnectionName = "messaging";
    public const string FlightEvents = "flight-events";
}
