using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Features.Mission;
using Peregrine.Api.Tests.Fixtures;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Integration;

public sealed class MissionEndpointTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly DroneAppFactory _factory;

    public MissionEndpointTests()
    {
        _factory = new DroneAppFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static StringContent PlanJson(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private const string SimplePlan = """
        {
          "fileType": "Plan",
          "version": 1,
          "groundStation": "QGroundControl",
          "mission": {
            "items": [
              { "type": "SimpleItem", "command": 22, "params": [0,0,0,null,0,0,30.0], "Altitude": 30.0 },
              { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.414516,54.456488,30.0] },
              { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.420000,54.460000,30.0] }
            ],
            "plannedHomePosition": [24.414516, 54.456488, 0.0],
            "version": 2
          },
          "geoFence": { "circles": [], "polygons": [], "version": 2 },
          "rallyPoints": { "points": [], "version": 2 }
        }
        """;

    private const string PlanWithLanding = """
        {
          "fileType": "Plan",
          "version": 1,
          "mission": {
            "items": [
              { "type": "SimpleItem", "command": 22, "params": [0,0,0,null,0,0,30.0], "Altitude": 30.0 },
              { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.414516,54.456488,30.0] },
              { "type": "SimpleItem", "command": 21, "params": [0,0,0,null,0,0,0] }
            ],
            "plannedHomePosition": [24.414516, 54.456488, 0.0],
            "version": 2
          },
          "geoFence": { "circles": [], "polygons": [], "version": 2 },
          "rallyPoints": { "points": [], "version": 2 }
        }
        """;

    private const string PlanWithRtl = """
        {
          "fileType": "Plan",
          "version": 1,
          "mission": {
            "items": [
              { "type": "SimpleItem", "command": 22, "params": [0,0,0,null,0,0,30.0], "Altitude": 30.0 },
              { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.414516,54.456488,30.0] },
              { "type": "SimpleItem", "command": 20, "params": [0,0,0,null,0,0,0] }
            ],
            "plannedHomePosition": [24.414516, 54.456488, 0.0],
            "version": 2
          },
          "geoFence": { "circles": [], "polygons": [], "version": 2 },
          "rallyPoints": { "points": [], "version": 2 }
        }
        """;

    // -----------------------------------------------------------------------
    // POST /drone/mission
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadMission_ValidPlan_Returns200WithWaypointCount()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        var response = await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("waypoint");
    }

    [Fact]
    public async Task UploadMission_InvalidJson_Returns400()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.PostAsync("/drone/mission", PlanJson("not-json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadMission_WrongFileType_Returns400()
    {
        _factory.ResetDroneAndAssertCleanState();

        var response = await _client.PostAsync("/drone/mission",
            PlanJson("""{"fileType":"Fence","version":1,"mission":{"items":[],"version":2},"geoFence":{"circles":[],"polygons":[],"version":2},"rallyPoints":{"points":[],"version":2}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("fileType");
    }

    [Fact]
    public async Task UploadMission_WrongVersion_Returns400()
    {
        _factory.ResetDroneAndAssertCleanState();

        var badPlan = SimplePlan.Replace("\"version\": 1,", "\"version\": 99,");
        var response = await _client.PostAsync("/drone/mission", PlanJson(badPlan));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().Contain("version");
    }

    [Fact]
    public async Task UploadMission_WhileFlying_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1, 1, 10)]);
        drone.Navigate();

        var response = await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UploadMission_LoadsWaypointsIntoQueue()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        _factory.GetDroneContext().WaypointQueueDepth().Should().Be(2);
    }

    [Fact]
    public async Task UploadMission_ReplacesManuallyLoadedWaypoints()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.LoadWaypoints([new Waypoint(1, 1, 10), new Waypoint(2, 2, 10), new Waypoint(3, 3, 10)]);

        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        // SimplePlan has 2 nav waypoints
        drone.WaypointQueueDepth().Should().Be(2);
    }

    [Fact]
    public async Task UploadMission_PersistsToDisk()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();

        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        _factory.GetMissionPlanStore().Load().Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // GET /drone/mission
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetMission_NoPlanLoaded_Returns404()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetMissionPlanStore().Clear();

        var response = await _client.GetAsync("/drone/mission");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMission_AfterUpload_Returns200WithPlanSummary()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        var response = await _client.GetAsync("/drone/mission");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("waypointCount");
        body.Should().Contain("hasTakeoff");
    }

    // -----------------------------------------------------------------------
    // DELETE /drone/mission
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteMission_ClearsPlan_Returns200()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        var response = await _client.DeleteAsync("/drone/mission");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        drone.WaypointQueueDepth().Should().Be(0);
        drone.GetMissionPlan().Should().BeNull();
    }

    [Fact]
    public async Task DeleteMission_WhileFlying_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1, 1, 10)]);
        drone.Navigate();

        var response = await _client.DeleteAsync("/drone/mission");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteMission_ClearsPersistedFile()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.GetDroneContext().PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        await _client.DeleteAsync("/drone/mission");

        _factory.GetMissionPlanStore().Load().Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Navigate with mission plan (auto-takeoff from Idle)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Navigate_FromIdle_WithTakeoffPlan_Returns200AndTakingOffState()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        drone.State.Should().Be(DroneState.TakingOff);
    }

    [Fact]
    public async Task Navigate_FromIdle_WithoutTakeoffInPlan_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();

        // Load a plan with no Takeoff command
        const string planNoTakeoff = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": {
                "items": [
                  { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.4,54.4,30.0] }
                ],
                "version": 2
              },
              "geoFence": { "circles": [], "polygons": [], "version": 2 },
              "rallyPoints": { "points": [], "version": 2 }
            }
            """;
        await _client.PostAsync("/drone/mission", PlanJson(planNoTakeoff));

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Navigate_FromHovering_WithPlan_WorksNormally()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        // Manually get to Hovering (skip auto-takeoff path)
        drone.TakeOff();
        drone.TransitionToHovering();

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        drone.State.Should().Be(DroneState.Flying);
    }

    // -----------------------------------------------------------------------
    // Simulator: auto-navigate after takeoff
    // -----------------------------------------------------------------------

    [Fact]
    public void TryAutoNavigate_AfterTakeoffWithPlan_TransitionsToFlying()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        // Load a plan with takeoff, then start navigate (goes to TakingOff)
        var plan = new MissionPlan(
            Waypoints: [new Waypoint(24.4, 54.4, 30)],
            HasTakeoff: true,
            TakeoffAltitude: 30,
            HasLanding: false,
            HasRtl: false,
            PlannedHomePosition: null,
            GeoFenceCircles: [],
            GeoFencePolygons: [],
            RallyPoints: []);

        drone.LoadMissionPlan(plan);
        var (ok, _) = drone.Navigate();
        ok.Should().BeTrue();
        drone.State.Should().Be(DroneState.TakingOff);

        // Simulate reaching hover altitude → TransitionToHovering
        drone.TransitionToHovering();
        drone.State.Should().Be(DroneState.Hovering);

        // Auto-navigate should fire
        var navigated = drone.TryAutoNavigate();
        navigated.Should().BeTrue();
        drone.State.Should().Be(DroneState.Flying);
    }

    [Fact]
    public void TryAutoNavigate_WithoutMissionPlan_ReturnsFalse()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();

        var navigated = drone.TryAutoNavigate();

        navigated.Should().BeFalse();
        drone.State.Should().Be(DroneState.Hovering);
    }

    // -----------------------------------------------------------------------
    // MissionHasAutoLand
    // -----------------------------------------------------------------------

    [Fact]
    public void MissionHasAutoLand_PlanWithLandCommand_ReturnsTrue()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var plan = new MissionPlan(
            Waypoints: [new Waypoint(1, 1, 10)],
            HasTakeoff: false,
            TakeoffAltitude: null,
            HasLanding: true,
            HasRtl: false,
            PlannedHomePosition: null,
            GeoFenceCircles: [],
            GeoFencePolygons: [],
            RallyPoints: []);

        drone.LoadMissionPlan(plan);

        drone.MissionHasAutoLand.Should().BeTrue();
    }

    [Fact]
    public void MissionHasAutoLand_NoPlan_ReturnsFalse()
    {
        var drone = DroneContextFactory.Create();
        drone.MissionHasAutoLand.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // RTL waypoint enqueuing
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadMissionPlan_WithRtl_EnqueuesHomeWaypointAndSetsReturningHome()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var plan = new MissionPlan(
            Waypoints: [new Waypoint(24.4, 54.4, 30)],
            HasTakeoff: false,
            TakeoffAltitude: null,
            HasLanding: false,
            HasRtl: true,
            PlannedHomePosition: new GpsCoordinate(0, 0, 0),
            GeoFenceCircles: [],
            GeoFencePolygons: [],
            RallyPoints: []);

        drone.LoadMissionPlan(plan);

        // Queue should have original waypoint + home waypoint appended
        drone.WaypointQueueDepth().Should().Be(2);
        drone.IsReturningHome.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Manual waypoints clear the mission plan
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadWaypoints_ClearsMissionPlan()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var plan = new MissionPlan(
            Waypoints: [new Waypoint(1, 1, 10)],
            HasTakeoff: true,
            TakeoffAltitude: 30,
            HasLanding: false,
            HasRtl: false,
            PlannedHomePosition: null,
            GeoFenceCircles: [],
            GeoFencePolygons: [],
            RallyPoints: []);

        drone.LoadMissionPlan(plan);
        drone.GetMissionPlan().Should().NotBeNull();

        drone.LoadWaypoints([new Waypoint(2, 2, 20)]);

        drone.GetMissionPlan().Should().BeNull();
        drone.WaypointQueueDepth().Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Reviewer-gap tests: missing edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Navigate_FromIdle_WithNoplanAtAll_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        // No plan uploaded — raw Idle

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        drone.State.Should().Be(DroneState.Idle);
    }

    [Fact]
    public async Task Navigate_FromIdle_PlanWithTakeoffAndLandButNoWaypoints_Returns409()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();

        // Takeoff + Land only — no NAV_WAYPOINT items
        const string takeoffLandOnlyPlan = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": {
                "items": [
                  { "type": "SimpleItem", "command": 22, "params": [0,0,0,null,0,0,30.0], "Altitude": 30.0 },
                  { "type": "SimpleItem", "command": 21, "params": [0,0,0,null,0,0,0] }
                ],
                "version": 2
              },
              "geoFence": { "circles": [], "polygons": [], "version": 2 },
              "rallyPoints": { "points": [], "version": 2 }
            }
            """;
        await _client.PostAsync("/drone/mission", PlanJson(takeoffLandOnlyPlan));

        var response = await _client.PostAsync("/drone/navigate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        drone.State.Should().Be(DroneState.Idle);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("waypoints");
    }

    [Fact]
    public async Task UploadMission_WhileTakingOff_Returns409AndDroneStateUnchanged()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.State.Should().Be(DroneState.TakingOff);

        var response = await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        drone.State.Should().Be(DroneState.TakingOff);
    }

    [Fact]
    public async Task DeleteMission_WhileTakingOff_Returns409AndPlanPreserved()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));
        drone.Navigate(); // Idle + Takeoff plan → TakingOff
        drone.State.Should().Be(DroneState.TakingOff);

        var response = await _client.DeleteAsync("/drone/mission");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        // Plan and queue must be intact — drone is mid-takeoff
        drone.GetMissionPlan().Should().NotBeNull();
        drone.WaypointQueueDepth().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UploadMission_WhileFlying_Returns409AndDroneStatePreserved()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1, 1, 10)]);
        drone.Navigate();
        drone.State.Should().Be(DroneState.Flying);

        var response = await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        drone.State.Should().Be(DroneState.Flying);
        drone.WaypointQueueDepth().Should().Be(1);
    }

    [Fact]
    public async Task UploadMission_WhileHovering_Returns409AndDroneStateUnchanged()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.State.Should().Be(DroneState.Hovering);

        var response = await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        drone.State.Should().Be(DroneState.Hovering);
    }

    [Fact]
    public async Task DeleteMission_WhileHovering_Returns409AndPlanPreserved()
    {
        _factory.ResetDroneAndAssertCleanState();
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        await _client.PostAsync("/drone/mission", PlanJson(SimplePlan));
        drone.TakeOff();
        drone.TransitionToHovering();
        drone.State.Should().Be(DroneState.Hovering);

        var response = await _client.DeleteAsync("/drone/mission");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        drone.GetMissionPlan().Should().NotBeNull();
    }
}
