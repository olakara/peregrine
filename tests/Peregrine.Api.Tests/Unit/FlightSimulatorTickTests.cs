using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure;
using Peregrine.Api.Infrastructure.Configuration;
using Peregrine.Api.Tests.Helpers;

namespace Peregrine.Api.Tests.Unit;

/// <summary>
/// Tests for FlightSimulatorService tick logic.
/// We construct the simulator directly and invoke Tick* via a thin subclass
/// that exposes the internal methods for testing.
/// </summary>
public sealed class FlightSimulatorTickTests
{
    private static (TestableFlight sim, DroneContext drone) CreateSim(
        Action<DroneConfiguration>? configure = null)
    {
        var drone = DroneContextFactory.Create(configure);
        var broadcaster = new TelemetryBroadcaster();
        var config = BuildConfig(configure);
        var sim = new TestableFlight(drone, broadcaster, Options.Create(config),
            NullLogger<FlightSimulatorService>.Instance);
        return (sim, drone);
    }

    private static DroneConfiguration BuildConfig(Action<DroneConfiguration>? configure = null)
    {
        var config = new DroneConfiguration
        {
            Id = "test",
            Name = "Test",
            HomePosition = new HomePositionConfig { Latitude = 0, Longitude = 0, Altitude = 0 },
            Performance = new PerformanceConfig
            {
                MaxSpeedMps = 15.0,
                MaxAltitudeMeters = 120.0,
                TakeoffSpeedMps = 3.0,
                DefaultHoverAltitudeMeters = 30.0
            },
            Battery = new BatteryConfig
            {
                InitialChargePercent = 100.0,
                EmergencyLandThresholdPercent = 10.0,
                DrainRates = new DrainRatesConfig
                {
                    IdlePerSecond = 0.02,
                    HoveringPerSecond = 0.15,
                    FlyingPerSecond = 0.25,
                    TakeoffLandingPerSecond = 0.20,
                    ChargeRatePerSecond = 0.5
                }
            },
            Simulation = new SimulationConfig { TickIntervalMs = 500, TelemetryIntervalMs = 1000 }
        };
        configure?.Invoke(config);
        return config;
    }

    // --- TakingOff ---

    [Fact]
    public void TickTakingOff_ClimbsTowardTargetAltitude()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(30.0);
        // dt = 0.5s, climbRate = 3 m/s → expect +1.5m per tick
        sim.PublicTick(0.5);

        drone.Position.Altitude.Should().BeApproximately(1.5, 0.001);
        drone.State.Should().Be(DroneState.TakingOff);
    }

    [Fact]
    public void TickTakingOff_TransitionsToHoveringWhenTargetReached()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(1.0); // very low target
        // Each tick: climbRate(3) × dt(0.5) = 1.5m → overshoots target → should hover
        sim.PublicTick(0.5);

        drone.State.Should().Be(DroneState.Hovering);
        drone.Position.Altitude.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void TickTakingOff_DrainsBattery()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(30.0);
        var before = drone.BatteryPercent;

        sim.PublicTick(0.5);

        drone.BatteryPercent.Should().BeLessThan(before);
    }

    // --- Landing ---

    [Fact]
    public void TickLanding_DescendsTowardZero()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(30.0);
        drone.TransitionToHovering();
        drone.UpdatePosition(0.0, 0.0, 10.0, 0, 0); // set altitude to 10m
        drone.Land();

        sim.PublicTick(0.5); // descend by 1.5m

        drone.Position.Altitude.Should().BeApproximately(8.5, 0.001);
        drone.State.Should().Be(DroneState.Landing);
    }

    [Fact]
    public void TickLanding_TransitionsToIdleWhenGrounded()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(30.0);
        drone.TransitionToHovering();
        drone.UpdatePosition(0.0, 0.0, 0.005, 0, 0); // nearly at ground
        drone.Land();

        sim.PublicTick(0.5);

        drone.State.Should().Be(DroneState.Idle);
        drone.Position.Altitude.Should().BeApproximately(0.0, 0.001);
    }

    // --- Flying ---

    [Fact]
    public void TickFlying_MovesPositionTowardWaypoint()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();
        // Waypoint ~1km north
        drone.LoadWaypoints([new Waypoint(0.009, 0.0, 10.0)]);
        drone.Navigate();

        var startLat = drone.Position.Latitude;
        sim.PublicTick(0.5); // 15 m/s × 0.5s = 7.5m toward waypoint

        drone.Position.Latitude.Should().BeGreaterThan(startLat);
        drone.State.Should().Be(DroneState.Flying);
    }

    [Fact]
    public void TickFlying_ReachesWaypoint_DequeuesAndHoversIfQueueEmpty()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();
        // Very close waypoint (< 7.5m at max speed)
        drone.LoadWaypoints([new Waypoint(0.00001, 0.0, 10.0)]);
        drone.Navigate();

        sim.PublicTick(0.5);

        drone.WaypointQueueDepth().Should().Be(0);
        drone.State.Should().Be(DroneState.Hovering);
    }

    [Fact]
    public void TickFlying_ReachesWaypoint_MovesToNextWaypointWhenQueueNotEmpty()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();
        drone.LoadWaypoints([
            new Waypoint(0.00001, 0.0, 10.0),   // very close — will be reached
            new Waypoint(1.0, 0.0, 10.0)          // far away
        ]);
        drone.Navigate();

        sim.PublicTick(0.5);

        drone.WaypointQueueDepth().Should().Be(1);
        drone.State.Should().Be(DroneState.Flying);
    }

    [Fact]
    public void TickFlying_WithNoWaypoints_TransitionsToHovering()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();
        drone.LoadWaypoints([new Waypoint(1.0, 0.0, 10.0)]);
        drone.Navigate();
        drone.DequeueWaypoint(); // drain the queue manually

        sim.PublicTick(0.5);

        drone.State.Should().Be(DroneState.Hovering);
    }

    [Fact]
    public void TickFlying_InterpolatesAltitudeTowardWaypoint()
    {
        var (sim, drone) = CreateSim();
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();
        drone.UpdatePosition(0.0, 0.0, 10.0, 0, 0);
        // Waypoint at altitude 50m — should climb toward it
        drone.LoadWaypoints([new Waypoint(0.5, 0.0, 50.0)]);
        drone.Navigate();
        var startAlt = drone.Position.Altitude;

        sim.PublicTick(0.5);

        drone.Position.Altitude.Should().BeGreaterThan(startAlt);
    }

    // --- Charging ---

    [Fact]
    public void TickCharging_IncreasesBattery()
    {
        var (sim, drone) = CreateSim(cfg => cfg.Battery.InitialChargePercent = 50.0);
        drone.PowerOn();
        drone.StartCharging();
        var before = drone.BatteryPercent;

        sim.PublicTick(0.5); // chargeRate(0.5%/s) × 0.5s = 0.25%

        drone.BatteryPercent.Should().BeGreaterThan(before);
    }

    [Fact]
    public void TickCharging_TransitionsToIdleWhenFull()
    {
        var (sim, drone) = CreateSim(cfg => cfg.Battery.InitialChargePercent = 99.9);
        drone.PowerOn();
        drone.StartCharging();

        sim.PublicTick(0.5);

        drone.State.Should().Be(DroneState.Idle);
        drone.BatteryPercent.Should().Be(100.0);
    }

    // --- Emergency battery drain ---

    [Fact]
    public void DrainBattery_EmergencyLand_TriggeredWhenBatteryBelowThresholdWhileAirborne()
    {
        var (sim, drone) = CreateSim(cfg =>
        {
            cfg.Battery.InitialChargePercent = 11.0;       // just above threshold
            cfg.Battery.EmergencyLandThresholdPercent = 10.0;
            cfg.Battery.DrainRates.HoveringPerSecond = 2.0; // large drain to cross threshold in one tick
        });
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();

        sim.PublicTick(0.5); // drains 2.0 × 0.5 = 1.0% → 10.0% = hits threshold

        drone.State.Should().Be(DroneState.Landing);
    }

    [Fact]
    public void DrainBattery_ForcesLandingWhenDepleted()
    {
        var (sim, drone) = CreateSim(cfg =>
        {
            cfg.Battery.InitialChargePercent = 0.05;
            cfg.Battery.EmergencyLandThresholdPercent = 0.0;
            cfg.Battery.DrainRates.HoveringPerSecond = 0.2; // will deplete in one tick
        });
        drone.PowerOn();
        drone.TakeOff(10.0);
        drone.TransitionToHovering();

        sim.PublicTick(0.5);

        drone.State.Should().Be(DroneState.Landing);
        drone.BatteryPercent.Should().Be(0.0);
    }

    // --- Testable subclass ---

    private sealed class TestableFlight(
        DroneContext drone,
        TelemetryBroadcaster broadcaster,
        IOptions<DroneConfiguration> options,
        Microsoft.Extensions.Logging.ILogger<FlightSimulatorService> logger)
        : FlightSimulatorService(drone, broadcaster, options, logger)
    {
        public void PublicTick(double dt) => Tick(dt);
    }
}
