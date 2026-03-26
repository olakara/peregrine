namespace Peregrine.Api.Infrastructure;

internal static class GeoMath
{
    internal const double EarthRadiusMeters = 6_371_000.0;

    internal static double HaversineDistance(
        double lat1Deg, double lon1Deg,
        double lat2Deg, double lon2Deg)
    {
        var lat1 = ToRadians(lat1Deg);
        var lat2 = ToRadians(lat2Deg);
        var dLat = ToRadians(lat2Deg - lat1Deg);
        var dLon = ToRadians(lon2Deg - lon1Deg);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1) * Math.Cos(lat2)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    internal static double Bearing(
        double lat1Deg, double lon1Deg,
        double lat2Deg, double lon2Deg)
    {
        var lat1 = ToRadians(lat1Deg);
        var lat2 = ToRadians(lat2Deg);
        var dLon = ToRadians(lon2Deg - lon1Deg);

        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2)
              - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

        return (ToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    internal static (double Lat, double Lon) MoveToward(
        double latDeg, double lonDeg,
        double bearingDeg, double distanceMeters)
    {
        var lat = ToRadians(latDeg);
        var lon = ToRadians(lonDeg);
        var bearing = ToRadians(bearingDeg);
        var angular = distanceMeters / EarthRadiusMeters;

        var newLat = Math.Asin(
            Math.Sin(lat) * Math.Cos(angular)
            + Math.Cos(lat) * Math.Sin(angular) * Math.Cos(bearing));

        var newLon = lon + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat),
            Math.Cos(angular) - Math.Sin(lat) * Math.Sin(newLat));

        return (ToDegrees(newLat), ToDegrees(newLon));
    }

    internal static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    internal static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
