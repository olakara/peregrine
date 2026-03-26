using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Battery;

public sealed class StartRechargeEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/battery/recharge", (DroneContext drone) =>
        {
            var (success, error) = drone.StartCharging();
            return success
                ? Results.Ok(new { message = "Charging started.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("StartRecharge")
        .WithSummary("Start battery recharge (Idle → Charging). Auto-stops when full.")
        .WithTags("Battery");
    }
}
