using AeroFlow.Local.OpsDashboard.Components;
using AeroFlow.Local.OpsDashboard.Health;
using AeroFlow.Local.OpsDashboard.Live;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Blazor Server (interactive server rendering), ADR-0016. Live updates ride Blazor's own SignalR circuit.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Health strip: polls each service's /health through Aspire service discovery (http://<resource-name>).
builder.Services.Configure<HealthStripOptions>(builder.Configuration.GetSection(HealthStripOptions.SectionName));
builder.Services.AddHttpClient(ServiceHealthMonitor.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ServiceHealthMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServiceHealthMonitor>());

// Live airport (#73): ops-dashboard subscriptions on flight-events, baggage-events and gate-events feed an
// in-memory AirportView. Listeners only start when the "messaging" connection exists.
builder.AddLiveAirport();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "ops-dashboard",
    status = "ok",
    message = "AeroFlow live airport ops dashboard (live, #73)"
}));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Expose for WebApplicationFactory in tests.
public partial class Program;
