using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Mission;

public sealed class GetMissionPlanEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapGet("/drone/mission", (DroneContext drone) =>
        {
            var plan = drone.GetMissionPlan();
            if (plan is null)
                return Results.NotFound(new { error = "No mission plan is currently loaded." });

            return Results.Ok(new
            {
                plan = QGcPlanParser.ToStatus(plan),
                status = drone.GetStatus()
            });
        })
        .WithName("GetMissionPlan")
        .WithSummary("Get the currently loaded mission plan summary")
        .WithTags("Mission");
    }
}
