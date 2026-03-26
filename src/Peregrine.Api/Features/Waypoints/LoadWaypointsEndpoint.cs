using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Waypoints;

public sealed class LoadWaypointsEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/waypoints", (List<WaypointRequest> requests, DroneContext drone) =>
        {
            if (requests is null || requests.Count == 0)
                return Results.BadRequest(new { error = "Waypoint list cannot be empty." });

            var waypoints = requests.Select(r =>
                new Waypoint(r.Latitude, r.Longitude, r.Altitude, r.SpeedMps));

            var (success, error) = drone.LoadWaypoints(waypoints);
            return success
                ? Results.Ok(new { message = $"{requests.Count} waypoint(s) loaded.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("LoadWaypoints")
        .WithSummary("Load an ordered list of GPS waypoints for navigation")
        .WithTags("Waypoints");
    }
}
