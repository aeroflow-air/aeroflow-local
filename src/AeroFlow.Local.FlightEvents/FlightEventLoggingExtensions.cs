using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AeroFlow.Local.FlightEvents;

public static class FlightEventLoggingExtensions
{
    /// <summary>
    /// Registers the Service Bus client (Aspire integration, tracing on) and a background
    /// processor that logs every flight event received on the subscription named by
    /// <c>FlightEvents:Subscription</c> (set by the AppHost). No-op if it is not set.
    /// </summary>
    public static IHostApplicationBuilder AddFlightEventLogging(this IHostApplicationBuilder builder, string serviceName)
    {
        FlightEventTracing.Enable();
        var subscription = builder.Configuration["FlightEvents:Subscription"];
        if (string.IsNullOrWhiteSpace(subscription))
        {
            return builder;
        }

        builder.AddAzureServiceBusClient(FlightEventTopics.ConnectionName);
        builder.Services.AddHostedService(sp => new FlightEventLogger(
            sp.GetRequiredService<ServiceBusClient>(),
            subscription,
            serviceName,
            sp.GetRequiredService<ILogger<FlightEventLogger>>()));
        return builder;
    }
}

public static class FlightEventTracing
{
    /// <summary>Azure SDK activity sources are experimental; turn them on so Service Bus spans reach the dashboard.</summary>
    public static void Enable() => AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
}

internal sealed partial class FlightEventLogger(
    ServiceBusClient client,
    string subscription,
    string serviceName,
    ILogger<FlightEventLogger> logger) : BackgroundService
{
    // Field needed by the .NET 8 [LoggerMessage] generator (it doesn't read primary-ctor params).
    private readonly ILogger _logger = logger;
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(FlightEventTopics.FlightEvents, subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = true,
            MaxConcurrentCalls = 1,
        });
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
        await _processor.StartProcessingAsync(stoppingToken);
        LogListening(serviceName, FlightEventTopics.FlightEvents, subscription);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var evt = args.Message.Body.ToObjectFromJson<FlightEvent>(FlightEventJson.Options);
        if (evt is null)
        {
            LogUnreadable(serviceName, args.Message.MessageId);
            return Task.CompletedTask;
        }

        var detail = evt.DelayMinutes is { } delay ? $" +{delay}m ({evt.Reason})" : evt.Reason is { } reason ? $" ({reason})" : string.Empty;
        LogReceived(serviceName, evt.EventType, evt.FlightNumber, evt.Origin, evt.Destination, detail, evt.FlightId);
        return Task.CompletedTask;
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        LogError(args.Exception, serviceName, args.ErrorSource.ToString());
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Service} listening on {Topic}/{Subscription}")]
    private partial void LogListening(string service, string topic, string subscription);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Service} received {EventType} for {FlightNumber} {Origin}->{Destination}{Detail} [{FlightId}]")]
    private partial void LogReceived(string service, FlightEventType eventType, string flightNumber, string origin, string destination, string detail, string flightId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Service} could not read message {MessageId}")]
    private partial void LogUnreadable(string service, string messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Service} Service Bus processor error ({Source})")]
    private partial void LogError(Exception ex, string service, string source);
}
