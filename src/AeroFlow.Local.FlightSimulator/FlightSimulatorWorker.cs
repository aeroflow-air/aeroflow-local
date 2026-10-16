using System.Diagnostics;
using AeroFlow.Local.FlightEvents;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AeroFlow.Local.FlightSimulator;

public sealed partial class FlightSimulatorWorker(
    ServiceBusClient client,
    IOptionsMonitor<SimulatorOptions> options,
    TimeProvider clock,
    ILogger<FlightSimulatorWorker> logger) : BackgroundService
{
    // Field needed by the .NET 8 [LoggerMessage] generator (it doesn't read primary-ctor params).
    private readonly ILogger _logger = logger;

    public static readonly ActivitySource ActivitySource = new("AeroFlow.Local.FlightSimulator");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = options.CurrentValue;
        var generator = new FlightGenerator(initial.Seed, clock);
        await using var sender = client.CreateSender(FlightEventTopics.FlightEvents);
        LogStarted(initial.Seed, initial.TickSeconds, initial.Enabled);

        // Events the bus hasn't accepted yet, oldest first. A failed send keeps them here and the next tick retries
        // them (same EventId, so duplicate detection drops any copy that did get through), so none are lost (ADR-0017).
        var pending = new Queue<FlightEvent>();
        var wasPaused = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            if (!current.Enabled)
            {
                if (!wasPaused)
                {
                    LogPaused();
                    wasPaused = true;
                }
            }
            else
            {
                if (wasPaused)
                {
                    LogResumed();
                    wasPaused = false;
                }

                await TickAsync(generator, sender, current, pending, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, current.TickSeconds)), clock, stoppingToken)
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private async Task TickAsync(FlightGenerator generator, ServiceBusSender sender, SimulatorOptions current, Queue<FlightEvent> pending, CancellationToken ct)
    {
        foreach (var evt in generator.Tick(current))
        {
            pending.Enqueue(evt);
        }

        if (pending.Count == 0)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("simulator tick", ActivityKind.Producer);
        activity?.SetTag("aeroflow.events.count", pending.Count);
        activity?.SetTag("aeroflow.flights.active", generator.ActiveCount);

        try
        {
            while (pending.TryPeek(out var evt))
            {
                var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(evt, FlightEventJson.Options))
                {
                    MessageId = evt.EventId.ToString(),
                    CorrelationId = evt.FlightId,
                    Subject = evt.EventType.ToString(),
                    ContentType = "application/json",
                };
                message.ApplicationProperties["eventType"] = evt.EventType.ToString();
                message.ApplicationProperties["flightNumber"] = evt.FlightNumber;

                await sender.SendMessageAsync(message, ct);
                pending.Dequeue();
                LogPublished(evt.EventType, evt.FlightNumber, evt.Origin, evt.Destination, evt.FlightId);
            }
        }
        catch (Exception ex) when (ex is ServiceBusException or TimeoutException)
        {
            // Transient (e.g. emulator restarting); keep the loop alive and try again next tick.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogPublishFailed(ex, pending.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Flight simulator started (seed {Seed}, tick {TickSeconds}s, enabled {Enabled})")]
    private partial void LogStarted(int seed, double tickSeconds, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} for {FlightNumber} {Origin}->{Destination} ({FlightId})")]
    private partial void LogPublished(FlightEventType eventType, string flightNumber, string origin, string destination, string flightId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flight simulator paused (Simulator:Enabled=false)")]
    private partial void LogPaused();

    [LoggerMessage(Level = LogLevel.Information, Message = "Flight simulator resumed")]
    private partial void LogResumed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing flight events failed; {Pending} pending, will retry next tick")]
    private partial void LogPublishFailed(Exception ex, int pending);
}
