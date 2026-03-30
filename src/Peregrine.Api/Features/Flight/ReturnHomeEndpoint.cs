using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class ReturnHomeEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/return-home", (DroneContext drone) =>
        {
            var (success, error) = drone.ReturnHome();
            return success
                ? Results.Ok(new { message = "Returning to home position. Will auto-land on arrival.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("ReturnHome")
        .WithSummary("Navigate to home position and auto-land (Hovering/Flying → Flying → Landing → Idle)")
        .WithTags("Flight");
    }
}
