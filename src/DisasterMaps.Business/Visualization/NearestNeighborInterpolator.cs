namespace DisasterMaps.Business.Visualization;

public sealed class NearestNeighborInterpolator : ISpatialInterpolator
{
    private IReadOnlyList<SamplePoint> puntos = [];
    private double distanciaMaximaKm = 15;

    public string Method => "NEAREST";

    public void Fit(
        IReadOnlyList<SamplePoint> points,
        InterpolationParameters p)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(p);

        if (p.MaxDistanceKm <= 0)
            throw new ArgumentOutOfRangeException(nameof(p));

        puntos = points.ToArray();
        distanciaMaximaKm = p.MaxDistanceKm;
    }

    public double? Estimate(double lat, double lon)
    {
        var cercano = puntos
            .Select(p => new
            {
                p.Value,
                Distancia = GeoMath.DistanciaKm(
                    lat, lon, p.Lat, p.Lon)
            })
            .OrderBy(p => p.Distancia)
            .FirstOrDefault();

        return cercano is not null &&
               cercano.Distancia <= distanciaMaximaKm
            ? cercano.Value
            : null;
    }
}