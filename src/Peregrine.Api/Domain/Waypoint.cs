namespace Peregrine.Api.Domain;

public sealed record Waypoint(double Latitude, double Longitude, double Altitude, double? SpeedMps = null);
