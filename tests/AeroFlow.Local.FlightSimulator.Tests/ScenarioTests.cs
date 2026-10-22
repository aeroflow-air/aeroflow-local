using System.Net;
using System.Net.Http.Json;
using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.FlightSimulator;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AeroFlow.Local.FlightSimulator.Tests;

public sealed class FlightGeneratorScenarioTests
{
    // Deterministic: a new flight every tick, never randomly delayed or cancelled.
    private static readonly SimulatorOptions Calm = new() { NewFlightProbability = 1, DelayProbability = 0, CancelProbability = 0, MaxActiveFlights = 8 };

    private static (FlightGenerator Generator, FlightEvent Scheduled) OneFlight()
    {
        var generator = new FlightGenerator(7, TimeProvider.System);
        var scheduled = Assert.Single(generator.Tick(Calm));
        Assert.Equal(FlightEventType.Scheduled, scheduled.EventType);
        return (generator, scheduled);
    }

    [Fact]
    public void Delay_emits_a_normal_Delayed_event_on_the_next_tick()
    {
        var (generator, f) = OneFlight();

        var result = Assert.IsType<ScenarioResult.Accepted>(generator.Delay(f.FlightNumber, 45, null));
        Assert.Equal(FlightEventType.Delayed, result.Event.EventType);
        Assert.Equal(45, result.Event.DelayMinutes);
        Assert.Equal("Demo scenario", result.Event.Reason);
        Assert.Equal(f.ScheduledDeparture, result.Event.ScheduledDeparture);

        var next = generator.Tick(Calm);
        Assert.Equal(result.Event, next[0]);
        // The delayed flight doesn't also advance to Boarding in the same tick.
        Assert.DoesNotContain(next, e => e.FlightId == f.FlightId && e.EventType == FlightEventType.Boarding);
    }

    [Fact]
    public void Delays_accumulate_and_the_flight_then_carries_on()
    {
        var (generator, f) = OneFlight();
        generator.Delay(f.FlightNumber, 30, "ATC");
        var second = Assert.IsType<ScenarioResult.Accepted>(generator.Delay(f.FlightNumber.ToLowerInvariant(), 15, null));
        Assert.Equal(45, second.Event.DelayMinutes);

        Assert.Equal(2, generator.Tick(Calm).Count(e => e.FlightId == f.FlightId));
        var after = generator.Tick(Calm).Single(e => e.FlightId == f.FlightId);
        Assert.Equal(FlightEventType.Boarding, after.EventType);
        Assert.Equal(45, after.DelayMinutes);
    }

    [Fact]
    public void Unknown_or_boarding_flights_and_bad_minutes_are_rejected()
    {
        var (generator, f) = OneFlight();
        Assert.IsType<ScenarioResult.NotFound>(generator.Delay("ZZ1", 10, null));
        Assert.IsType<ScenarioResult.Invalid>(generator.Delay(f.FlightNumber, 0, null));
        Assert.IsType<ScenarioResult.Invalid>(generator.Delay(f.FlightNumber, 601, null));

        generator.Tick(Calm); // -> Boarding
        Assert.IsType<ScenarioResult.Invalid>(generator.Delay(f.FlightNumber, 10, null));
        Assert.DoesNotContain(generator.Delayable(), d => d.FlightId == f.FlightId);
    }

    [Fact]
    public void Delayable_lists_flights_not_yet_boarding_with_their_delay()
    {
        var (generator, f) = OneFlight();
        generator.Delay(f.FlightNumber, 20, null);
        var d = Assert.Single(generator.Delayable());
        Assert.Equal(f.FlightNumber, d.FlightNumber);
        Assert.Equal(20, d.DelayMinutes);
    }
}

public sealed class ScenarioEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScenarioEndpointTests(WebApplicationFactory<Program> factory)
    {
        // No Service Bus in tests: drop the publishing worker and drive the generator directly.
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(FlightSimulatorWorker)).ToList())
            {
                s.Remove(d);
            }
        }));
    }

    [Fact]
    public async Task Delay_endpoint_accepts_active_flights_and_rejects_bad_requests()
    {
        var generator = _factory.Services.GetRequiredService<FlightGenerator>();
        var f = generator.Tick(new SimulatorOptions { NewFlightProbability = 1, DelayProbability = 0, CancelProbability = 0 }).Single();
        var client = _factory.CreateClient();

        var flights = await client.GetFromJsonAsync<List<DelayableFlight>>("/scenarios/flights");
        Assert.Contains(flights!, d => d.FlightNumber == f.FlightNumber);

        var ok = await client.PostAsJsonAsync("/scenarios/delay", new DelayRequest(f.FlightNumber, 40));
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        var evt = await ok.Content.ReadFromJsonAsync<FlightEvent>(FlightEventJson.Options);
        Assert.Equal(FlightEventType.Delayed, evt!.EventType);
        Assert.Equal(40, evt.DelayMinutes);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/scenarios/delay", new DelayRequest("ZZ1", 10))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/scenarios/delay", new DelayRequest(f.FlightNumber, 0))).StatusCode);
    }
}
