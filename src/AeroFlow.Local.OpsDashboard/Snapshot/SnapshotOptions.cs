namespace AeroFlow.Local.OpsDashboard.Snapshot;

/// <summary>Binds the <c>Snapshot</c> section: where the startup state comes from (#74).</summary>
public sealed class SnapshotOptions
{
    public const string SectionName = "Snapshot";

    /// <summary>Airport the board shows.</summary>
    public string HomeAirport { get; set; } = "LHR";

    /// <summary>Base URLs, resolved by Aspire service discovery from the AppHost's <c>WithReference</c> links.</summary>
    public string FlightStatusUrl { get; set; } = "http://svc-flight-status";

    public string GateAllocationUrl { get; set; } = "http://svc-gate-allocation";

    public string BaggageReclaimUrl { get; set; } = "http://svc-baggage-reclaim";

    /// <summary>
    /// The services may still be starting when the dashboard does (the AppHost doesn't make it wait for them), so
    /// a load with failed sources is retried this often, up to <see cref="MaxAttempts"/> times.
    /// </summary>
    public double RetrySeconds { get; set; } = 5;

    public int MaxAttempts { get; set; } = 24;

    public double TimeoutSeconds { get; set; } = 5;
}
