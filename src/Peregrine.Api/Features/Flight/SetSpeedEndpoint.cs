using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class SetSpeedEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPut("/drone/speed", (SetSpeedRequest request, DroneContext drone) =>
        {
            var (success, error) = drone.SetSpeed(request.SpeedMps);
            return success
                ? Results.Ok(new { message = $"Speed set to {request.SpeedMps:F1} m/s.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("SetSpeed")
        .WithSummary("Set the drone's cruise speed in m/s (any powered-on state; resets on power-off)")
        .WithTags("Flight");
    }
}
