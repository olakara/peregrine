using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Telemetry;

public sealed class GetStatusEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapGet("/drone/status", (DroneContext drone) =>
            Results.Ok(drone.GetStatus()))
        .WithName("GetStatus")
        .WithSummary("Get the current drone status snapshot")
        .WithTags("Telemetry");
    }
}
