using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.FlightSimulator;
using OpenTelemetry.Trace;

FlightEventTracing.Enable();

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(FlightSimulatorWorker.ActivitySource.Name));

// Aspire client integration: connection string comes from the AppHost's WithReference(messaging).
builder.AddAzureServiceBusClient(FlightEventTopics.ConnectionName);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection(SimulatorOptions.SectionName));
builder.Services.AddHostedService<FlightSimulatorWorker>();

builder.Build().Run();
