using System.Text.Json;
using AeroFlow.Local.FlightEvents;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AeroFlow.Local.OpsDashboard.Live;

/// <summary>
/// Reads one topic's <c>ops-dashboard</c> subscription and projects each message onto the <see cref="AirportViewStore"/>.
/// Same shape as the services' processors: manual completion, one at a time, unreadable bodies dead-lettered.
/// </summary>
public sealed partial class TopicListener<TMessage>(
    ServiceBusClient client,
    AirportViewStore store,
    string topic,
    string subscription,
    Func<AirportView, TMessage, AirportView> project,
    ILogger<TopicListener<TMessage>> logger) : IHostedService, IAsyncDisposable
    where TMessage : class
{
    private readonly ILogger _logger = logger;
    private ServiceBusProcessor? _processor;

    public string Topic => topic;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = client.CreateProcessor(topic, subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        });
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);
        LogStarted(topic, subscription);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }

    /// <summary>Parse a body; <c>null</c> if it can't be read. Public for tests.</summary>
    public static TMessage? TryRead(BinaryData body)
    {
        try
        {
            return body.ToObjectFromJson<TMessage>(LiveJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var message = TryRead(args.Message.Body);
        if (message is null)
        {
            LogUnreadable(topic, args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "unreadable", $"not a {typeof(TMessage).Name}", args.CancellationToken);
            return;
        }

        store.Update(view => project(view, message));
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(args.Exception, topic, args.ErrorSource.ToString());
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dashboard listening on {Topic}/{Subscription}")]
    private partial void LogStarted(string topic, string subscription);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dead-lettered unreadable message {MessageId} on {Topic}")]
    private partial void LogUnreadable(string topic, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Service Bus processor error on {Topic} ({Source})")]
    private partial void LogProcessorError(Exception ex, string topic, string source);
}

public static class LiveEventsExtensions
{
    /// <summary>
    /// Registers the store, an expiry sweep, and, only when a <c>messaging</c> connection exists, one listener per
    /// topic. Without Service Bus the dashboard runs with empty panels.
    /// </summary>
    public static IHostApplicationBuilder AddLiveAirport(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<LiveEventsOptions>(builder.Configuration.GetSection(LiveEventsOptions.SectionName));
        builder.Services.AddSingleton<AirportViewStore>();
        builder.Services.AddHostedService<ExpirySweep>();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(LiveEventsOptions.ConnectionName)))
        {
            return builder;
        }

        FlightEventTracing.Enable();
        builder.AddAzureServiceBusClient(LiveEventsOptions.ConnectionName);
        Add<FlightEvent>(builder, o => o.FlightTopic, AirportProjector.Apply);
        Add<BaggageEventMessage>(builder, o => o.BaggageTopic, AirportProjector.Apply);
        Add<GateEventMessage>(builder, o => o.GateTopic, AirportProjector.Apply);
        return builder;
    }

    private static void Add<T>(IHostApplicationBuilder builder, Func<LiveEventsOptions, string> topic, Func<AirportView, T, AirportView> project)
        where T : class =>
        builder.Services.AddSingleton<IHostedService>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<LiveEventsOptions>>().Value;
            return new TopicListener<T>(
                sp.GetRequiredService<ServiceBusClient>(),
                sp.GetRequiredService<AirportViewStore>(),
                topic(o),
                o.Subscription,
                project,
                sp.GetRequiredService<ILogger<TopicListener<T>>>());
        });
}

/// <summary>Every 15 s, drops finished flights and cleared belts so the panels don't grow forever.</summary>
public sealed class ExpirySweep(AirportViewStore store, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            store.Update(view => AirportProjector.Expire(view, clock.GetUtcNow()));
        }
    }
}
