using System.Collections.Immutable;
using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Live;

namespace AeroFlow.Local.OpsDashboard.Snapshot;

/// <summary>Pure mapping from the services' read APIs onto dashboard rows. No I/O, no clock: time is passed in.</summary>
public static class SnapshotMapper
{
    public static ImmutableList<FlightRow> Departures(DeparturesBoardDto board) =>
        board.Departures
            .SelectMany(d => Status(d.Status)
                .Select(status => new FlightRow(
                    FlightRow.SnapshotKey(d.FlightNumber), d.FlightNumber, board.Airport, d.Destination, d.ScheduledDeparture,
                    status, d.DelayMinutes, d.Gate, null, board.GeneratedAt) { FromSnapshot = true }))
            .ToImmutableList();

    public static ImmutableList<FlightRow> Arrivals(ArrivalsBoardDto board) =>
        board.Arrivals
            .SelectMany(a => Status(a.Status)
                .Select(status => new FlightRow(
                    FlightRow.SnapshotKey(a.FlightNumber), a.FlightNumber, a.Origin, board.Airport, a.ScheduledDeparture,
                    status, a.DelayMinutes, null, null, board.GeneratedAt) { FromSnapshot = true }))
            .ToImmutableList();

    /// <summary>
    /// One tile per gate: closed gates are shown closed (with their reason); for open gates the allocation on
    /// block at <paramref name="now"/>, else the next one coming, else open.
    /// </summary>
    public static ImmutableList<GateTile> Gates(IEnumerable<GateDto> gates, DateTimeOffset now) =>
        gates
            .Select(g =>
            {
                if (string.Equals(g.Status, "Closed", StringComparison.OrdinalIgnoreCase))
                {
                    return new GateTile(g.Id, null, null, null, now) { FromSnapshot = true, Closed = true, ClosedReason = g.ClosedReason };
                }

                var showing = g.Allocations.Where(a => a.OnBlock <= now && now < a.OffBlock)
                    .Concat(g.Allocations.Where(a => a.OnBlock > now).OrderBy(a => a.OnBlock))
                    .Take(1)
                    .ToList();
                return showing.Count == 0
                    ? new GateTile(g.Id, null, null, null, now) { FromSnapshot = true }
                    : new GateTile(g.Id, showing[0].FlightNumber, showing[0].OnBlock, showing[0].OffBlock, now) { FromSnapshot = true };
            })
            .ToImmutableList();

    /// <summary>Every reclaim on every belt. First/last-bag times aren't in the API, so they stay empty until events arrive.</summary>
    public static ImmutableList<BeltFlight> Belts(IEnumerable<BeltDto> belts, DateTimeOffset now) =>
        belts
            .SelectMany(b => b.Reclaims.Select(r => new BeltFlight(
                r.FlightId, r.FlightNumber, b.Id, Stage(r.Stage), r.BagCount,
                r.BagsCollected,
                r.BagsDelayed, r.LandedAt, null, null, now) { FromSnapshot = true }))
            .ToImmutableList();

    /// <summary>Flight-status states the board can show. Diverted has no board status, so the flight is skipped.</summary>
    private static IEnumerable<FlightEventType> Status(string status) =>
        Enum.TryParse<FlightEventType>(status, ignoreCase: true, out var parsed) ? [parsed] : [];

    private static ReclaimStage Stage(string stage) =>
        Enum.TryParse<ReclaimStage>(stage, ignoreCase: true, out var parsed) ? parsed : ReclaimStage.Assigned;
}
