var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "baggage-reclaim",
    status = "ok",
    message = "Baggage reclaim placeholder — swap for the real svc-* repo when it exists."
}));

app.Run();
