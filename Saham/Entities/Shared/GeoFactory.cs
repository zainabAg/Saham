using NetTopologySuite;
using NetTopologySuite.Geometries;
using Volo.Abp;

namespace Saham.Entities.Shared;

/* All locations are WGS84 (SRID 4326) and stored as PostGIS geography (decision D1).
 * NTS uses (X = longitude, Y = latitude). */
public static class GeoFactory
{
    public const int Srid = 4326;

    private static readonly GeometryFactory Factory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: Srid);

    public static Point CreatePoint(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new BusinessException(SahamErrorCodes.InvalidLocation);
        }

        return Factory.CreatePoint(new Coordinate(longitude, latitude));
    }

    /* Polygon from a closed or open ring of (latitude, longitude) pairs. */
    public static MultiPolygon CreateArea(IReadOnlyList<(double Latitude, double Longitude)> ring)
    {
        if (ring.Count < 3)
        {
            throw new BusinessException(SahamErrorCodes.InvalidServiceArea);
        }

        var coordinates = ring.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToList();
        if (!coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(coordinates[0]);
        }

        var polygon = Factory.CreatePolygon(coordinates.ToArray());
        if (!polygon.IsValid)
        {
            throw new BusinessException(SahamErrorCodes.InvalidServiceArea);
        }

        return Factory.CreateMultiPolygon(new[] { polygon });
    }

    /* Great-circle distance in meters (TD-01: straight line). Used in memory;
     * database queries use PostGIS ST_Distance on geography, which gives the same result. */
    public static double DistanceMeters(Point a, Point b)
    {
        const double earthRadius = 6_371_000d;
        var lat1 = ToRadians(a.Y);
        var lat2 = ToRadians(b.Y);
        var dLat = lat2 - lat1;
        var dLon = ToRadians(b.X - a.X);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadius * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
