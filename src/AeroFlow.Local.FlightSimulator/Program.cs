using AeroFlow.Local.FlightEvents;
using AeroFlow.Local.FlightSimulator;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
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

// OpenAPI document at /openapi/v1.json, gated by ApiDocs:Enabled like the svc-* services (ADR-0015). It is the
// source of openapi/flight-simulator.json. This local tool serves the spec only, with no Scalar UI.
var apiDocsEnabled = builder.Configuration.GetValue("ApiDocs:Enabled", false);
if (apiDocsEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AeroFlow Flight Simulator API",
        Version = "v1",
        Description = "Local demo controls for the flight simulator: list flights that can be delayed and ask for a delay. Changes only the simulated flights; everything downstream arrives as normal flight-events.",
    }));
}

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapScenarios();

if (apiDocsEnabled)
{
    app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
}

app.Run();

// Expose for WebApplicationFactory in tests.
public partial class Program;
