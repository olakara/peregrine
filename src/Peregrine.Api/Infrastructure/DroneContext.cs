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
    private bool _returningHome;
    private readonly Queue<Waypoint> _waypointQueue = new();

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
            if (_state != DroneState.Hovering)
                return (false, $"Cannot navigate from state {_state}. Drone must be Hovering.");
            if (_waypointQueue.Count == 0)
                return (false, "Waypoint queue is empty. Load waypoints before navigating.");
            _state = DroneState.Flying;
            return (true, null);
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

    public (bool Success, string? Error) SetSpeed(double speedMps)
    {
        lock (_lock)
        {
            if (_state == DroneState.Offline)
                return (false, "Drone is offline. Power on first.");
            if (speedMps <= 0)
                return (false, "Speed must be greater than 0 m/s.");
            if (speedMps > _config.Performance.MaxSpeedMps)
                return (false, $"Speed {speedMps:F1} m/s exceeds maximum allowed speed of {_config.Performance.MaxSpeedMps:F1} m/s.");
            _desiredSpeedMps = speedMps;
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
            return _waypointQueue.TryDequeue(out var wp) ? wp : null;
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
}
