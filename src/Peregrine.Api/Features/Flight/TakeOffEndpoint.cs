using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class TakeOffEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/takeoff", (TakeOffRequest? request, DroneContext drone) =>
        {
            var (success, error) = drone.TakeOff(request?.Altitude);
            return success
                ? Results.Ok(new { message = "Taking off.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("TakeOff")
        .WithSummary("Take off to specified altitude (Idle → TakingOff → Hovering)")
        .WithTags("Flight");
    }
}
