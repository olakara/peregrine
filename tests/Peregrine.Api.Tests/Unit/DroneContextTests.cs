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
