using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace AeroFlow.Local.OpsDashboard.Health;

/// <summary>Binds the <c>HealthStrip</c> section: which services to poll and how often.</summary>
public sealed class HealthStripOptions
{
    public const string SectionName = "HealthStrip";

    /// <summary>Aspire resource names; each is polled at <c>http://{name}/health</c> via service discovery.</summary>
    public List<string> Services { get; set; } = [];

    public double PollSeconds { get; set; } = 10;
}

public enum ServiceHealth
{
    Unknown,
    Healthy,
    Degraded,
    Unreachable,
}

public sealed record ServiceStatus(string Name, ServiceHealth Health, DateTimeOffset? CheckedAt);

/// <summary>
/// Background poller behind the health strip. Holds an immutable snapshot and raises <see cref="Changed"/>
/// so components can re-render. Read-only: it only calls <c>GET /health</c>.
/// </summary>
public sealed class ServiceHealthMonitor(
    IHttpClientFactory httpClients,
    IOptions<HealthStripOptions> options,
    TimeProvider clock,
    ILogger<ServiceHealthMonitor> logger) : BackgroundService
{
    public const string HttpClientName = "health-strip";

    private ImmutableList<ServiceStatus> _snapshot =
        options.Value.Services.Select(s => new ServiceStatus(s, ServiceHealth.Unknown, null)).ToImmutableList();

    public event Action? Changed;

    public IReadOnlyList<ServiceStatus> Snapshot => Volatile.Read(ref _snapshot);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Services.Count == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), clock);
        do
        {
            await PollAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var client = httpClients.CreateClient(HttpClientName);
        var checks = options.Value.Services.Select(async name =>
        {
            try
            {
                using var response = await client.GetAsync(new Uri($"http://{name}/health"), cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var health = response.IsSuccessStatusCode && body.Contains("Healthy", StringComparison.OrdinalIgnoreCase)
                    ? ServiceHealth.Healthy
                    : ServiceHealth.Degraded;
                return new ServiceStatus(name, health, clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogDebug(ex, "Health check for {Service} failed", name);
                return new ServiceStatus(name, ServiceHealth.Unreachable, clock.GetUtcNow());
            }
        });

        var results = await Task.WhenAll(checks);
        Volatile.Write(ref _snapshot, results.ToImmutableList());
        Changed?.Invoke();
    }
}
