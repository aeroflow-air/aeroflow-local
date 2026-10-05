var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "ops-dashboard",
    status = "ok",
    message = "AeroFlow ops dashboard API placeholder"
}));

app.MapGet("/", () => Results.Ok(new
{
    service = "ops-dashboard",
    description = "Placeholder ops dashboard API — replace with real UI/API when #53 lands."
}));

app.Run();
