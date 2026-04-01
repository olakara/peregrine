using FluentAssertions;
using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Unit;

public sealed class DroneContextTests
{
    // --- PowerOn ---

    [Fact]
    public void PowerOn_FromOffline_TransitionsToIdle()
    {
        var drone = DroneContextFactory.Create();
        var (success, error) = drone.PowerOn();
        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Idle);
    }

    [Theory]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Charging)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.Flying)]
    [InlineData(DroneState.Landing)]
    public void PowerOn_FromNonOffline_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.PowerOn();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- PowerOff ---

    [Theory]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Charging)]
    public void PowerOff_FromIdleOrCharging_TransitionsToOffline(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.PowerOff();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Offline);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.Flying)]
    [InlineData(DroneState.Landing)]
    public void PowerOff_FromAirborneOrOffline_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.PowerOff();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- TakeOff ---

    [Fact]
    public void TakeOff_FromIdle_TransitionsToTakingOff()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var (success, error) = drone.TakeOff();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.TakingOff);
    }

    [Fact]
    public void TakeOff_WithCustomAltitude_SetsTargetAltitude()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        drone.TakeOff(50.0);

        drone.TargetAltitude().Should().Be(50.0);
    }

    [Fact]
    public void TakeOff_WithAltitudeAboveMax_ClampsToMax()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Performance.MaxAltitudeMeters = 100.0);
        drone.PowerOn();

        drone.TakeOff(200.0);

        drone.TargetAltitude().Should().Be(100.0);
    }

    [Fact]
    public void TakeOff_WithLowBattery_ReturnsError()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.EmergencyLandThresholdPercent = 20.0);
        drone.PowerOn();
        // Drain battery below threshold
        drone.DrainBattery(85.0); // leaves 15%, below 20% threshold

        var (success, error) = drone.TakeOff();

        success.Should().BeFalse();
        error.Should().Contain("Battery too low");
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.Flying)]
    [InlineData(DroneState.Charging)]
    public void TakeOff_FromNonIdle_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.TakeOff();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- Land ---

    [Theory]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.Flying)]
    public void Land_FromHoveringOrFlying_TransitionsToLanding(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.Land();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Landing);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Charging)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Landing)]
    public void Land_FromInvalidState_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.Land();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- Hover ---

    [Fact]
    public void Hover_FromFlying_TransitionsToHovering()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);

        var (success, error) = drone.Hover();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Hovering);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Charging)]
    public void Hover_FromNonFlying_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.Hover();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- Navigate ---

    [Fact]
    public void Navigate_FromHoveringWithWaypoints_TransitionsToFlying()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        drone.LoadWaypoints([new Domain.Waypoint(1.0, 1.0, 10.0)]);
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.Navigate();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Flying);
    }

    [Fact]
    public void Navigate_WithEmptyQueue_ReturnsError()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.Navigate();

        success.Should().BeFalse();
        error.Should().Contain("empty");
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Flying)]
    [InlineData(DroneState.TakingOff)]
    public void Navigate_FromNonHovering_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);
        if (startState != DroneState.Offline)
            drone.LoadWaypoints([new Domain.Waypoint(1.0, 1.0, 10.0)]);

        var (success, error) = drone.Navigate();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- StartCharging ---

    [Fact]
    public void StartCharging_FromIdleWithPartialBattery_TransitionsToCharging()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        drone.DrainBattery(50.0);

        var (success, error) = drone.StartCharging();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Charging);
    }

    [Fact]
    public void StartCharging_WhenBatteryFull_ReturnsError()
    {
        var drone = DroneContextFactory.Create(); // starts at 100%
        drone.PowerOn();

        var (success, error) = drone.StartCharging();

        success.Should().BeFalse();
        error.Should().Contain("fully charged");
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Charging)]
    [InlineData(DroneState.TakingOff)]
    public void StartCharging_FromNonIdle_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 50.0);
        ForceState(drone, startState);

        var (success, error) = drone.StartCharging();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- StopCharging ---

    [Fact]
    public void StopCharging_FromCharging_TransitionsToIdle()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 50.0);
        drone.PowerOn();
        drone.StartCharging();

        var (success, error) = drone.StopCharging();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Idle);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Hovering)]
    public void StopCharging_FromNonCharging_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.StopCharging();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- LoadWaypoints ---

    [Fact]
    public void LoadWaypoints_WhenPoweredOn_LoadsQueue()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        var waypoints = new[] { new Domain.Waypoint(1, 1, 10), new Domain.Waypoint(2, 2, 20) };

        var (success, error) = drone.LoadWaypoints(waypoints);

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.WaypointQueueDepth().Should().Be(2);
    }

    [Fact]
    public void LoadWaypoints_ReplacesExistingQueue()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        drone.LoadWaypoints([new Domain.Waypoint(1, 1, 10), new Domain.Waypoint(2, 2, 20)]);

        drone.LoadWaypoints([new Domain.Waypoint(5, 5, 50)]);

        drone.WaypointQueueDepth().Should().Be(1);
    }

    [Fact]
    public void LoadWaypoints_WhenOffline_ReturnsError()
    {
        var drone = DroneContextFactory.Create();

        var (success, error) = drone.LoadWaypoints([new Domain.Waypoint(1, 1, 10)]);

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LoadWaypoints_WithAltitudeExceedingMax_ReturnsError()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Performance.MaxAltitudeMeters = 100.0);
        drone.PowerOn();

        var (success, error) = drone.LoadWaypoints([new Domain.Waypoint(1, 1, 101.0)]);

        success.Should().BeFalse();
        error.Should().Contain("101.0m").And.Contain("100.0m");
        drone.WaypointQueueDepth().Should().Be(0);
    }

    [Fact]
    public void LoadWaypoints_WithNegativeAltitude_ReturnsError()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var (success, error) = drone.LoadWaypoints([new Domain.Waypoint(1, 1, -5.0)]);

        success.Should().BeFalse();
        error.Should().Contain("-5.0m");
        drone.WaypointQueueDepth().Should().Be(0);
    }

    [Fact]
    public void LoadWaypoints_InvalidAltitude_DoesNotModifyExistingQueue()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Performance.MaxAltitudeMeters = 100.0);
        drone.PowerOn();
        drone.LoadWaypoints([new Domain.Waypoint(1, 1, 50)]);

        var (success, _) = drone.LoadWaypoints([new Domain.Waypoint(2, 2, 200.0)]);

        success.Should().BeFalse();
        drone.WaypointQueueDepth().Should().Be(1); // original queue untouched
    }

    // --- ClearWaypoints ---

    [Theory]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.TakingOff)]
    public void ClearWaypoints_WhenNotFlying_ClearsQueue(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        drone.LoadWaypoints([new Domain.Waypoint(1, 1, 10)]);
        ForceState(drone, startState);

        var (success, error) = drone.ClearWaypoints();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.WaypointQueueDepth().Should().Be(0);
    }

    [Fact]
    public void ClearWaypoints_WhileFlying_ReturnsError()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);

        var (success, error) = drone.ClearWaypoints();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // --- Battery ---

    [Fact]
    public void DrainBattery_ReducesBatteryByAmount()
    {
        var drone = DroneContextFactory.Create();
        drone.DrainBattery(30.0);
        drone.BatteryPercent.Should().BeApproximately(70.0, 0.001);
    }

    [Fact]
    public void DrainBattery_ClampsToZero()
    {
        var drone = DroneContextFactory.Create();
        var alive = drone.DrainBattery(200.0);
        drone.BatteryPercent.Should().Be(0.0);
        alive.Should().BeFalse();
    }

    [Fact]
    public void DrainBattery_ReturnsTrueWhenBatteryRemains()
    {
        var drone = DroneContextFactory.Create();
        var alive = drone.DrainBattery(50.0);
        alive.Should().BeTrue();
    }

    [Fact]
    public void ChargeBattery_IncreasesBatteryByAmount()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 50.0);
        drone.ChargeBattery(20.0);
        drone.BatteryPercent.Should().BeApproximately(70.0, 0.001);
    }

    [Fact]
    public void ChargeBattery_ClampsToHundred()
    {
        var drone = DroneContextFactory.Create();
        drone.ChargeBattery(50.0);
        drone.BatteryPercent.Should().Be(100.0);
    }

    [Fact]
    public void ChargeBattery_ReturnsTrueWhenFull()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 99.0);
        var full = drone.ChargeBattery(5.0);
        full.Should().BeTrue();
    }

    [Fact]
    public void ChargeBattery_ReturnsFalseWhenNotFull()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 50.0);
        var full = drone.ChargeBattery(20.0);
        full.Should().BeFalse();
    }

    // --- ForceLand ---

    [Theory]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.Flying)]
    public void ForceLand_FromAirborne_TransitionsToLanding(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        drone.ForceLand();

        drone.State.Should().Be(DroneState.Landing);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Charging)]
    [InlineData(DroneState.Landing)]
    public void ForceLand_FromNonAirborne_NoOp(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        drone.ForceLand();

        drone.State.Should().Be(startState);
    }

    // --- Simulator state transitions ---

    [Theory]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Flying)]
    public void TransitionToHovering_FromTakingOffOrFlying_Succeeds(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var result = drone.TransitionToHovering();

        result.Should().BeTrue();
        drone.State.Should().Be(DroneState.Hovering);
    }

    [Fact]
    public void TransitionToHovering_FromOtherState_ReturnsFalse()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Idle);

        var result = drone.TransitionToHovering();

        result.Should().BeFalse();
    }

    [Fact]
    public void TransitionToIdle_FromLanding_Succeeds()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Landing);

        var result = drone.TransitionToIdle();

        result.Should().BeTrue();
        drone.State.Should().Be(DroneState.Idle);
    }

    [Fact]
    public void TransitionToIdle_FromOtherState_ReturnsFalse()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        var result = drone.TransitionToIdle();

        result.Should().BeFalse();
    }

    [Fact]
    public void TransitionChargingToIdle_FromCharging_Succeeds()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 50.0);
        drone.PowerOn();
        drone.StartCharging();

        var result = drone.TransitionChargingToIdle();

        result.Should().BeTrue();
        drone.State.Should().Be(DroneState.Idle);
    }

    // --- GetStatus ---

    [Fact]
    public void GetStatus_ReflectsCurrentState()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var status = drone.GetStatus();

        status.DroneId.Should().Be("test-drone");
        status.DroneName.Should().Be("Test Drone");
        status.State.Should().Be(DroneState.Idle);
        status.BatteryPercent.Should().Be(100.0);
        status.IsCharging.Should().BeFalse();
    }

    [Fact]
    public void GetStatus_IsCharging_WhenCharging()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Battery.InitialChargePercent = 50.0);
        drone.PowerOn();
        drone.StartCharging();

        var status = drone.GetStatus();

        status.IsCharging.Should().BeTrue();
        status.State.Should().Be(DroneState.Charging);
    }

    // --- Waypoint helpers ---

    [Fact]
    public void PeekNextWaypoint_ReturnsNullWhenQueueEmpty()
    {
        var drone = DroneContextFactory.Create();
        drone.PeekNextWaypoint().Should().BeNull();
    }

    [Fact]
    public void PeekNextWaypoint_ReturnsFirstWaypointWithoutRemoving()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        var wp = new Domain.Waypoint(1.0, 2.0, 10.0);
        drone.LoadWaypoints([wp]);

        var peeked = drone.PeekNextWaypoint();

        peeked.Should().Be(wp);
        drone.WaypointQueueDepth().Should().Be(1);
    }

    [Fact]
    public void DequeueWaypoint_RemovesAndReturnsFirst()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        var wp1 = new Domain.Waypoint(1.0, 2.0, 10.0);
        var wp2 = new Domain.Waypoint(3.0, 4.0, 20.0);
        drone.LoadWaypoints([wp1, wp2]);

        var dequeued = drone.DequeueWaypoint();

        dequeued.Should().Be(wp1);
        drone.WaypointQueueDepth().Should().Be(1);
    }

    // --- SetSpeed ---

    [Fact]
    public void SetSpeed_FromIdle_SetsDesiredSpeed()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var (success, error) = drone.SetSpeed(10.0);

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.DesiredSpeedMps.Should().Be(10.0);
    }

    [Fact]
    public void SetSpeed_FromHovering_SetsDesiredSpeed()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.SetSpeed(8.0);

        success.Should().BeTrue();
        drone.DesiredSpeedMps.Should().Be(8.0);
    }

    [Fact]
    public void SetSpeed_FromOffline_ReturnsError()
    {
        var drone = DroneContextFactory.Create();

        var (success, error) = drone.SetSpeed(5.0);

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
        drone.DesiredSpeedMps.Should().BeNull();
    }

    [Fact]
    public void SetSpeed_AboveMaxSpeed_ReturnsError()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var (success, error) = drone.SetSpeed(999.0);

        success.Should().BeFalse();
        error.Should().Contain("exceeds maximum");
        drone.DesiredSpeedMps.Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void SetSpeed_ZeroOrNegative_ReturnsError(double speed)
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var (success, error) = drone.SetSpeed(speed);

        success.Should().BeFalse();
        error.Should().Contain("greater than 0");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SetSpeed_NonFiniteValue_ReturnsError(double speed)
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();

        var (success, error) = drone.SetSpeed(speed);

        success.Should().BeFalse();
        error.Should().Contain("finite");
        drone.DesiredSpeedMps.Should().BeNull();
    }

    [Fact]
    public void SetSpeed_ResetsToNullOnPowerOff()
    {
        var drone = DroneContextFactory.Create();
        drone.PowerOn();
        drone.SetSpeed(10.0);

        drone.PowerOff();

        drone.DesiredSpeedMps.Should().BeNull();
    }

    // --- ReturnHome ---

    [Fact]
    public void ReturnHome_FromHovering_TransitionsToFlying()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.ReturnHome();

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.State.Should().Be(DroneState.Flying);
    }

    [Fact]
    public void ReturnHome_FromFlying_RemainsFlying()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);

        var (success, error) = drone.ReturnHome();

        success.Should().BeTrue();
        drone.State.Should().Be(DroneState.Flying);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Landing)]
    [InlineData(DroneState.Charging)]
    public void ReturnHome_FromNonAirborneState_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.ReturnHome();

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ReturnHome_ClearsExistingWaypointsAndLoadsHomeAsOnlyWaypoint()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);
        drone.LoadWaypoints([new Domain.Waypoint(50, 50, 100), new Domain.Waypoint(60, 60, 100)]);
        // re-enter Hovering state (LoadWaypoints doesn't change state)

        drone.ReturnHome();

        drone.WaypointQueueDepth().Should().Be(1);
        var homeWp = drone.PeekNextWaypoint();
        var nonNullHomeWp = homeWp!;
        nonNullHomeWp.Latitude.Should().Be(0.0);   // DroneContextFactory home: lat=0, lon=0
        nonNullHomeWp.Longitude.Should().Be(0.0);
        // Altitude should match the drone's current altitude, not home.Altitude=0
        // (drone flies level to home, then auto-lands on arrival)
        nonNullHomeWp.Altitude.Should().Be(drone.Position.Altitude);
    }

    [Fact]
    public void ReturnHome_SetsIsReturningHomeFlag()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        drone.ReturnHome();

        drone.IsReturningHome.Should().BeTrue();
    }

    [Fact]
    public void Hover_AfterReturnHome_ClearsReturningHomeFlag()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);
        drone.ReturnHome(); // state stays Flying, flag set

        drone.Hover();

        drone.IsReturningHome.Should().BeFalse();
        drone.State.Should().Be(DroneState.Hovering);
    }

    [Fact]
    public void Land_AfterReturnHome_ClearsReturningHomeFlag()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);
        drone.ReturnHome(); // transitions to Flying

        drone.Land();

        drone.IsReturningHome.Should().BeFalse();
        drone.State.Should().Be(DroneState.Landing);
    }

    [Fact]
    public void LoadWaypoints_AfterReturnHome_ClearsReturningHomeFlag()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);
        drone.ReturnHome(); // flag set

        // LoadWaypoints is allowed from any non-Offline state (including Flying); this test verifies that
        // calling LoadWaypoints alone after ReturnHome is enough to clear the IsReturningHome flag.
        drone.LoadWaypoints([new Domain.Waypoint(1, 1, 10)]);

        drone.IsReturningHome.Should().BeFalse();
    }

    [Fact]
    public void ForceLand_AfterReturnHome_ClearsReturningHomeFlag()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);
        drone.ReturnHome();

        drone.ForceLand();

        drone.IsReturningHome.Should().BeFalse();
        drone.State.Should().Be(DroneState.Landing);
    }

    // --- AdjustAltitude ---

    [Theory]
    [InlineData(DroneState.Hovering)]
    [InlineData(DroneState.Flying)]
    public void AdjustAltitude_FromHoveringOrFlying_SetsTargetAltitude(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.AdjustAltitude(60.0);

        success.Should().BeTrue();
        error.Should().BeNull();
        drone.TargetAltitude().Should().Be(60.0);
    }

    [Theory]
    [InlineData(DroneState.Offline)]
    [InlineData(DroneState.Idle)]
    [InlineData(DroneState.Charging)]
    [InlineData(DroneState.TakingOff)]
    [InlineData(DroneState.Landing)]
    public void AdjustAltitude_FromInvalidState_ReturnsError(DroneState startState)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, startState);

        var (success, error) = drone.AdjustAltitude(60.0);

        success.Should().BeFalse();
        error.Should().Contain("Hovering or Flying");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void AdjustAltitude_ZeroOrNegative_ReturnsError(double altitude)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.AdjustAltitude(altitude);

        success.Should().BeFalse();
        error.Should().Contain("greater than 0");
    }

    [Fact]
    public void AdjustAltitude_AboveMaxAltitude_ReturnsError()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Performance.MaxAltitudeMeters = 100.0);
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.AdjustAltitude(200.0);

        success.Should().BeFalse();
        error.Should().Contain("maximum");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AdjustAltitude_NonFiniteValue_ReturnsError(double altitude)
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.AdjustAltitude(altitude);

        success.Should().BeFalse();
        error.Should().Contain("finite");
    }

    [Fact]
    public void AdjustAltitude_AtMaxAltitude_Succeeds()
    {
        var drone = DroneContextFactory.Create(cfg => cfg.Performance.MaxAltitudeMeters = 120.0);
        ForceState(drone, DroneState.Hovering);

        var (success, error) = drone.AdjustAltitude(120.0);

        success.Should().BeTrue();
        drone.TargetAltitude().Should().Be(120.0);
    }

    [Fact]
    public void AdjustAltitude_WhileFlying_SetsActiveLegOverride()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);

        drone.AdjustAltitude(80.0);

        drone.ActiveLegAltitudeOverride().Should().Be(80.0);
    }

    [Fact]
    public void AdjustAltitude_WhileHovering_DoesNotSetActiveLegOverride()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Hovering);

        drone.AdjustAltitude(80.0);

        drone.ActiveLegAltitudeOverride().Should().BeNull();
    }

    [Fact]
    public void DequeueWaypoint_ClearsActiveLegAltitudeOverride()
    {
        var drone = DroneContextFactory.Create();
        ForceState(drone, DroneState.Flying);
        drone.AdjustAltitude(80.0);

        drone.DequeueWaypoint(); // dequeues the waypoint loaded by ForceState

        drone.ActiveLegAltitudeOverride().Should().BeNull();
    }

    // --- Helpers ---

    /// <summary>
    /// Forces the drone into the given state by driving it through the required transitions.
    /// Uses the command API where possible; falls back to simulator-API methods for mid-flight states.
    /// </summary>
    private static void ForceState(DroneContext drone, DroneState target)
    {
        switch (target)
        {
            case DroneState.Offline:
                break;
            case DroneState.Idle:
                drone.PowerOn();
                break;
            case DroneState.Charging:
                drone.PowerOn();
                drone.DrainBattery(50.0);
                drone.StartCharging();
                break;
            case DroneState.TakingOff:
                drone.PowerOn();
                drone.TakeOff();
                break;
            case DroneState.Hovering:
                drone.PowerOn();
                drone.TakeOff();
                drone.TransitionToHovering();
                break;
            case DroneState.Flying:
                drone.PowerOn();
                drone.TakeOff();
                drone.TransitionToHovering();
                drone.LoadWaypoints([new Domain.Waypoint(10, 10, 30)]);
                drone.Navigate();
                break;
            case DroneState.Landing:
                drone.PowerOn();
                drone.TakeOff();
                drone.TransitionToHovering();
                drone.Land();
                break;
        }
    }
}
