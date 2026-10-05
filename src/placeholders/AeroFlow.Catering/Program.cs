var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/ping", () => Results.Ok(new
{
    service = "catering",
    status = "ok",
    message = "Catering placeholder — swap for the real svc-* repo when it exists."
}));

app.Run();
