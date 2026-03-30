using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Peregrine.Api.Infrastructure;

/// <summary>
/// Background service that drives the drone simulation on each tick.
/// Handles flight physics (Haversine navigation), altitude transitions,
/// battery drain/charge, and emergency procedures.
/// </summary>
public class FlightSimulatorService : BackgroundService
{
    private readonly DroneContext _drone;
    private readonly TelemetryBroadcaster _broadcaster;
    private readonly DroneConfiguration _config;
    private readonly ILogger<FlightSimulatorService> _logger;

    public FlightSimulatorService(
        DroneContext drone,
        TelemetryBroadcaster broadcaster,
        IOptions<DroneConfiguration> options,
        ILogger<FlightSimulatorService> logger)
    {
        _drone = drone;
        _broadcaster = broadcaster;
        _config = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tickInterval = TimeSpan.FromMilliseconds(_config.Simulation.TickIntervalMs);
        var telemetryInterval = TimeSpan.FromMilliseconds(_config.Simulation.TelemetryIntervalMs);
        var timer = new PeriodicTimer(tickInterval);

        var msSinceLastTelemetry = 0.0;

        _logger.LogInformation(
            "Flight simulator started. Tick: {TickMs}ms, Telemetry: {TelemetryMs}ms",
            _config.Simulation.TickIntervalMs,
            _config.Simulation.TelemetryIntervalMs);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var dt = _config.Simulation.TickIntervalMs / 1000.0;

            Tick(dt);

            msSinceLastTelemetry += _config.Simulation.TickIntervalMs;
            if (msSinceLastTelemetry >= _config.Simulation.TelemetryIntervalMs)
            {
                _broadcaster.Publish(_drone.GetStatus());
                msSinceLastTelemetry = 0;
            }
        }
    }

    internal void Tick(double dt)
    {
        var state = _drone.State;

        switch (state)
        {
            case DroneState.TakingOff:
                TickTakingOff(dt);
                break;

            case DroneState.Flying:
                TickFlying(dt);
                break;

            case DroneState.Landing:
                TickLanding(dt);
                break;

            case DroneState.Charging:
                TickCharging(dt);
                break;

            case DroneState.Hovering:
                DrainBattery(DroneState.Hovering, dt);
                break;

            case DroneState.Idle:
                DrainBattery(DroneState.Idle, dt);
                break;
        }
    }

    private void TickTakingOff(double dt)
    {
        DrainBattery(DroneState.TakingOff, dt);

        var current = _drone.Position;
        var targetAlt = _drone.TargetAltitude();
        var climbRate = _config.Performance.TakeoffSpeedMps;
        var newAlt = Math.Min(current.Altitude + climbRate * dt, targetAlt);

        _drone.UpdatePosition(current.Latitude, current.Longitude, newAlt,
            headingDegrees: 0, speedMps: climbRate);

        if (Math.Abs(newAlt - targetAlt) < 0.01)
        {
            _drone.TransitionToHovering();
            _logger.LogInformation("Drone reached altitude {Alt:F1}m, now hovering.", newAlt);
        }
    }

    private void TickFlying(double dt)
    {
        DrainBattery(DroneState.Flying, dt);

        var waypoint = _drone.PeekNextWaypoint();
        if (waypoint is null)
        {
            _drone.TransitionToHovering();
            return;
        }

        var current = _drone.Position;
        var speedMps = waypoint.SpeedMps ?? _drone.DesiredSpeedMps ?? _config.Performance.MaxSpeedMps;

        var distanceMeters = GeoMath.HaversineDistance(
            current.Latitude, current.Longitude,
            waypoint.Latitude, waypoint.Longitude);

        var bearing = GeoMath.Bearing(
            current.Latitude, current.Longitude,
            waypoint.Latitude, waypoint.Longitude);

        var horizontalMove = speedMps * dt;

        if (horizontalMove >= distanceMeters)
        {
            // Snap to waypoint and dequeue
            _drone.DequeueWaypoint();
            _drone.UpdatePosition(
                waypoint.Latitude, waypoint.Longitude, waypoint.Altitude,
                headingDegrees: bearing, speedMps: 0);

            _logger.LogInformation(
                "Waypoint reached ({Lat:F6}, {Lon:F6}). {Remaining} remaining.",
                waypoint.Latitude, waypoint.Longitude, _drone.WaypointQueueDepth());

            if (_drone.WaypointQueueDepth() == 0)
            {
                _drone.TransitionToHovering();
                if (_drone.IsReturningHome)
                {
                    _drone.Land();
                    _logger.LogInformation("Returned to home position. Auto-landing.");
                }
                else
                {
                    _logger.LogInformation("All waypoints completed. Hovering.");
                }
            }
        }
        else
        {
            // Move toward waypoint
            var (newLat, newLon) = GeoMath.MoveToward(
                current.Latitude, current.Longitude,
                bearing, horizontalMove);

            // Interpolate altitude toward waypoint altitude
            var altDiff = waypoint.Altitude - current.Altitude;
            var altMove = _config.Performance.TakeoffSpeedMps * dt;
            var newAlt = altDiff >= 0
                ? Math.Min(current.Altitude + altMove, waypoint.Altitude)
                : Math.Max(current.Altitude - altMove, waypoint.Altitude);

            _drone.UpdatePosition(newLat, newLon, newAlt, bearing, speedMps);
        }
    }

    private void TickLanding(double dt)
    {
        DrainBattery(DroneState.Landing, dt);

        var current = _drone.Position;
        var descentRate = _config.Performance.TakeoffSpeedMps;
        var newAlt = Math.Max(current.Altitude - descentRate * dt, 0.0);

        _drone.UpdatePosition(current.Latitude, current.Longitude, newAlt,
            headingDegrees: 0, speedMps: descentRate);

        if (newAlt <= 0.01)
        {
            _drone.TransitionToIdle();
            _logger.LogInformation("Drone landed. State: Idle.");
        }
    }

    private void TickCharging(double dt)
    {
        var full = _drone.ChargeBattery(_config.Battery.DrainRates.ChargeRatePerSecond * dt);
        if (full)
        {
            _drone.TransitionChargingToIdle();
            _logger.LogInformation("Battery fully charged. State: Idle.");
        }
    }

    private void DrainBattery(DroneState state, double dt)
    {
        var rates = _config.Battery.DrainRates;
        var drainRate = state switch
        {
            DroneState.Idle => rates.IdlePerSecond,
            DroneState.Hovering => rates.HoveringPerSecond,
            DroneState.Flying => rates.FlyingPerSecond,
            DroneState.TakingOff or DroneState.Landing => rates.TakeoffLandingPerSecond,
            _ => 0.0
        };

        if (drainRate <= 0) return;

        var alive = _drone.DrainBattery(drainRate * dt);
        var battery = _drone.BatteryPercent;

        // Emergency landing if below threshold while airborne
        if (battery <= _config.Battery.EmergencyLandThresholdPercent
            && state is DroneState.TakingOff or DroneState.Hovering or DroneState.Flying)
        {
            _logger.LogWarning(
                "Battery critical ({Battery:F1}%). Initiating emergency landing.", battery);
            _drone.ForceLand();
        }
        else if (!alive)
        {
            _logger.LogWarning("Battery depleted. Forcing landing.");
            _drone.ForceLand();
        }
    }

}
