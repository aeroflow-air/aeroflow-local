using System.Collections.Immutable;
using AeroFlow.Local.FlightEvents;

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

        var existing = view.Flights.GetValueOrDefault(e.FlightId);
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

        return Record(view with { Flights = view.Flights.SetItem(e.FlightId, row) }, e.EventId, e.OccurredAt, FlightTopic, e.EventType.ToString(), e.FlightNumber, summary);
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
        if (flights.TryGetValue(e.FlightId, out var flight))
        {
            flights = flights.SetItem(e.FlightId, flight with { Belt = e.Belt });
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
