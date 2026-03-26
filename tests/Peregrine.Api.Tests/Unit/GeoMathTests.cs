using FluentAssertions;
using Peregrine.Api.Infrastructure;

namespace Peregrine.Api.Tests.Unit;

public sealed class GeoMathTests
{
    private const double TolerancePercent = 0.01; // 1% tolerance for floating-point GPS calculations

    // --- HaversineDistance ---

    [Fact]
    public void HaversineDistance_SamePoint_ReturnsZero()
    {
        var dist = GeoMath.HaversineDistance(37.7749, -122.4194, 37.7749, -122.4194);
        dist.Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void HaversineDistance_SanFranciscoToLosAngeles_ApproximatelyCorrect()
    {
        // SF: 37.7749°N, 122.4194°W → LA: 34.0522°N, 118.2437°W ≈ 559 km
        var distMeters = GeoMath.HaversineDistance(37.7749, -122.4194, 34.0522, -118.2437);
        var expectedMeters = 559_000.0;
        distMeters.Should().BeInRange(expectedMeters * 0.99, expectedMeters * 1.01);
    }

    [Fact]
    public void HaversineDistance_IsSymmetric()
    {
        var d1 = GeoMath.HaversineDistance(51.5074, -0.1278, 48.8566, 2.3522);
        var d2 = GeoMath.HaversineDistance(48.8566, 2.3522, 51.5074, -0.1278);
        d1.Should().BeApproximately(d2, 0.001);
    }

    [Fact]
    public void HaversineDistance_ShortDistance_SubMeter()
    {
        // Two points ~1 meter apart (roughly 0.000009 degrees of latitude ≈ 1m)
        var dist = GeoMath.HaversineDistance(0.0, 0.0, 0.000009, 0.0);
        dist.Should().BeInRange(0.5, 1.5);
    }

    [Fact]
    public void HaversineDistance_Antipodal_ApproximatelyHalfCircumference()
    {
        // Antipodal: opposite sides of Earth ≈ π × R ≈ 20,015 km
        var dist = GeoMath.HaversineDistance(0.0, 0.0, 0.0, 180.0);
        var expectedMeters = Math.PI * GeoMath.EarthRadiusMeters;
        dist.Should().BeInRange(expectedMeters * 0.999, expectedMeters * 1.001);
    }

    // --- Bearing ---

    [Fact]
    public void Bearing_DueNorth_Returns0()
    {
        var bearing = GeoMath.Bearing(0.0, 0.0, 1.0, 0.0);
        bearing.Should().BeApproximately(0.0, 0.1);
    }

    [Fact]
    public void Bearing_DueSouth_Returns180()
    {
        var bearing = GeoMath.Bearing(1.0, 0.0, 0.0, 0.0);
        bearing.Should().BeApproximately(180.0, 0.1);
    }

    [Fact]
    public void Bearing_DueEast_Returns90()
    {
        var bearing = GeoMath.Bearing(0.0, 0.0, 0.0, 1.0);
        bearing.Should().BeApproximately(90.0, 0.1);
    }

    [Fact]
    public void Bearing_DueWest_Returns270()
    {
        var bearing = GeoMath.Bearing(0.0, 1.0, 0.0, 0.0);
        bearing.Should().BeApproximately(270.0, 0.1);
    }

    [Fact]
    public void Bearing_IsAlwaysInRange0To360()
    {
        var testCases = new (double lat1, double lon1, double lat2, double lon2)[]
        {
            (51.5, -0.1, 48.9, 2.4),
            (-33.9, 151.2, 35.7, 139.7),
            (40.7, -74.0, -23.5, -46.6),
        };

        foreach (var (lat1, lon1, lat2, lon2) in testCases)
        {
            var bearing = GeoMath.Bearing(lat1, lon1, lat2, lon2);
            bearing.Should().BeInRange(0.0, 360.0);
        }
    }

    // --- MoveToward ---

    [Fact]
    public void MoveToward_ZeroDistance_ReturnsSamePoint()
    {
        var (lat, lon) = GeoMath.MoveToward(37.7749, -122.4194, 45.0, 0.0);
        lat.Should().BeApproximately(37.7749, 0.0001);
        lon.Should().BeApproximately(-122.4194, 0.0001);
    }

    [Fact]
    public void MoveToward_NorthwardStep_IncreasesLatitude()
    {
        var (lat, lon) = GeoMath.MoveToward(0.0, 0.0, 0.0, 1000.0); // bearing 0 = north
        lat.Should().BeGreaterThan(0.0);
        lon.Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void MoveToward_EastwardStep_IncreasesLongitude()
    {
        var (lat, lon) = GeoMath.MoveToward(0.0, 0.0, 90.0, 1000.0); // bearing 90 = east
        lat.Should().BeApproximately(0.0, 0.001);
        lon.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void MoveToward_DistanceIsConsistentWithHaversine()
    {
        var (newLat, newLon) = GeoMath.MoveToward(0.0, 0.0, 45.0, 500.0);
        var actualDist = GeoMath.HaversineDistance(0.0, 0.0, newLat, newLon);
        actualDist.Should().BeApproximately(500.0, 1.0); // within 1 metre
    }

    // --- Unit conversion ---

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, Math.PI / 2)]
    [InlineData(180.0, Math.PI)]
    [InlineData(360.0, 2 * Math.PI)]
    public void ToRadians_ConvertsCorrectly(double degrees, double expectedRadians)
    {
        GeoMath.ToRadians(degrees).Should().BeApproximately(expectedRadians, 1e-10);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(Math.PI / 2, 90.0)]
    [InlineData(Math.PI, 180.0)]
    public void ToDegrees_ConvertsCorrectly(double radians, double expectedDegrees)
    {
        GeoMath.ToDegrees(radians).Should().BeApproximately(expectedDegrees, 1e-10);
    }
}
