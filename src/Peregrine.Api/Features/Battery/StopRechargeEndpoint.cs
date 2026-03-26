using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Battery;

public sealed class StopRechargeEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/battery/recharge/stop", (DroneContext drone) =>
        {
            var (success, error) = drone.StopCharging();
            return success
                ? Results.Ok(new { message = "Charging stopped.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("StopRecharge")
        .WithSummary("Stop battery recharge (Charging → Idle)")
        .WithTags("Battery");
    }
}
