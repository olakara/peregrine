using Peregrine.Api.Domain;
using Peregrine.Api.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Peregrine.Api.Infrastructure;

/// <summary>
/// Thread-safe mutable drone state. Owns all state transitions and enforces the state machine.
/// </summary>
public sealed class DroneContext
{
    private readonly Lock _lock = new();
    private readonly DroneConfiguration _config;

    private DroneState _state = DroneState.Offline;
    private GpsCoordinate _position;
    private double _headingDegrees;
    private double _speedMps;
    private double _batteryPercent;
    private double _targetAltitude;
    private double? _desiredSpeedMps;
    private double? _activeLegAltitudeOverride;
    private bool _returningHome;
    private readonly Queue<Waypoint> _waypointQueue = new();
    private MissionPlan? _missionPlan;
    private bool _autoNavigateAfterHover;

    public DroneContext(IOptions<DroneConfiguration> options)
    {
        _config = options.Value;
        var home = _config.HomePosition;
        _position = new GpsCoordinate(home.Latitude, home.Longitude, home.Altitude);
        _batteryPercent = _config.Battery.InitialChargePercent;
    }

    // --- Command API (called by endpoints) ---

    public (bool Success, string? Error) PowerOn()
    {
        lock (_lock)
        {
            if (_state != DroneState.Offline)
                return (false, $"Cannot power on from state {_state}. Drone must be Offline.");
            _state = DroneState.Idle;
            return (true, null);
        }
    }

    public (bool Success, string? Error) PowerOff()
    {
        lock (_lock)
        {
            if (_state is not (DroneState.Idle or DroneState.Charging))
                return (false, $"Cannot power off from state {_state}. Drone must be on the ground (Idle or Charging).");
            _state = DroneState.Offline;
            _speedMps = 0;
            _desiredSpeedMps = null;
            _returningHome = false;
            return (true, null);
        }
    }

    public (bool Success, string? Error) TakeOff(double? requestedAltitude = null)
    {
        lock (_lock)
        {
            if (_state != DroneState.Idle)
                return (false, $"Cannot take off from state {_state}. Drone must be Idle.");
            if (_batteryPercent <= _config.Battery.EmergencyLandThresholdPercent)
                return (false, $"Battery too low ({_batteryPercent:F1}%). Recharge before flight.");

            _targetAltitude = requestedAltitude
                ?? _config.Performance.DefaultHoverAltitudeMeters;

            if (_targetAltitude > _config.Performance.MaxAltitudeMeters)
                _targetAltitude = _config.Performance.MaxAltitudeMeters;

            _autoNavigateAfterHover = false;
            _state = DroneState.TakingOff;
            return (true, null);
        }
    }

    public (bool Success, string? Error) Land()
    {
        lock (_lock)
        {
            if (_state is not (DroneState.Hovering or DroneState.Flying))
                return (false, $"Cannot land from state {_state}. Drone must be Hovering or Flying.");
            _state = DroneState.Landing;
            _speedMps = 0;
            _returningHome = false;
            _autoNavigateAfterHover = false;
            return (true, null);
        }
    }

    public (bool Success, string? Error) Hover()
    {
        lock (_lock)
        {
            if (_state != DroneState.Flying)
                return (false, $"Cannot hover from state {_state}. Drone must be Flying.");
            _state = DroneState.Hovering;
            _speedMps = 0;
            _returningHome = false;
            return (true, null);
        }
    }

    public (bool Success, string? Error) Navigate()
    {
        lock (_lock)
        {
            // Existing path: Hovering → Flying
            if (_state == DroneState.Hovering)
            {
                if (_waypointQueue.Count == 0)
                    return (false, "Waypoint queue is empty. Load waypoints before navigating.");
                _state = DroneState.Flying;
                return (true, null);
            }

            // New path: Idle + mission plan with Takeoff → TakingOff (auto-navigates once hovering)
            if (_state == DroneState.Idle && _missionPlan?.HasTakeoff == true)
            {
                if (_batteryPercent <= _config.Battery.EmergencyLandThresholdPercent)
                    return (false, $"Battery too low ({_batteryPercent:F1}%). Recharge before flight.");
                if (_waypointQueue.Count == 0)
                    return (false, "Mission plan has no navigation waypoints to execute.");

                var alt = _missionPlan.TakeoffAltitude ?? _config.Performance.DefaultHoverAltitudeMeters;
                _targetAltitude = Math.Min(alt, _config.Performance.MaxAltitudeMeters);
                _state = DroneState.TakingOff;
                _autoNavigateAfterHover = true;
                return (true, null);
            }

            return (false, $"Cannot navigate from state {_state}. Drone must be Hovering (or Idle with a mission plan that includes a Takeoff command).");
        }
    }

    public (bool Success, string? Error) StartCharging()
    {
        lock (_lock)
        {
            if (_state != DroneState.Idle)
                return (false, $"Cannot charge from state {_state}. Drone must be Idle (on the ground, powered on).");
            if (_batteryPercent >= 100.0)
                return (false, "Battery is already fully charged.");
            _state = DroneState.Charging;
            return (true, null);
        }
    }

    public (bool Success, string? Error) StopCharging()
    {
        lock (_lock)
        {
            if (_state != DroneState.Charging)
                return (false, $"Cannot stop charging from state {_state}.");
            _state = DroneState.Idle;
            return (true, null);
        }
    }

    public (bool Success, string? Error) LoadWaypoints(IEnumerable<Waypoint> waypoints)
    {
        lock (_lock)
        {
            if (_state is DroneState.Offline)
                return (false, "Drone is offline. Power on first.");

            // Manual waypoints replace any uploaded mission plan
            _missionPlan = null;
            _autoNavigateAfterHover = false;

            _waypointQueue.Clear();
            foreach (var wp in waypoints)
                _waypointQueue.Enqueue(wp);

            _returningHome = false;
            return (true, null);
        }
    }

    public (bool Success, string? Error) ClearWaypoints()
    {
        lock (_lock)
        {
            if (_state == DroneState.Flying)
                return (false, "Cannot clear waypoints while Flying. Hover first.");
            _waypointQueue.Clear();
            return (true, null);
        }
    }

    public (bool Success, string? Error) LoadMissionPlan(MissionPlan plan)
    {
        lock (_lock)
        {
            if (_state is DroneState.TakingOff or DroneState.Flying or DroneState.Landing)
                return (false, "Cannot upload mission plan while airborne. Land first.");

            _missionPlan = plan;
            _autoNavigateAfterHover = false;

            // Populate the waypoint queue from the plan's navigation waypoints
            _waypointQueue.Clear();
            foreach (var wp in plan.Waypoints)
                _waypointQueue.Enqueue(wp);

            // If plan includes RTL, append the home position as the final waypoint so
            // the drone automatically returns home after completing all other waypoints.
            if (plan.HasRtl)
            {
                var homePos = plan.PlannedHomePosition
                    ?? new GpsCoordinate(_config.HomePosition.Latitude, _config.HomePosition.Longitude, _config.HomePosition.Altitude);
                _waypointQueue.Enqueue(new Waypoint(homePos.Latitude, homePos.Longitude, homePos.Altitude));
                _returningHome = true;
            }
            else
            {
                _returningHome = false;
            }

            return (true, null);
        }
    }

    public (bool Success, string? Error) ClearMissionPlan()
    {
        lock (_lock)
        {
            if (_state is DroneState.TakingOff or DroneState.Flying or DroneState.Landing)
                return (false, "Cannot clear mission plan while airborne. Land first.");
            _missionPlan = null;
            _autoNavigateAfterHover = false;
            _waypointQueue.Clear();
            _returningHome = false;
            return (true, null);
        }
    }

    public MissionPlan? GetMissionPlan()
    {
        lock (_lock) return _missionPlan;
    }

    /// <summary>
    /// Called by FlightSimulatorService after TakingOff transitions to Hovering.
    /// If a mission plan triggered this takeoff, auto-transitions to Flying.
    /// Returns true when the transition occurred.
    /// </summary>
    public bool TryAutoNavigate()
    {
        lock (_lock)
        {
            if (!_autoNavigateAfterHover || _state != DroneState.Hovering || _waypointQueue.Count == 0)
            {
                _autoNavigateAfterHover = false;
                return false;
            }
            _autoNavigateAfterHover = false;
            _state = DroneState.Flying;
            return true;
        }
    }

    public (bool Success, string? Error) SetSpeed(double speedMps)
    {
        lock (_lock)
        {
            if (_state == DroneState.Offline)
                return (false, "Drone is offline. Power on first.");
            if (!double.IsFinite(speedMps))
                return (false, "Speed must be a finite number.");
            if (speedMps <= 0)
                return (false, "Speed must be greater than 0 m/s.");
            if (speedMps > _config.Performance.MaxSpeedMps)
                return (false, $"Speed {speedMps:F1} m/s exceeds maximum allowed speed of {_config.Performance.MaxSpeedMps:F1} m/s.");
            _desiredSpeedMps = speedMps;
            return (true, null);
        }
    }

    public (bool Success, string? Error) AdjustAltitude(double altitudeMeters)
    {
        lock (_lock)
        {
            if (_state is not (DroneState.Hovering or DroneState.Flying))
                return (false, $"Cannot adjust altitude from state {_state}. Drone must be Hovering or Flying.");
            if (!double.IsFinite(altitudeMeters))
                return (false, "Altitude must be a finite number.");
            if (altitudeMeters <= 0)
                return (false, "Altitude must be greater than 0 meters.");
            if (altitudeMeters > _config.Performance.MaxAltitudeMeters)
                return (false, $"Altitude {altitudeMeters:F1}m exceeds the maximum allowed altitude of {_config.Performance.MaxAltitudeMeters:F1}m.");
            _targetAltitude = altitudeMeters;
            // For Flying state, override the current leg's altitude target so the
            // simulator interpolates toward this value instead of the waypoint altitude.
            if (_state == DroneState.Flying)
                _activeLegAltitudeOverride = altitudeMeters;
            return (true, null);
        }
    }

    public (bool Success, string? Error) ReturnHome()
    {
        lock (_lock)
        {
            if (_state is not (DroneState.Hovering or DroneState.Flying))
                return (false, $"Cannot return home from state {_state}. Drone must be Hovering or Flying.");

            var home = _config.HomePosition;
            _waypointQueue.Clear();
            // Use current altitude so the drone flies level to home, then auto-lands on arrival
            _waypointQueue.Enqueue(new Waypoint(home.Latitude, home.Longitude, _position.Altitude));
            _returningHome = true;

            if (_state == DroneState.Hovering)
                _state = DroneState.Flying;

            return (true, null);
        }
    }

    // --- Simulator API (called by FlightSimulatorService only) ---

    public void UpdatePosition(double latitude, double longitude, double altitude,
        double headingDegrees, double speedMps)
    {
        lock (_lock)
        {
            _position = new GpsCoordinate(latitude, longitude, altitude);
            _headingDegrees = headingDegrees;
            _speedMps = speedMps;
        }
    }

    /// <summary>Returns false if battery hit zero (caller should trigger emergency land).</summary>
    public bool DrainBattery(double amountPercent)
    {
        lock (_lock)
        {
            _batteryPercent = Math.Max(0.0, _batteryPercent - amountPercent);
            return _batteryPercent > 0.0;
        }
    }

    /// <summary>Returns true when battery reaches 100% (caller should stop charging).</summary>
    public bool ChargeBattery(double amountPercent)
    {
        lock (_lock)
        {
            _batteryPercent = Math.Min(100.0, _batteryPercent + amountPercent);
            return _batteryPercent >= 100.0;
        }
    }

    public bool TransitionToHovering()
    {
        lock (_lock)
        {
            if (_state is not (DroneState.TakingOff or DroneState.Flying))
                return false;
            _state = DroneState.Hovering;
            _speedMps = 0;
            return true;
        }
    }

    public bool TransitionToIdle()
    {
        lock (_lock)
        {
            if (_state != DroneState.Landing)
                return false;
            _state = DroneState.Idle;
            _speedMps = 0;
            _autoNavigateAfterHover = false;
            var home = _config.HomePosition;
            _position = new GpsCoordinate(home.Latitude, home.Longitude, home.Altitude);
            return true;
        }
    }

    public bool TransitionChargingToIdle()
    {
        lock (_lock)
        {
            if (_state != DroneState.Charging)
                return false;
            _state = DroneState.Idle;
            return true;
        }
    }

    /// <summary>Forces landing regardless of state (emergency use only).</summary>
    public void ForceLand()
    {
        lock (_lock)
        {
            if (_state is DroneState.TakingOff or DroneState.Hovering or DroneState.Flying)
            {
                _state = DroneState.Landing;
                _speedMps = 0;
                _returningHome = false;
                _autoNavigateAfterHover = false;
            }
        }
    }

    public Waypoint? PeekNextWaypoint()
    {
        lock (_lock)
        {
            return _waypointQueue.TryPeek(out var wp) ? wp : null;
        }
    }

    public Waypoint? DequeueWaypoint()
    {
        lock (_lock)
        {
            var wp = _waypointQueue.TryDequeue(out var w) ? w : (Waypoint?)null;
            // Clear the per-leg altitude override so the next waypoint's altitude drives the next leg.
            if (wp is not null)
                _activeLegAltitudeOverride = null;
            return wp;
        }
    }

    public int WaypointQueueDepth()
    {
        lock (_lock) return _waypointQueue.Count;
    }

    public double TargetAltitude()
    {
        lock (_lock) return _targetAltitude;
    }

    public double? ActiveLegAltitudeOverride()
    {
        lock (_lock) return _activeLegAltitudeOverride;
    }

    // --- Snapshot ---

    public DroneStatus GetStatus()
    {
        lock (_lock)
        {
            return new DroneStatus(
                DroneId: _config.Id,
                DroneName: _config.Name,
                State: _state,
                Position: _position,
                HeadingDegrees: _headingDegrees,
                SpeedMps: _speedMps,
                BatteryPercent: Math.Round(_batteryPercent, 2),
                IsCharging: _state == DroneState.Charging,
                WaypointQueueDepth: _waypointQueue.Count,
                Timestamp: DateTimeOffset.UtcNow
            );
        }
    }

    public DroneState State { get { lock (_lock) return _state; } }
    public double BatteryPercent { get { lock (_lock) return _batteryPercent; } }
    public GpsCoordinate Position { get { lock (_lock) return _position; } }
    public double? DesiredSpeedMps { get { lock (_lock) return _desiredSpeedMps; } }
    public bool IsReturningHome { get { lock (_lock) return _returningHome; } }
    /// <summary>True when the active mission plan ends with a Land command (auto-lands on waypoint completion).</summary>
    public bool MissionHasAutoLand { get { lock (_lock) return _missionPlan?.HasLanding == true; } }
}
