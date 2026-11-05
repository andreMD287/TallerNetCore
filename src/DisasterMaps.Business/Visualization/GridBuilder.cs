namespace DisasterMaps.Business.Visualization;

public sealed record GeographicBounds(
    double MinLat, double MaxLat,
    double MinLon, double MaxLon);

public sealed record SpatialGrid(
    double MinLat, double MinLon,
    double StepLat, double StepLon,
    int Rows, int Cols,
    double?[][] Values);

public static class GridBuilder
{
    public static SpatialGrid Build(
        IReadOnlyList<SamplePoint> points,
        ISpatialInterpolator interpolator,
        double maxDistanceKm = 15,
        double? cellSizeKm = null,
        int maxCells = 40000,
        GeographicBounds? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(interpolator);

        if (points.Count == 0)
            throw new ArgumentException("No hay puntos para generar la malla.");

        if (!double.IsFinite(maxDistanceKm) || maxDistanceKm <= 0 ||
            maxCells <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDistanceKm));

        if (cellSizeKm is { } size &&
            (!double.IsFinite(size) || size <= 0))
            throw new ArgumentOutOfRangeException(nameof(cellSizeKm));

        double minLat, maxLat, minLon, maxLon;

        if (bounds is null)
        {
            minLat = points.Min(p => p.Lat);
            maxLat = points.Max(p => p.Lat);
            minLon = points.Min(p => p.Lon);
            maxLon = points.Max(p => p.Lon);

            double middleLat = (minLat + maxLat) / 2;
            double marginLat = GeoMath.GradosLatitud(maxDistanceKm);
            double marginLon = GeoMath.GradosLongitud(
                maxDistanceKm, middleLat);

            minLat = Math.Max(-90, minLat - marginLat);
            maxLat = Math.Min(90, maxLat + marginLat);
            minLon = Math.Max(-180, minLon - marginLon);
            maxLon = Math.Min(180, maxLon + marginLon);
        }
        else
        {
            minLat = bounds.MinLat;
            maxLat = bounds.MaxLat;
            minLon = bounds.MinLon;
            maxLon = bounds.MaxLon;
        }

        if (!double.IsFinite(minLat) || !double.IsFinite(maxLat) ||
            !double.IsFinite(minLon) || !double.IsFinite(maxLon) ||
            minLat < -90 || maxLat > 90 ||
            minLon < -180 || maxLon > 180 ||
            minLat >= maxLat || minLon >= maxLon)
            throw new ArgumentException("La caja geografica no es valida.");

        double centerLat = (minLat + maxLat) / 2;
        double heightKm = (maxLat - minLat) * 111.32;
        double widthKm = (maxLon - minLon) *
            111.32 * Math.Cos(centerLat * Math.PI / 180);

        if (widthKm <= 0 || heightKm <= 0)
            throw new ArgumentException("Extension geografica invalida.");

        double sizeKm = cellSizeKm ?? 1.0;

        static (int Rows, int Cols) Dimensions(
            double h, double w, double size)
        {
            return (
                Math.Max(1, (int)Math.Ceiling(h / size)),
                Math.Max(1, (int)Math.Ceiling(w / size))
            );
        }

        var dimensions = Dimensions(heightKm, widthKm, sizeKm);

        if (cellSizeKm is null)
        {
            while ((long)dimensions.Rows * dimensions.Cols > maxCells)
            {
                sizeKm *= 1.1;
                dimensions = Dimensions(heightKm, widthKm, sizeKm);
            }
        }
        else if ((long)dimensions.Rows * dimensions.Cols > maxCells)
        {
            throw new ArgumentException(
                "La resolucion solicitada supera MaxCells.");
        }

        double stepLat = (maxLat - minLat) / dimensions.Rows;
        double stepLon = (maxLon - minLon) / dimensions.Cols;

        var values = new double?[dimensions.Rows][];

        for (int row = 0; row < dimensions.Rows; row++)
        {
            values[row] = new double?[dimensions.Cols];

            double lat = minLat + (row + 0.5) * stepLat;

            for (int col = 0; col < dimensions.Cols; col++)
            {
                double lon = minLon + (col + 0.5) * stepLon;

                bool hasNearbyPoint = points.Any(p =>
                    GeoMath.DistanciaKm(
                        lat, lon, p.Lat, p.Lon) <= maxDistanceKm);

                values[row][col] = hasNearbyPoint
                    ? interpolator.Estimate(lat, lon)
                    : null;
            }
        }

        return new SpatialGrid(
            minLat, minLon, stepLat, stepLon,
            dimensions.Rows, dimensions.Cols, values);
    }
}