using AeroFlow.Local.FlightEvents;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Logs flight-events received on this placeholder's subscription (set by the AppHost).
builder.AddFlightEventLogging("cleaning");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "cleaning",
    status = "ok",
    message = "Cleaning placeholder — swap for the real svc-* repo when it exists."
}));

app.Run();
