namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>Pure queries over the event stream: filtering for the stream panel and the per-flight journey (#76).</summary>
public static class EventQueries
{
    /// <summary>
    /// Stream rows (newest first) matching an optional topic and an optional flight number.
    /// Flight matching is case-insensitive and exact — <c>BA1</c> must not match <c>BA123</c>.
    /// </summary>
    public static IEnumerable<EventRow> Filter(AirportView view, string? topic, string? flightNumber) =>
        view.Stream.Where(e =>
            (string.IsNullOrWhiteSpace(topic) || string.Equals(e.Topic, topic, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(flightNumber) || string.Equals(e.FlightNumber, flightNumber.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>Every stream event for one flight across all topics, oldest first — the flight's journey so far.</summary>
    public static IReadOnlyList<EventRow> Journey(AirportView view, string flightNumber) =>
        Filter(view, topic: null, flightNumber).Reverse().ToList();
}
