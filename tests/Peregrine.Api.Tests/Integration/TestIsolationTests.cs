using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Tests.Fixtures;

namespace Peregrine.Api.Tests.Integration;

public sealed class TestIsolationTests : IDisposable
{
    private readonly DroneAppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void ResetDroneAndAssertCleanState_FromOffline_KeepsCleanBaseline()
    {
        _factory.ResetDroneAndAssertCleanState();
        _factory.AssertCleanState();
    }

    [Fact]
    public void ResetDroneAndAssertCleanState_FromCharging_RestoresOfflineAndFullBattery()
    {
        var drone = _factory.GetDroneContext();
        drone.PowerOn();
        drone.DrainBattery(45.0);
        drone.StartCharging();

        _factory.ResetDroneAndAssertCleanState();
    }

    [Fact]
    public void ResetDroneAndAssertCleanState_ClearsMissionPlanAndWaypoints()
    {
        var drone = _factory.GetDroneContext();
        drone.PowerOn();

        var plan = new MissionPlan(
            Waypoints:
            [
                new Waypoint(24.4145, 54.4564, 30.0),
                new Waypoint(24.42, 54.46, 40.0)
            ],
            HasTakeoff: true,
            TakeoffAltitude: 30.0,
            HasLanding: false,
            HasRtl: true,
            PlannedHomePosition: new GpsCoordinate(24.4145, 54.4564, 0.0),
            GeoFenceCircles: [],
            GeoFencePolygons: [],
            RallyPoints: []);

        var loadResult = drone.LoadMissionPlan(plan);
        loadResult.Success.Should().BeTrue(loadResult.Error);
        drone.WaypointQueueDepth().Should().BeGreaterThan(0);
        _factory.GetMissionPlanStore().Save("{\"fileType\":\"Plan\",\"version\":1}");
        _factory.GetMissionPlanStore().Load().Should().NotBeNull();

        _factory.ResetDroneAndAssertCleanState();
    }
}
