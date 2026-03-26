using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Flight;

public sealed class LandEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/land", (DroneContext drone) =>
        {
            var (success, error) = drone.Land();
            return success
                ? Results.Ok(new { message = "Landing initiated.", status = drone.GetStatus() })
                : Results.Conflict(new { error });
        })
        .WithName("Land")
        .WithSummary("Initiate landing (Hovering/Flying → Landing → Idle)")
        .WithTags("Flight");
    }
}
