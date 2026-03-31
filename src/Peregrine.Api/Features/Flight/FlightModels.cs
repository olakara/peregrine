namespace Peregrine.Api.Features.Flight;

public sealed record TakeOffRequest(double? Altitude = null);
public sealed record SetSpeedRequest(double SpeedMps);
public sealed record AdjustAltitudeRequest(double AltitudeMeters);
