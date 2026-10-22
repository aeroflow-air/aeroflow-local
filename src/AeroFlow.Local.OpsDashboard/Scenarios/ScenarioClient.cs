using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AeroFlow.Local.OpsDashboard.Scenarios;

public sealed class ScenarioOptions
{
    public const string SectionName = "Scenarios";

    /// <summary>Base URL of the flight simulator (Aspire service discovery name by default).</summary>
    public string SimulatorUrl { get; set; } = "http://flight-simulator";
}

/// <summary>Copy of the simulator's <c>GET /scenarios/flights</c> item.</summary>
public sealed record DelayableFlightDto(string FlightId, string FlightNumber, string Origin, string Destination, DateTimeOffset ScheduledDeparture, int DelayMinutes);

public sealed record ScenarioOutcome(bool Ok, string Message);

/// <summary>Edge: calls the flight simulator's demo scenario endpoints.</summary>
public sealed class ScenarioClient(IHttpClientFactory httpClients, IOptions<ScenarioOptions> options)
{
    public const string HttpClientName = "scenarios";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => httpClients.CreateClient(HttpClientName);
    private string Base => options.Value.SimulatorUrl.TrimEnd('/');

    public async Task<IReadOnlyList<DelayableFlightDto>> FlightsAsync(CancellationToken ct)
    {
        try
        {
            return await Client.GetFromJsonAsync<List<DelayableFlightDto>>($"{Base}/scenarios/flights", Json, ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async Task<ScenarioOutcome> DelayAsync(string flightNumber, int minutes, string? reason, CancellationToken ct)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync($"{Base}/scenarios/delay", new { flightNumber, minutes, reason }, Json, ct);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                return new ScenarioOutcome(true, $"{flightNumber} delayed +{minutes} min; watch the board and gates update.");
            }

            var error = await response.Content.ReadFromJsonAsync<ErrorDto>(Json, ct);
            return new ScenarioOutcome(false, error?.Error ?? $"simulator returned {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ScenarioOutcome(false, $"flight simulator unreachable: {ex.Message}");
        }
    }

    private sealed record ErrorDto(string? Error);
}

public static class ScenarioExtensions
{
    public static IHostApplicationBuilder AddScenarios(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ScenarioOptions>(builder.Configuration.GetSection(ScenarioOptions.SectionName));
        builder.Services.AddHttpClient(ScenarioClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<ScenarioClient>();
        return builder;
    }
}
