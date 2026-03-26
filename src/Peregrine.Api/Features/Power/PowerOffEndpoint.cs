using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Power;

public sealed class PowerOffEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/power/off", (DroneContext drone) =>
        {
            var (success, error) = drone.PowerOff();
            return success
                ? Results.Ok(new { message = "Drone powered off.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("PowerOff")
        .WithSummary("Power off the drone (Idle/Charging → Offline)")
        .WithTags("Power");
    }
}
