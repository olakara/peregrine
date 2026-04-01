using System.Text.Json;
using FluentAssertions;
using Peregrine.Api.Features.Mission;

namespace Peregrine.Api.Tests.Unit;

public sealed class MissionPlanParserTests
{
    private const string MinimalValidPlan = """
        {
          "fileType": "Plan",
          "version": 1,
          "groundStation": "QGroundControl",
          "mission": {
            "items": [],
            "version": 2
          },
          "geoFence": { "circles": [], "polygons": [], "version": 2 },
          "rallyPoints": { "points": [], "version": 2 }
        }
        """;

    private static QGroundControlPlan Deserialize(string json) =>
        JsonSerializer.Deserialize<QGroundControlPlan>(json, QGcPlanParser.JsonOptions)!;

    // --- Basic parsing ---

    [Fact]
    public void Parse_EmptyMission_ReturnsZeroWaypoints()
    {
        var raw = Deserialize(MinimalValidPlan);
        var plan = QGcPlanParser.Parse(raw);

        plan.Waypoints.Should().BeEmpty();
        plan.HasTakeoff.Should().BeFalse();
        plan.HasLanding.Should().BeFalse();
        plan.HasRtl.Should().BeFalse();
    }

    [Fact]
    public void Parse_WaypointItem_ExtractsCoordinates()
    {
        var json = BuildPlanWithItems("""
            {
              "type": "SimpleItem",
              "command": 16,
              "params": [0, 0, 0, null, 24.414516, 54.456488, 50.0],
              "autoContinue": true
            }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.Waypoints.Should().HaveCount(1);
        plan.Waypoints[0].Latitude.Should().BeApproximately(24.414516, 0.000001);
        plan.Waypoints[0].Longitude.Should().BeApproximately(54.456488, 0.000001);
        plan.Waypoints[0].Altitude.Should().BeApproximately(50.0, 0.01);
    }

    [Fact]
    public void Parse_TakeoffItem_SetsFlagAndAltitude()
    {
        var json = BuildPlanWithItems("""
            {
              "type": "SimpleItem",
              "command": 22,
              "params": [0, 0, 0, null, 0, 0, 30.0],
              "Altitude": 30.0
            }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.HasTakeoff.Should().BeTrue();
        plan.TakeoffAltitude.Should().BeApproximately(30.0, 0.01);
        plan.Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LandItem_SetsFlag()
    {
        var json = BuildPlanWithItems("""
            { "type": "SimpleItem", "command": 21, "params": [0,0,0,null,0,0,0] }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.HasLanding.Should().BeTrue();
    }

    [Fact]
    public void Parse_RtlItem_SetsFlag()
    {
        var json = BuildPlanWithItems("""
            { "type": "SimpleItem", "command": 20, "params": [0,0,0,null,0,0,0] }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.HasRtl.Should().BeTrue();
    }

    [Fact]
    public void Parse_ComplexItem_IsSkipped()
    {
        var json = BuildPlanWithItems("""
            {
              "type": "ComplexItem",
              "complexItemType": "survey",
              "items": []
            }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.Waypoints.Should().BeEmpty();
        plan.HasTakeoff.Should().BeFalse();
    }

    [Fact]
    public void Parse_UnknownCommand_IsSkipped()
    {
        var json = BuildPlanWithItems("""
            { "type": "SimpleItem", "command": 999, "params": [0,0,0,null,1.0,2.0,10.0] }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlannedHomePosition_IsMapped()
    {
        var json = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": {
                "items": [],
                "plannedHomePosition": [24.414516, 54.456488, 5.0],
                "version": 2
              },
              "geoFence": { "circles": [], "polygons": [], "version": 2 },
              "rallyPoints": { "points": [], "version": 2 }
            }
            """;

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.PlannedHomePosition.Should().NotBeNull();
        plan.PlannedHomePosition!.Latitude.Should().BeApproximately(24.414516, 0.000001);
    }

    // --- GeoFence ---

    [Fact]
    public void Parse_GeoFenceCircle_IsParsed()
    {
        var json = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": { "items": [], "version": 2 },
              "geoFence": {
                "circles": [{
                  "circle": { "center": [24.4, 54.4], "radius": 100.0 },
                  "inclusion": true,
                  "version": 1
                }],
                "polygons": [],
                "version": 2
              },
              "rallyPoints": { "points": [], "version": 2 }
            }
            """;

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.GeoFenceCircles.Should().HaveCount(1);
        plan.GeoFenceCircles[0].Radius.Should().BeApproximately(100.0, 0.01);
        plan.GeoFenceCircles[0].Inclusion.Should().BeTrue();
    }

    [Fact]
    public void Parse_GeoFencePolygon_IsParsed()
    {
        var json = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": { "items": [], "version": 2 },
              "geoFence": {
                "circles": [],
                "polygons": [{
                  "polygon": [[24.4, 54.4], [24.5, 54.4], [24.5, 54.5]],
                  "inclusion": false,
                  "version": 1
                }],
                "version": 2
              },
              "rallyPoints": { "points": [], "version": 2 }
            }
            """;

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.GeoFencePolygons.Should().HaveCount(1);
        plan.GeoFencePolygons[0].Vertices.Should().HaveCount(3);
        plan.GeoFencePolygons[0].Inclusion.Should().BeFalse();
    }

    // --- Rally points ---

    [Fact]
    public void Parse_RallyPoints_AreParsed()
    {
        var json = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": { "items": [], "version": 2 },
              "geoFence": { "circles": [], "polygons": [], "version": 2 },
              "rallyPoints": {
                "points": [[24.4, 54.4, 20.0], [24.5, 54.5, 25.0]],
                "version": 2
              }
            }
            """;

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.RallyPoints.Should().HaveCount(2);
        plan.RallyPoints[0].Altitude.Should().BeApproximately(20.0, 0.01);
    }

    // --- Full realistic plan ---

    [Fact]
    public void Parse_FullMission_ExtractsAllParts()
    {
        var json = """
            {
              "fileType": "Plan",
              "version": 1,
              "mission": {
                "items": [
                  { "type": "SimpleItem", "command": 22, "params": [0,0,0,null,0,0,30.0], "Altitude": 30.0 },
                  { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.414516,54.456488,30.0] },
                  { "type": "SimpleItem", "command": 16, "params": [0,0,0,null,24.420000,54.460000,30.0] },
                  { "type": "ComplexItem", "complexItemType": "survey" },
                  { "type": "SimpleItem", "command": 21, "params": [0,0,0,null,0,0,0] }
                ],
                "plannedHomePosition": [24.414516, 54.456488, 0.0],
                "version": 2
              },
              "geoFence": { "circles": [], "polygons": [], "version": 2 },
              "rallyPoints": { "points": [], "version": 2 }
            }
            """;

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.HasTakeoff.Should().BeTrue();
        plan.TakeoffAltitude.Should().BeApproximately(30.0, 0.01);
        plan.Waypoints.Should().HaveCount(2);
        plan.HasLanding.Should().BeTrue();
        plan.HasRtl.Should().BeFalse();
        plan.PlannedHomePosition.Should().NotBeNull();
    }

    // --- Helpers ---

    [Fact]
    public void Parse_WaypointWithShortParamsArray_IsSkippedWithoutThrowing()
    {
        // params has only 3 elements — indices 4/5/6 are out of bounds
        var json = BuildPlanWithItems("""
            { "type": "SimpleItem", "command": 16, "params": [0, 0, 0] }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        // Incomplete params → no valid coordinates → waypoint skipped
        plan.Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ItemWithNonStringType_IsSkippedWithoutThrowing()
    {
        // "type" is a number, not a string
        var json = BuildPlanWithItems("""
            { "type": 42, "command": 16, "params": [0,0,0,null,24.0,54.0,30.0] }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ItemWithNonIntegerCommand_IsSkippedWithoutThrowing()
    {
        // "command" is a string, not an int
        var json = BuildPlanWithItems("""
            { "type": "SimpleItem", "command": "NAV_WAYPOINT", "params": [0,0,0,null,24.0,54.0,30.0] }
            """);

        var plan = QGcPlanParser.Parse(Deserialize(json));

        plan.Waypoints.Should().BeEmpty();
    }

    // --- Helpers ---

    private static string BuildPlanWithItems(string itemJson) => $$"""
        {
          "fileType": "Plan",
          "version": 1,
          "mission": {
            "items": [{{itemJson}}],
            "version": 2
          },
          "geoFence": { "circles": [], "polygons": [], "version": 2 },
          "rallyPoints": { "points": [], "version": 2 }
        }
        """;
}
