using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class HoverEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/hover", (DroneContext drone) =>
        {
            var (success, error) = drone.Hover();
            return success
                ? Results.Ok(new { message = "Hovering in place.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("Hover")
        .WithSummary("Pause navigation and hover (Flying → Hovering)")
        .WithTags("Flight");
    }
}
