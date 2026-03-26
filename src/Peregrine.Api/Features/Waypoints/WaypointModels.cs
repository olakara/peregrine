namespace Peregrine.Api.Features.Waypoints;

public sealed record WaypointRequest(double Latitude, double Longitude, double Altitude, double? SpeedMps = null);
