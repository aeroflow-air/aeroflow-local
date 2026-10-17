using System.Collections.Immutable;
using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.OpsDashboard.Snapshot;

namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>One row on the departures/arrivals board, keyed by the simulator's flight id.</summary>
public sealed record FlightRow(
    string FlightId,
    string FlightNumber,
    string Origin,
    string Destination,
    DateTimeOffset ScheduledDeparture,
    FlightEventType Status,
    int DelayMinutes,
    string? Gate,
    string? Belt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>True when the row came from a service API on startup/refresh (#74), not from an event.</summary>
    public bool FromSnapshot { get; init; }

    /// <summary>Key used for rows loaded from svc-flight-status, which knows flight numbers but not the simulator's ids.</summary>
    public static string SnapshotKey(string flightNumber) => $"api:{flightNumber}";
}

public enum GateTileState
{
    Open,
    Closed,
    Occupied,
}

/// <summary>A gate the dashboard has seen. Open when free, closed when svc-gate-allocation says so, else occupied.</summary>
public sealed record GateTile(string Gate, string? FlightNumber, DateTimeOffset? OnBlock, DateTimeOffset? OffBlock, DateTimeOffset UpdatedAt)
{
    /// <summary>True when the tile came from svc-gate-allocation's API, not from a gate event.</summary>
    public bool FromSnapshot { get; init; }

    /// <summary>Closed gates only come from the gate API snapshot; no gate event carries a closure.</summary>
    public bool Closed { get; init; }

    public string? ClosedReason { get; init; }

    public GateTileState State => Closed ? GateTileState.Closed : FlightNumber is null ? GateTileState.Open : GateTileState.Occupied;
}

public enum ReclaimStage
{
    Assigned,
    OnBelt,
    LastBag,
    Complete,
}

/// <summary>One flight's reclaim on a belt.</summary>
public sealed record BeltFlight(
    string FlightId,
    string FlightNumber,
    string Belt,
    ReclaimStage Stage,
    int? BagCount,
    int? BagsCollected,
    int BagsDelayed,
    DateTimeOffset AssignedAt,
    DateTimeOffset? FirstBagAt,
    DateTimeOffset? LastBagAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>True when the reclaim came from svc-baggage-reclaim's API, not from a baggage event.</summary>
    public bool FromSnapshot { get; init; }

    /// <summary>
    /// Bag progress as a percentage. Real progress (collected of expected) when both counts are known — the
    /// belt API reports them; live events only carry counts at completion, so mid-reclaim this falls back to
    /// stage progress until a snapshot refresh fills the counts in.
    /// </summary>
    public int ProgressPercent => BagCount is > 0 && BagsCollected is { } collected
        ? Math.Clamp(collected * 100 / BagCount.Value, 0, 100)
        : Stage switch
        {
            ReclaimStage.Assigned => 5,
            ReclaimStage.OnBelt => 40,
            ReclaimStage.LastBag => 80,
            _ => 100,
        };
}

/// <summary>A line in the event stream.</summary>
public sealed record EventRow(Guid EventId, DateTimeOffset At, string Topic, string Type, string FlightNumber, string Summary);

/// <summary>
/// Immutable picture of the live airport: seeded from the service APIs on startup (#74), then kept current by events.
/// Every change goes through <see cref="AirportProjector"/>.
/// </summary>
public sealed record AirportView
{
    public const int StreamLength = 200;
    public const int SeenMemory = 5_000;

    /// <summary>Completed reclaims stay on the belt panel this long, then drop off.</summary>
    public static readonly TimeSpan CompletedReclaimRetention = TimeSpan.FromMinutes(2);

    /// <summary>Flights that departed, landed or were cancelled stay on the board this long.</summary>
    public static readonly TimeSpan FinishedFlightRetention = TimeSpan.FromMinutes(10);

    public static readonly AirportView Empty = new();

    public ImmutableDictionary<string, FlightRow> Flights { get; init; } = ImmutableDictionary<string, FlightRow>.Empty;

    public ImmutableSortedDictionary<string, GateTile> Gates { get; init; } =
        ImmutableSortedDictionary.Create<string, GateTile>(GateOrder.Instance);

    public ImmutableDictionary<string, BeltFlight> Reclaims { get; init; } = ImmutableDictionary<string, BeltFlight>.Empty;

    /// <summary>Newest first, at most <see cref="StreamLength"/>.</summary>
    public ImmutableList<EventRow> Stream { get; init; } = [];

    public ImmutableHashSet<Guid> Seen { get; init; } = [];

    public ImmutableQueue<Guid> SeenOrder { get; init; } = [];

    public long Version { get; init; }

    /// <summary>What the last startup/refresh load from the service APIs found (#74).</summary>
    public SnapshotStatus Snapshot { get; init; } = SnapshotStatus.NotLoaded;

    public IEnumerable<IGrouping<string, BeltFlight>> Belts =>
        Reclaims.Values.OrderBy(r => r.AssignedAt).GroupBy(r => r.Belt).OrderBy(g => g.Key, GateOrder.Instance);

    /// <summary>Orders "2" before "10" and "55A" after "55".</summary>
    public sealed class GateOrder : IComparer<string>
    {
        public static readonly GateOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var (nx, sx) = Split(x ?? "");
            var (ny, sy) = Split(y ?? "");
            var byNumber = nx.CompareTo(ny);
            return byNumber != 0 ? byNumber : string.CompareOrdinal(sx, sy);
        }

        private static (int Number, string Suffix) Split(string value)
        {
            var digits = new string(value.TakeWhile(char.IsAsciiDigit).ToArray());
            return (digits.Length > 0 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue, value[digits.Length..]);
        }
    }
}
