namespace AeroFlow.Local.FlightSimulator;

public sealed record DelayRequest(string FlightNumber, int Minutes, string? Reason = null);

/// <summary>
/// Demo control (local only): lets the ops dashboard trigger scenarios. Scenarios only ever change the simulated
/// flights, so everything downstream still arrives as normal <c>flight-events</c> lifecycle events.
/// </summary>
public static class ScenarioEndpoints
{
    public static IEndpointRouteBuilder MapScenarios(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/scenarios");
        group.MapGet("/flights", (FlightGenerator generator) => Results.Ok(generator.Delayable()));
        group.MapPost("/delay", (DelayRequest request, FlightGenerator generator) =>
            generator.Delay(request.FlightNumber ?? "", request.Minutes, request.Reason) switch
            {
                ScenarioResult.Accepted a => Results.Accepted(value: a.Event),
                ScenarioResult.NotFound n => Results.NotFound(new { error = n.Error }),
                ScenarioResult.Invalid i => Results.BadRequest(new { error = i.Error }),
                _ => Results.StatusCode(500),
            });
        return app;
    }
}
