namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>Binds the <c>LiveEvents</c> section.</summary>
public sealed class LiveEventsOptions
{
    public const string SectionName = "LiveEvents";

    /// <summary>Aspire connection name; listeners only start when <c>ConnectionStrings:messaging</c> is set.</summary>
    public const string ConnectionName = "messaging";

    /// <summary>Subscription name used on every topic (created by the AppHost).</summary>
    public string Subscription { get; set; } = "ops-dashboard";

    public string FlightTopic { get; set; } = AirportProjector.FlightTopic;

    public string BaggageTopic { get; set; } = AirportProjector.BaggageTopic;

    public string GateTopic { get; set; } = AirportProjector.GateTopic;
}
