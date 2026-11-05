namespace DisasterMaps.Business.Visualization;

public sealed class OrdinaryKrigingInterpolator : ISpatialInterpolator
{
    private IReadOnlyList<SamplePoint> points = [];
    private InterpolationParameters parameters = new();
    private Variogram variogram = new(15, 1);

    public string Method => "KRIGING";

    public void Fit(
        IReadOnlyList<SamplePoint> samples,
        InterpolationParameters p)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(p);

        if (samples.Count < 5)
            throw new ArgumentException(
                "Kriging requiere al menos cinco muestras.");

        if (p.Neighbors < 5 || p.MaxDistanceKm <= 0 ||
            !double.IsFinite(p.MaxDistanceKm))
            throw new ArgumentOutOfRangeException(nameof(p));

        if (samples.Any(s =>
            !double.IsFinite(s.Lat) ||
            !double.IsFinite(s.Lon) ||
            !double.IsFinite(s.Value) ||
            s.Lat is < -90 or > 90 ||
            s.Lon is < -180 or > 180))
            throw new ArgumentException("Muestras invalidas.");

        if (samples.Select(s => (s.Lat, s.Lon)).Distinct().Count() < 5)
            throw new ArgumentException(
                "Se necesitan cinco ubicaciones distintas.");

        points = samples.ToArray();
        parameters = p;

        double mean = points.Average(x => x.Value);
        double variance = points.Average(x =>
            Math.Pow(x.Value - mean, 2));

        variogram = new Variogram(
            p.MaxDistanceKm,
            Math.Max(variance, 1e-6),
            Math.Max(variance, 1e-6) * 1e-8);
    }

    public double? Estimate(double lat, double lon)
    {
        if (!double.IsFinite(lat) || !double.IsFinite(lon))
            return null;

        var neighbors = points
            .Select(p => new
            {
                Point = p,
                Distance = GeoMath.DistanciaKm(
                    lat, lon, p.Lat, p.Lon)
            })
            .Where(x => x.Distance <= parameters.MaxDistanceKm)
            .OrderBy(x => x.Distance)
            .Take(parameters.Neighbors)
            .ToArray();

        if (neighbors.Length < 5) return null;

        if (neighbors[0].Distance < 1e-8)
            return neighbors[0].Point.Value;

        int n = neighbors.Length;
        var a = new double[n + 1, n + 1];
        var b = new double[n + 1];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double d = GeoMath.DistanciaKm(
                    neighbors[i].Point.Lat,
                    neighbors[i].Point.Lon,
                    neighbors[j].Point.Lat,
                    neighbors[j].Point.Lon);

                a[i, j] = variogram.Covariance(d);

                if (i == j)
                    a[i, j] += variogram.Nugget;
            }

            a[i, n] = 1;
            a[n, i] = 1;
            b[i] = variogram.Covariance(
                neighbors[i].Distance);
        }

        b[n] = 1;

        var weights = Solve(a, b);
        if (weights is null) return null;

        double result = 0;

        for (int i = 0; i < n; i++)
            result += weights[i] * neighbors[i].Point.Value;

        return double.IsFinite(result) ? result : null;
    }

    private static double[]? Solve(double[,] matrix, double[] rhs)
    {
        int n = rhs.Length;
        var a = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();

        for (int k = 0; k < n; k++)
        {
            int pivot = k;

            for (int i = k + 1; i < n; i++)
                if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k]))
                    pivot = i;

            if (Math.Abs(a[pivot, k]) < 1e-12)
                return null;

            for (int j = k; j < n; j++)
                (a[k, j], a[pivot, j]) =
                    (a[pivot, j], a[k, j]);

            (b[k], b[pivot]) = (b[pivot], b[k]);

            for (int i = k + 1; i < n; i++)
            {
                double factor = a[i, k] / a[k, k];

                for (int j = k; j < n; j++)
                    a[i, j] -= factor * a[k, j];

                b[i] -= factor * b[k];
            }
        }

        var x = new double[n];

        for (int i = n - 1; i >= 0; i--)
        {
            double sum = b[i];

            for (int j = i + 1; j < n; j++)
                sum -= a[i, j] * x[j];

            x[i] = sum / a[i, i];
        }

        return x;
    }
}