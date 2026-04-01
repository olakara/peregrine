namespace Peregrine.Api.Domain;

public sealed record GeoFenceCircle(double Latitude, double Longitude, double Radius, bool Inclusion);

public sealed record GeoFencePolygon(IReadOnlyList<GpsCoordinate> Vertices, bool Inclusion);

public sealed record RallyPoint(double Latitude, double Longitude, double Altitude);

public sealed record MissionPlan(
    IReadOnlyList<Waypoint> Waypoints,
    bool HasTakeoff,
    double? TakeoffAltitude,
    bool HasLanding,
    bool HasRtl,
    GpsCoordinate? PlannedHomePosition,
    IReadOnlyList<GeoFenceCircle> GeoFenceCircles,
    IReadOnlyList<GeoFencePolygon> GeoFencePolygons,
    IReadOnlyList<RallyPoint> RallyPoints);
