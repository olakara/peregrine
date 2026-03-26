using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Power;

public sealed class PowerOnEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/power/on", (DroneContext drone) =>
        {
            var (success, error) = drone.PowerOn();
            return success
                ? Results.Ok(new { message = "Drone powered on.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("PowerOn")
        .WithSummary("Power on the drone (Offline → Idle)")
        .WithTags("Power");
    }
}
