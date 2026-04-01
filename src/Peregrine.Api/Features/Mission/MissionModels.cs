using System.Text.Json;
using System.Text.Json.Serialization;
using Peregrine.Api.Domain;

namespace Peregrine.Api.Features.Mission;

// ---------------------------------------------------------------------------
// QGroundControl .plan JSON DTOs (internal — only used for deserialization)
// ---------------------------------------------------------------------------

internal sealed class QGroundControlPlan
{
    [JsonPropertyName("fileType")] public string FileType { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("mission")] public QGcMission Mission { get; init; } = new();
    [JsonPropertyName("geoFence")] public QGcGeoFence GeoFence { get; init; } = new();
    [JsonPropertyName("rallyPoints")] public QGcRallyPoints RallyPoints { get; init; } = new();
}

internal sealed class QGcMission
{
    [JsonPropertyName("items")] public List<JsonElement> Items { get; init; } = [];
    [JsonPropertyName("plannedHomePosition")] public double[]? PlannedHomePosition { get; init; }
    [JsonPropertyName("cruiseSpeed")] public double? CruiseSpeed { get; init; }
    [JsonPropertyName("hoverSpeed")] public double? HoverSpeed { get; init; }
}

internal sealed class QGcGeoFence
{
    [JsonPropertyName("circles")] public List<JsonElement> Circles { get; init; } = [];
    [JsonPropertyName("polygons")] public List<JsonElement> Polygons { get; init; } = [];
}

internal sealed class QGcRallyPoints
{
    [JsonPropertyName("points")] public List<double[]> Points { get; init; } = [];
}

// ---------------------------------------------------------------------------
// API response types
// ---------------------------------------------------------------------------

public sealed record MissionHomePosition(double Latitude, double Longitude, double Altitude);

public sealed record MissionPlanStatus(
    int WaypointCount,
    bool HasTakeoff,
    double? TakeoffAltitude,
    bool HasLanding,
    bool HasRtl,
    int GeoFenceCircleCount,
    int GeoFencePolygonCount,
    int RallyPointCount,
    MissionHomePosition? PlannedHomePosition);

// ---------------------------------------------------------------------------
// Parser — converts a raw QGC plan into the MissionPlan domain object
// ---------------------------------------------------------------------------

internal static class QGcPlanParser
{
    private const int CmdNavWaypoint = 16;
    private const int CmdNavReturnToLaunch = 20;
    private const int CmdNavLand = 21;
    private const int CmdNavTakeoff = 22;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static MissionPlan Parse(QGroundControlPlan raw)
    {
        var waypoints = new List<Waypoint>();
        var hasTakeoff = false;
        double? takeoffAltitude = null;
        var hasLanding = false;
        var hasRtl = false;

        foreach (var item in raw.Mission.Items)
        {
            if (!item.TryGetProperty("type", out var typeProp))
                continue;

            if (typeProp.ValueKind != JsonValueKind.String)
                continue;

            // Skip ComplexItems (surveys, corridors, structure scans, etc.)
            if (typeProp.GetString() == "ComplexItem")
                continue;

            if (typeProp.GetString() != "SimpleItem")
                continue;

            if (!item.TryGetProperty("command", out var cmdProp))
                continue;

            if (cmdProp.ValueKind != JsonValueKind.Number || !cmdProp.TryGetInt32(out var cmd))
                continue;

            switch (cmd)
            {
                case CmdNavTakeoff:
                    hasTakeoff = true;
                    takeoffAltitude = ExtractAltitude(item);
                    break;

                case CmdNavWaypoint:
                    var wp = ExtractWaypoint(item);
                    if (wp is not null)
                        waypoints.Add(wp);
                    break;

                case CmdNavLand:
                    hasLanding = true;
                    break;

                case CmdNavReturnToLaunch:
                    hasRtl = true;
                    break;

                // All other commands are silently skipped
            }
        }

        GpsCoordinate? plannedHome = null;
        if (raw.Mission.PlannedHomePosition is { Length: >= 3 } home)
            plannedHome = new GpsCoordinate(home[0], home[1], home[2]);

        var geoFenceCircles = ParseGeoFenceCircles(raw.GeoFence.Circles);
        var geoFencePolygons = ParseGeoFencePolygons(raw.GeoFence.Polygons);

        var rallyPoints = raw.RallyPoints.Points
            .Where(p => p.Length >= 3)
            .Select(p => new RallyPoint(p[0], p[1], p[2]))
            .ToList();

        return new MissionPlan(
            Waypoints: waypoints,
            HasTakeoff: hasTakeoff,
            TakeoffAltitude: takeoffAltitude,
            HasLanding: hasLanding,
            HasRtl: hasRtl,
            PlannedHomePosition: plannedHome,
            GeoFenceCircles: geoFenceCircles,
            GeoFencePolygons: geoFencePolygons,
            RallyPoints: rallyPoints);
    }

    public static MissionPlanStatus ToStatus(MissionPlan plan) => new(
        WaypointCount: plan.Waypoints.Count,
        HasTakeoff: plan.HasTakeoff,
        TakeoffAltitude: plan.TakeoffAltitude,
        HasLanding: plan.HasLanding,
        HasRtl: plan.HasRtl,
        GeoFenceCircleCount: plan.GeoFenceCircles.Count,
        GeoFencePolygonCount: plan.GeoFencePolygons.Count,
        RallyPointCount: plan.RallyPoints.Count,
        PlannedHomePosition: plan.PlannedHomePosition is { } h
            ? new MissionHomePosition(h.Latitude, h.Longitude, h.Altitude)
            : null);

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static double? ExtractAltitude(JsonElement item)
    {
        // Try params[6] first (MAVLink standard altitude position)
        if (item.TryGetProperty("params", out var paramsEl))
        {
            var alt = GetParamAt(paramsEl, 6);
            if (alt is > 0)
                return alt;
        }

        // Fallback to top-level "Altitude" field (QGC display value)
        if (item.TryGetProperty("Altitude", out var altEl) && altEl.ValueKind == JsonValueKind.Number)
            return altEl.GetDouble();

        return null;
    }

    private static Waypoint? ExtractWaypoint(JsonElement item)
    {
        if (!item.TryGetProperty("params", out var paramsEl))
            return null;

        var lat = GetParamAt(paramsEl, 4);
        var lon = GetParamAt(paramsEl, 5);
        var alt = GetParamAt(paramsEl, 6);

        if (lat is null || lon is null || alt is null)
            return null;

        return new Waypoint(lat.Value, lon.Value, alt.Value);
    }

    private static double? GetParamAt(JsonElement paramsArray, int index)
    {
        if (paramsArray.ValueKind != JsonValueKind.Array)
            return null;

        var length = paramsArray.GetArrayLength();
        if (index < 0 || index >= length)
            return null;

        var el = paramsArray[index];
        return el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
    }

    private static List<GeoFenceCircle> ParseGeoFenceCircles(List<JsonElement> rawCircles)
    {
        var result = new List<GeoFenceCircle>();
        foreach (var item in rawCircles)
        {
            if (!item.TryGetProperty("circle", out var circleEl))
                continue;
            if (!circleEl.TryGetProperty("center", out var centerEl))
                continue;
            if (centerEl.ValueKind != JsonValueKind.Array)
                continue;
            if (!circleEl.TryGetProperty("radius", out var radiusEl))
                continue;
            if (radiusEl.ValueKind != JsonValueKind.Number)
                continue;

            var center = centerEl.EnumerateArray().ToList();
            if (center.Count < 2)
                continue;
            if (center[0].ValueKind != JsonValueKind.Number || center[1].ValueKind != JsonValueKind.Number)
                continue;

            var inclusion = !item.TryGetProperty("inclusion", out var inclEl)
                || inclEl.ValueKind != JsonValueKind.False && inclEl.ValueKind != JsonValueKind.True
                || inclEl.GetBoolean();
            result.Add(new GeoFenceCircle(
                Latitude: center[0].GetDouble(),
                Longitude: center[1].GetDouble(),
                Radius: radiusEl.GetDouble(),
                Inclusion: inclusion));
        }
        return result;
    }

    private static List<GeoFencePolygon> ParseGeoFencePolygons(List<JsonElement> rawPolygons)
    {
        var result = new List<GeoFencePolygon>();
        foreach (var item in rawPolygons)
        {
            if (!item.TryGetProperty("polygon", out var polyEl))
                continue;
            if (polyEl.ValueKind != JsonValueKind.Array)
                continue;

            var vertices = polyEl.EnumerateArray()
                .Select(v =>
                {
                    if (v.ValueKind != JsonValueKind.Array)
                        return null;
                    var pts = v.EnumerateArray().ToList();
                    if (pts.Count < 2 || pts[0].ValueKind != JsonValueKind.Number || pts[1].ValueKind != JsonValueKind.Number)
                        return null;
                    return new GpsCoordinate(pts[0].GetDouble(), pts[1].GetDouble(), 0);
                })
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList();

            if (vertices.Count < 3)
                continue;

            var inclusion = !item.TryGetProperty("inclusion", out var inclEl)
                || inclEl.ValueKind != JsonValueKind.False && inclEl.ValueKind != JsonValueKind.True
                || inclEl.GetBoolean();
            result.Add(new GeoFencePolygon(vertices, inclusion));
        }
        return result;
    }
}
