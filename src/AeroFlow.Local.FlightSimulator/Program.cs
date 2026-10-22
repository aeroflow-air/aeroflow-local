using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.FlightSimulator;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

FlightEventTracing.Enable();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(FlightSimulatorWorker.ActivitySource.Name));

// Aspire client integration: connection string comes from the AppHost's WithReference(messaging).
builder.AddAzureServiceBusClient(FlightEventTopics.ConnectionName);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SimulatorOptions>(builder.Configuration.GetSection(SimulatorOptions.SectionName));
// One generator shared by the worker (ticks and publishes) and the scenario endpoints (demo controls).
builder.Services.AddSingleton(sp => new FlightGenerator(
    sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.Seed,
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<FlightSimulatorWorker>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapScenarios();
app.Run();

// Expose for WebApplicationFactory in tests.
public partial class Program;
