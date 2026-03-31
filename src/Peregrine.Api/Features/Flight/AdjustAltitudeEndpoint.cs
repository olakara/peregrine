using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class AdjustAltitudeEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPut("/drone/altitude", (AdjustAltitudeRequest request, DroneContext drone) =>
        {
            var (success, error) = drone.AdjustAltitude(request.AltitudeMeters);
            return success
                ? Results.Ok(new { message = $"Adjusting altitude to {request.AltitudeMeters:F1} m.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("AdjustAltitude")
        .WithSummary("Adjust the drone's target altitude while Hovering or Flying")
        .WithTags("Flight");
    }
}
