var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "cleaning",
    status = "ok",
    message = "Cleaning placeholder — swap for the real svc-* repo when it exists."
}));

app.Run();
