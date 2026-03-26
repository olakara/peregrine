namespace Peregrine.Api.Features.Battery;

public sealed record BatteryStatus(
    double BatteryPercent,
    bool IsCharging,
    string DroneState,
    DateTimeOffset Timestamp
);
