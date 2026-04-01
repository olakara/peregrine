using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Mission;

public sealed class DeleteMissionPlanEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapDelete("/drone/mission", (DroneContext drone, MissionPlanStore store) =>
        {
            var (success, error) = drone.ClearMissionPlan();
            if (!success)
                return Results.Conflict(new { error });

            var persisted = store.Clear();

            return Results.Ok(new
            {
                message = "Mission plan cleared.",
                status = drone.GetStatus(),
                persistenceWarning = persisted ? null : "Mission cleared from memory but the plan file could not be deleted from disk."
            });
        })
        .WithName("DeleteMissionPlan")
        .WithSummary("Clear the currently loaded mission plan")
        .WithTags("Mission");
    }
}
