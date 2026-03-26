using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class NavigateEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/navigate", (DroneContext drone) =>
        {
            var (success, error) = drone.Navigate();
            return success
                ? Results.Ok(new { message = "Navigation started.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("Navigate")
        .WithSummary("Start navigating loaded waypoints (Hovering → Flying)")
        .WithTags("Flight");
    }
}
