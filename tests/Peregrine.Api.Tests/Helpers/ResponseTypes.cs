namespace Peregrine.Api.Tests.Helpers;

/// <summary>Shared response DTOs for integration test deserialization.</summary>

public sealed record MessageStatusResponse(string? Message, DroneStatusDto? Status);

public sealed record DroneStatusDto(
    string? DroneId,
    string? DroneName,
    string? State,
    double BatteryPercent,
    bool IsCharging,
    int WaypointQueueDepth);

public sealed record ErrorResponse(string? Error);

public sealed record BatteryStatusResponse(
    double BatteryPercent,
    bool IsCharging,
    string? DroneState);
