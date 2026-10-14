using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using AeroFlow.Local.OpsDashboard.Live;
using Microsoft.Extensions.Options;

namespace AeroFlow.Local.OpsDashboard.Snapshot;

/// <summary>
/// Edge: reads current state from the three services' APIs and merges it into the <see cref="AirportViewStore"/>.
/// Read-only (GET only). Each source loads or fails on its own, so one service being down leaves the rest working.
/// </summary>
public sealed partial class SnapshotLoader(
    IHttpClientFactory httpClients,
    AirportViewStore store,
    IOptions<SnapshotOptions> options,
    TimeProvider clock,
    ILogger<SnapshotLoader> logger)
{
    public const string HttpClientName = "snapshot";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Loads every source once and merges the result. Concurrent calls (startup and the refresh button) queue.</summary>
    public async Task<AirportSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var o = options.Value;
            var client = httpClients.CreateClient(HttpClientName);
            var airport = Uri.EscapeDataString(o.HomeAirport);

            var departures = GetAsync<DeparturesBoardDto>(client, $"{o.FlightStatusUrl}/api/flights/departures/{airport}", cancellationToken);
            var arrivals = GetAsync<ArrivalsBoardDto>(client, $"{o.FlightStatusUrl}/api/flights/arrivals/{airport}", cancellationToken);
            var gates = GetAsync<List<GateDto>>(client, $"{o.GateAllocationUrl}/api/gates", cancellationToken);
            var belts = GetAsync<List<BeltDto>>(client, $"{o.BaggageReclaimUrl}/belts", cancellationToken);
            await Task.WhenAll(departures, arrivals, gates, belts);

            var now = clock.GetUtcNow();
            var snapshot = new AirportSnapshot(
                now,
                o.HomeAirport,
                Map(await departures, SnapshotMapper.Departures),
                Map(await arrivals, SnapshotMapper.Arrivals),
                Map(await gates, g => SnapshotMapper.Gates(g, now)),
                Map(await belts, b => SnapshotMapper.Belts(b, now)));

            store.Update(view => AirportProjector.Hydrate(view, snapshot));
            foreach (var (source, reason) in snapshot.Failures)
            {
                LogSourceFailed(source, reason);
            }

            LogLoaded(4 - snapshot.Failures.Count);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SourceLoad<T>> GetAsync<T>(HttpClient client, string url, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));
            using var response = await client.GetAsync(new Uri(url), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new SourceLoad<T>.Failed($"{url} returned {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<T>(LiveJson.Options, timeout.Token);
            return body is null ? new SourceLoad<T>.Failed($"{url} returned an empty body") : new SourceLoad<T>.Loaded(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SourceLoad<T>.Failed($"{url}: {ex.GetType().Name}");
        }
    }

    private static SourceLoad<TOut> Map<TIn, TOut>(SourceLoad<TIn> load, Func<TIn, TOut> map) =>
        load.Match<SourceLoad<TOut>>(v => new SourceLoad<TOut>.Loaded(map(v)), r => new SourceLoad<TOut>.Failed(r));

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashboard snapshot loaded {Loaded}/4 sources from the service APIs")]
    private partial void LogLoaded(int loaded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard snapshot source {Source} failed: {Reason}")]
    private partial void LogSourceFailed(string source, string reason);
}

/// <summary>
/// Loads the snapshot when the dashboard starts, alongside the Service Bus listeners, and retries while any source
/// fails (services may still be starting). Events that arrive meanwhile win over the snapshot.
/// </summary>
public sealed class SnapshotHydrator(SnapshotLoader loader, IOptions<SnapshotOptions> options, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        for (var attempt = 1; attempt <= Math.Max(1, o.MaxAttempts); attempt++)
        {
            var snapshot = await loader.LoadAsync(stoppingToken);
            if (snapshot.Failures.IsEmpty)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.1, o.RetrySeconds)), clock, stoppingToken);
        }
    }
}

public static class SnapshotExtensions
{
    /// <summary>Registers the loader and the startup hydrator. Needs <see cref="AirportViewStore"/> (from <c>AddLiveAirport</c>).</summary>
    public static IHostApplicationBuilder AddAirportSnapshot(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<SnapshotOptions>(builder.Configuration.GetSection(SnapshotOptions.SectionName));
        builder.Services.AddHttpClient(SnapshotLoader.HttpClientName);
        builder.Services.AddSingleton<SnapshotLoader>();
        builder.Services.AddHostedService<SnapshotHydrator>();
        return builder;
    }
}
