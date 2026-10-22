using System.Net;
using System.Text;
using AeroFlow.Local.OpsDashboard.Scenarios;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class ScenarioClientTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static ScenarioClient Client(Stub stub) =>
        new(new Factory(stub), Options.Create(new ScenarioOptions { SimulatorUrl = "http://sim/" }));

    [Fact]
    public async Task Delay_posts_to_the_simulator_and_reports_success()
    {
        var stub = new Stub(HttpStatusCode.Accepted, "{}");
        var outcome = await Client(stub).DelayAsync("BA123", 30, null, CancellationToken.None);
        Assert.True(outcome.Ok);
        Assert.Equal("http://sim/scenarios/delay", stub.Last!.RequestUri!.ToString());
        Assert.Contains("\"flightNumber\":\"BA123\"", stub.LastBody);
        Assert.Contains("\"minutes\":30", stub.LastBody);
    }

    [Fact]
    public async Task Delay_surfaces_the_simulator_error()
    {
        var outcome = await Client(new Stub(HttpStatusCode.BadRequest, "{\"error\":\"BA123 is Boarding and can no longer be delayed\"}"))
            .DelayAsync("BA123", 30, null, CancellationToken.None);
        Assert.False(outcome.Ok);
        Assert.Contains("Boarding", outcome.Message);
    }

    [Fact]
    public async Task Flights_reads_the_simulator_list_and_is_empty_when_unreachable()
    {
        var flights = await Client(new Stub(HttpStatusCode.OK,
            "[{\"flightId\":\"SIM-00001\",\"flightNumber\":\"BA123\",\"origin\":\"LHR\",\"destination\":\"AMS\",\"scheduledDeparture\":\"2026-10-22T21:00:00Z\",\"delayMinutes\":0}]"))
            .FlightsAsync(CancellationToken.None);
        Assert.Equal("BA123", Assert.Single(flights).FlightNumber);

        Assert.Empty(await Client(new Stub(HttpStatusCode.InternalServerError, "oops")).FlightsAsync(CancellationToken.None));
    }
}
