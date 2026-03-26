namespace Peregrine.Api.Domain;

public sealed record DroneStatus(
    string DroneId,
    string DroneName,
    DroneState State,
    GpsCoordinate Position,
    double HeadingDegrees,
    double SpeedMps,
    double BatteryPercent,
    bool IsCharging,
    int WaypointQueueDepth,
    DateTimeOffset Timestamp
);
