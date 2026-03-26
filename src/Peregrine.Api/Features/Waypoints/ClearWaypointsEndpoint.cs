using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Waypoints;

public sealed class ClearWaypointsEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapDelete("/drone/waypoints", (DroneContext drone) =>
        {
            var (success, error) = drone.ClearWaypoints();
            return success
                ? Results.Ok(new { message = "Waypoint queue cleared.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("ClearWaypoints")
        .WithSummary("Clear the waypoint queue")
        .WithTags("Waypoints");
    }
}
