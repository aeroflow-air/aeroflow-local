using System.Collections.Immutable;
using AeroFlow.Local.OpsDashboard.Live;

namespace AeroFlow.Local.OpsDashboard.Snapshot;

/// <summary>The outcome of loading one source: the rows it gave, or why it couldn't be read. Closed: Loaded or Failed.</summary>
public abstract record SourceLoad<T>
{
    private SourceLoad()
    {
    }

    public sealed record Loaded(T Value) : SourceLoad<T>;

    public sealed record Failed(string Reason) : SourceLoad<T>;

    public TOut Match<TOut>(Func<T, TOut> loaded, Func<string, TOut> failed) => this switch
    {
        Loaded l => loaded(l.Value),
        Failed f => failed(f.Reason),
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}

/// <summary>Names of the sources, as shown in the dashboard's snapshot status.</summary>
public static class SnapshotSources
{
    public const string Departures = "departures";
    public const string Arrivals = "arrivals";
    public const string Gates = "gates";
    public const string Belts = "belts";
}

/// <summary>Everything one load from the service APIs returned. Each source loads or fails on its own.</summary>
public sealed record AirportSnapshot(
    DateTimeOffset LoadedAt,
    string HomeAirport,
    SourceLoad<ImmutableList<FlightRow>> Departures,
    SourceLoad<ImmutableList<FlightRow>> Arrivals,
    SourceLoad<ImmutableList<GateTile>> Gates,
    SourceLoad<ImmutableList<BeltFlight>> Belts)
{
    public ImmutableDictionary<string, string> Failures =>
        new (string Name, string? Reason)[]
            {
                (SnapshotSources.Departures, Departures.Match<string?>(_ => null, r => r)),
                (SnapshotSources.Arrivals, Arrivals.Match<string?>(_ => null, r => r)),
                (SnapshotSources.Gates, Gates.Match<string?>(_ => null, r => r)),
                (SnapshotSources.Belts, Belts.Match<string?>(_ => null, r => r)),
            }
            .Where(s => s.Reason is not null)
            .ToImmutableDictionary(s => s.Name, s => s.Reason!);
}

/// <summary>What the dashboard last loaded from the APIs. <see cref="NotLoaded"/> until the first attempt finishes.</summary>
public sealed record SnapshotStatus(DateTimeOffset? At, ImmutableSortedSet<string> Loaded, ImmutableDictionary<string, string> Failed)
{
    public static readonly SnapshotStatus NotLoaded = new(null, [], ImmutableDictionary<string, string>.Empty);
}
