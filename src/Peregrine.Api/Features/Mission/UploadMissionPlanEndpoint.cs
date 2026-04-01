using System.Text.Json;
using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Features.Mission;

public sealed class UploadMissionPlanEndpoint : IEndpoint
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapPost("/drone/mission", async (HttpRequest req, DroneContext drone, MissionPlanStore store) =>
        {
            string rawJson;
            using (var reader = new StreamReader(req.Body))
                rawJson = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawJson))
                return Results.BadRequest(new { error = "Request body is required." });

            QGroundControlPlan plan;
            try
            {
                plan = JsonSerializer.Deserialize<QGroundControlPlan>(rawJson, QGcPlanParser.JsonOptions)
                    ?? throw new JsonException("Null result after deserialization.");
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }

            if (!string.Equals(plan.FileType, "Plan", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"Invalid fileType '{plan.FileType}'. Expected 'Plan'." });

            if (plan.Version != 1)
                return Results.BadRequest(new { error = $"Unsupported plan version {plan.Version}. Expected version 1." });

            MissionPlan missionPlan;
            try
            {
                missionPlan = QGcPlanParser.Parse(plan);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Invalid plan structure: {ex.Message}" });
            }

            var (success, error) = drone.LoadMissionPlan(missionPlan);
            if (!success)
                return Results.Conflict(new { error });

            store.Save(rawJson);

            return Results.Ok(new
            {
                message = $"Mission plan uploaded. {drone.WaypointQueueDepth()} waypoint(s) loaded (including any RTL home waypoint).",
                plan = QGcPlanParser.ToStatus(missionPlan),
                status = drone.GetStatus()
            });
        })
        .WithName("UploadMissionPlan")
        .WithSummary("Upload a QGroundControl .plan mission file (JSON body)")
        .WithTags("Mission")
        .Accepts<object>("application/json");
    }
}
