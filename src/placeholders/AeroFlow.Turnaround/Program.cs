var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "turnaround",
    status = "ok",
    message = "Turnaround placeholder — swap for the real svc-* repo when it exists."
}));

app.Run();
