using System.Collections.Immutable;
using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Snapshot;

namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>
/// Pure projection of events onto <see cref="AirportView"/>. Duplicate event ids are ignored (Service Bus is
/// at-least-once), so applying the same event twice returns the same view.
/// </summary>
public static class AirportProjector
{
    public const string FlightTopic = "flight-events";
    public const string BaggageTopic = "baggage-events";
    public const string GateTopic = "gate-events";

    public static AirportView Apply(AirportView view, FlightEvent e)
    {
        if (view.Seen.Contains(e.EventId))
        {
            return view;
        }

        // A row loaded from svc-flight-status on startup is keyed by flight number; the first event for that flight
        // takes it over (keeping its gate, belt and delay) and the event's own values win.
        var snapshotKey = FlightRow.SnapshotKey(e.FlightNumber);
        var existing = view.Flights.GetValueOrDefault(e.FlightId) ?? view.Flights.GetValueOrDefault(snapshotKey);
        var delay = e.EventType == FlightEventType.Delayed ? e.DelayMinutes ?? existing?.DelayMinutes ?? 0 : existing?.DelayMinutes ?? 0;
        var row = new FlightRow(
            e.FlightId, e.FlightNumber, e.Origin, e.Destination, e.ScheduledDeparture,
            // A delay is shown as the status until the next lifecycle step; the minutes stay on the row.
            e.EventType, delay, existing?.Gate, existing?.Belt, e.OccurredAt);

        var summary = e.EventType switch
        {
            FlightEventType.Delayed => $"{e.Origin}→{e.Destination} delayed {e.DelayMinutes ?? 0} min{Reason(e.Reason)}",
            FlightEventType.Cancelled => $"{e.Origin}→{e.Destination} cancelled{Reason(e.Reason)}",
            _ => $"{e.Origin}→{e.Destination} {e.EventType.ToString().ToLowerInvariant()}",
        };

        return Record(view with { Flights = view.Flights.Remove(snapshotKey).SetItem(e.FlightId, row) }, e.EventId, e.OccurredAt, FlightTopic, e.EventType.ToString(), e.FlightNumber, summary);
    }

    public static AirportView Apply(AirportView view, GateEventMessage e)
    {
        if (view.Seen.Contains(e.EventId))
        {
            return view;
        }

        var gates = view.Gates;
        if (e.PreviousGate is { } previous && gates.TryGetValue(previous, out var old) && old.FlightNumber == e.FlightNumber)
        {
            gates = gates.SetItem(previous, old with { FlightNumber = null, OnBlock = null, OffBlock = null, UpdatedAt = e.OccurredAt });
        }

        gates = e.EventType == GateEventType.GateReleased
            ? gates.SetItem(e.Gate, new GateTile(e.Gate, null, null, null, e.OccurredAt))
            : gates.SetItem(e.Gate, new GateTile(e.Gate, e.FlightNumber, e.OnBlock, e.OffBlock, e.OccurredAt));

        var flights = view.Flights;
        var key = e.FlightId ?? flights.Values.FirstOrDefault(f => f.FlightNumber == e.FlightNumber)?.FlightId;
        if (key is not null && flights.TryGetValue(key, out var flight))
        {
            flights = flights.SetItem(key, flight with { Gate = e.EventType == GateEventType.GateReleased ? flight.Gate : e.Gate });
        }

        var summary = e.EventType switch
        {
            GateEventType.GateAssigned => $"gate {e.Gate}{Window(e)}",
            GateEventType.GateChanged when e.PreviousGate is not null => $"gate {e.PreviousGate} → {e.Gate}{Window(e)}",
            GateEventType.GateChanged => $"gate {e.Gate} retimed{Window(e)}",
            _ => $"gate {e.Gate} released",
        };

        return Record(view with { Gates = gates, Flights = flights }, e.EventId, e.OccurredAt, GateTopic, e.EventType.ToString(), e.FlightNumber, summary);
    }

    public static AirportView Apply(AirportView view, BaggageEventMessage e)
    {
        if (view.Seen.Contains(e.EventId))
        {
            return view;
        }

        var current = view.Reclaims.GetValueOrDefault(e.FlightId)
            ?? new BeltFlight(e.FlightId, e.FlightNumber, e.Belt, ReclaimStage.Assigned, null, null, 0, e.OccurredAt, null, null, e.OccurredAt);
        var next = e.EventType switch
        {
            BaggageEventType.BeltAssigned => current with { Belt = e.Belt },
            BaggageEventType.BagsOnBelt => current with { Stage = Max(current.Stage, ReclaimStage.OnBelt), BagCount = e.BagCount ?? current.BagCount, FirstBagAt = e.OccurredAt },
            BaggageEventType.LastBagOnBelt => current with { Stage = Max(current.Stage, ReclaimStage.LastBag), LastBagAt = e.OccurredAt },
            BaggageEventType.ReclaimComplete => current with { Stage = ReclaimStage.Complete, BagCount = e.BagCount ?? current.BagCount, BagsCollected = e.BagsCollected },
            BaggageEventType.BagDelayed => current with { BagsDelayed = current.BagsDelayed + 1 },
            _ => current,
        };
        next = next with { UpdatedAt = e.OccurredAt };

        var flights = view.Flights;
        var flightKey = flights.ContainsKey(e.FlightId) ? e.FlightId : FlightRow.SnapshotKey(e.FlightNumber);
        if (flights.TryGetValue(flightKey, out var flight))
        {
            flights = flights.SetItem(flightKey, flight with { Belt = e.Belt });
        }

        var summary = e.EventType switch
        {
            BaggageEventType.BeltAssigned => $"belt {e.Belt}",
            BaggageEventType.BagsOnBelt => $"first bags on belt {e.Belt} ({e.BagCount} expected)",
            BaggageEventType.LastBagOnBelt => $"last bag on belt {e.Belt}",
            BaggageEventType.ReclaimComplete => $"belt {e.Belt} clear: {e.BagsCollected}/{e.BagCount} collected, {e.BagsMissing ?? 0} missing",
            BaggageEventType.BagDelayed => $"bag {e.BagTag} delayed{Reason(e.Reason)}",
            _ => e.EventType.ToString(),
        };

        return Record(view with { Reclaims = view.Reclaims.SetItem(e.FlightId, next), Flights = flights }, e.EventId, e.OccurredAt, BaggageTopic, e.EventType.ToString(), e.FlightNumber, summary);
    }

    /// <summary>
    /// Merge what the service APIs returned on startup or refresh (#74). Live data wins: anything an event has already
    /// put in the view is kept, and only earlier snapshot rows are replaced. Sources that failed leave their part of
    /// the view as it was. The event stream and de-duplication memory are not touched.
    /// </summary>
    public static AirportView Hydrate(AirportView view, AirportSnapshot snapshot)
    {
        var flights = view.Flights;
        var live = flights.Values.Where(f => !f.FromSnapshot).Select(f => f.FlightNumber).ToHashSet(StringComparer.Ordinal);
        flights = MergeFlights(flights, live, snapshot.Departures, f => Same(f.Origin, snapshot.HomeAirport));
        flights = MergeFlights(flights, live, snapshot.Arrivals, f => Same(f.Destination, snapshot.HomeAirport));

        var gates = snapshot.Gates.Match(
            tiles => tiles.Aggregate(view.Gates, (acc, tile) =>
                !acc.TryGetValue(tile.Gate, out var current) || current.FromSnapshot ? acc.SetItem(tile.Gate, tile) : acc),
            _ => view.Gates);

        var reclaims = snapshot.Belts.Match(
            loaded => loaded.Aggregate(
                view.Reclaims.RemoveRange(view.Reclaims.Values.Where(r => r.FromSnapshot).Select(r => r.FlightId)),
                (acc, r) => acc.ContainsKey(r.FlightId) ? acc : acc.SetItem(r.FlightId, r)),
            _ => view.Reclaims);

        // Arrivals from svc-flight-status don't carry a belt; take it from the reclaim if the row has none.
        flights = reclaims.Values.Aggregate(flights, (acc, r) =>
        {
            var key = acc.ContainsKey(r.FlightId) ? r.FlightId : FlightRow.SnapshotKey(r.FlightNumber);
            return acc.TryGetValue(key, out var row) && row.Belt is null ? acc.SetItem(key, row with { Belt = r.Belt }) : acc;
        });

        var failed = snapshot.Failures;
        var status = new SnapshotStatus(
            snapshot.LoadedAt,
            new[] { SnapshotSources.Departures, SnapshotSources.Arrivals, SnapshotSources.Gates, SnapshotSources.Belts }
                .Where(s => !failed.ContainsKey(s))
                .ToImmutableSortedSet(StringComparer.Ordinal),
            failed);

        return view with { Flights = flights, Gates = gates, Reclaims = reclaims, Snapshot = status, Version = view.Version + 1 };
    }

    private static ImmutableDictionary<string, FlightRow> MergeFlights(
        ImmutableDictionary<string, FlightRow> flights,
        HashSet<string> live,
        SourceLoad<ImmutableList<FlightRow>> source,
        Func<FlightRow, bool> inDirection) =>
        source.Match(
            rows => rows
                .Where(r => !live.Contains(r.FlightNumber))
                .Aggregate(
                    flights.RemoveRange(flights.Values.Where(f => f.FromSnapshot && inDirection(f)).Select(f => f.FlightId)),
                    (acc, r) => acc.SetItem(r.FlightId, r)),
            _ => flights);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Drop finished flights and completed reclaims after their retention. Returns the same view if nothing expired.</summary>
    public static AirportView Expire(AirportView view, DateTimeOffset now)
    {
        var oldFlights = view.Flights.Values
            .Where(f => f.Status is FlightEventType.Departed or FlightEventType.Landed or FlightEventType.Cancelled
                        && now - f.UpdatedAt > AirportView.FinishedFlightRetention)
            .Select(f => f.FlightId)
            .ToList();
        var oldReclaims = view.Reclaims.Values
            .Where(r => r.Stage == ReclaimStage.Complete && now - r.UpdatedAt > AirportView.CompletedReclaimRetention)
            .Select(r => r.FlightId)
            .ToList();

        return oldFlights.Count == 0 && oldReclaims.Count == 0
            ? view
            : view with
            {
                Flights = view.Flights.RemoveRange(oldFlights),
                Reclaims = view.Reclaims.RemoveRange(oldReclaims),
                Version = view.Version + 1,
            };
    }

    private static AirportView Record(AirportView view, Guid id, DateTimeOffset at, string topic, string type, string flight, string summary)
    {
        var stream = view.Stream.Insert(0, new EventRow(id, at, topic, type, flight, summary));
        if (stream.Count > AirportView.StreamLength)
        {
            stream = stream.RemoveRange(AirportView.StreamLength, stream.Count - AirportView.StreamLength);
        }

        var order = view.SeenOrder.Enqueue(id);
        var seen = view.Seen.Add(id);
        while (seen.Count > AirportView.SeenMemory)
        {
            order = order.Dequeue(out var oldest);
            seen = seen.Remove(oldest);
        }

        return view with { Stream = stream, Seen = seen, SeenOrder = order, Version = view.Version + 1 };
    }

    private static ReclaimStage Max(ReclaimStage a, ReclaimStage b) => a > b ? a : b;

    private static string Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";

    private static string Window(GateEventMessage e) =>
        e.OnBlock is { } on && e.OffBlock is { } off ? $" {on:HH:mm}–{off:HH:mm}" : "";
}
