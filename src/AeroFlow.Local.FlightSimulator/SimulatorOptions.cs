namespace AeroFlow.Local.FlightSimulator;

/// <summary>Bound from the <c>Simulator</c> config section (appsettings or <c>Simulator__*</c> env vars). Reloads live.</summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    /// <summary>Set false to pause publishing (the loop keeps running and resumes when re-enabled).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between simulation ticks. Each tick may schedule a flight and advances every active flight one step.</summary>
    public double TickSeconds { get; set; } = 5;

    /// <summary>Chance (0–1) of scheduling a new flight per tick.</summary>
    public double NewFlightProbability { get; set; } = 0.6;

    /// <summary>Cap on flights in progress at once.</summary>
    public int MaxActiveFlights { get; set; } = 8;

    /// <summary>Chance a scheduled flight is delayed before boarding.</summary>
    public double DelayProbability { get; set; } = 0.15;

    /// <summary>Chance a scheduled or delayed flight is cancelled.</summary>
    public double CancelProbability { get; set; } = 0.05;

    /// <summary>Random seed; the same seed yields the same flight sequence (repeatable demos).</summary>
    public int Seed { get; set; } = 42;
}
