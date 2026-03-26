using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Battery;

public sealed class GetBatteryEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapGet("/drone/battery", (DroneContext drone) =>
        {
            var status = drone.GetStatus();
            return Results.Ok(new BatteryStatus(
                status.BatteryPercent,
                status.IsCharging,
                status.State.ToString(),
                status.Timestamp
            ));
        })
        .WithName("GetBattery")
        .WithSummary("Get current battery level and charging status")
        .WithTags("Battery");
    }
}
