using System.Net;
using System.Text;
using AeroFlow.Local.OpsDashboard.Live;
using AeroFlow.Local.OpsDashboard.Snapshot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroFlow.Local.OpsDashboard.Tests;

public sealed class SnapshotLoaderTests
{
    [Fact]
    public async Task Loads_every_source_through_the_service_names_and_tolerates_one_being_down()
    {
        var requested = new List<string>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            requested.Add(url);
            return url switch
            {
                "http://svc-flight-status/api/flights/departures/LHR" =>
                    Json("""{"airport":"LHR","generatedAt":"2026-10-14T18:00:00Z","departures":[{"flightNumber":"BA1","destination":"AMS","scheduledDeparture":"2026-10-14T19:00:00Z","gate":"12","status":"Scheduled","delayMinutes":0}]}"""),
                "http://svc-flight-status/api/flights/arrivals/LHR" =>
                    Json("""{"airport":"LHR","generatedAt":"2026-10-14T18:00:00Z","arrivals":[{"flightNumber":"KL3","origin":"AMS","scheduledDeparture":"2026-10-14T17:00:00Z","status":"Landed","delayMinutes":0}]}"""),
                "http://svc-gate-allocation/api/gates" => throw new HttpRequestException("connection refused"),
                "http://svc-baggage-reclaim/belts" =>
                    Json("""[{"id":"4","capacity":2,"reclaims":[{"flightId":"SIM-3","flightNumber":"KL3","origin":"AMS","landedAt":"2026-10-14T17:50:00Z","stage":"OnBelt","bagCount":90,"bagsOnBelt":10,"bagsCollected":5,"bagsDelayed":0,"completedAt":null}]}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var store = new AirportViewStore();
        var loader = new SnapshotLoader(new StubFactory(handler), store, Options.Create(new SnapshotOptions()), TimeProvider.System, NullLogger<SnapshotLoader>.Instance);

        var snapshot = await loader.LoadAsync(CancellationToken.None);

        Assert.Equal(4, requested.Count);
        Assert.Equal(["gates"], snapshot.Failures.Keys);
        var view = store.Current;
        Assert.Equal(["api:BA1", "api:KL3"], view.Flights.Keys.Order());
        Assert.Equal("4", view.Flights["api:KL3"].Belt);
        Assert.Equal(ReclaimStage.OnBelt, view.Reclaims["SIM-3"].Stage);
        Assert.Empty(view.Gates);
        Assert.Equal(["gates"], view.Snapshot.Failed.Keys);
    }

    [Fact]
    public async Task A_non_success_status_or_bad_body_is_a_failed_source_not_an_exception()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.StartsWith("/api/flights", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json("not json"));
        var store = new AirportViewStore();
        var loader = new SnapshotLoader(new StubFactory(handler), store, Options.Create(new SnapshotOptions()), TimeProvider.System, NullLogger<SnapshotLoader>.Instance);

        var snapshot = await loader.LoadAsync(CancellationToken.None);

        Assert.Equal(["arrivals", "belts", "departures", "gates"], snapshot.Failures.Keys.Order());
        Assert.Empty(store.Current.Flights);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
