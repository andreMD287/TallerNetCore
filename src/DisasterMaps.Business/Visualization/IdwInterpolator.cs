namespace DisasterMaps.Business.Visualization;

public sealed class IdwInterpolator : ISpatialInterpolator
{
    private IReadOnlyList<SamplePoint> puntos = [];
    private InterpolationParameters parametros = new();

    public string Method => "IDW";

    public void Fit(
        IReadOnlyList<SamplePoint> points,
        InterpolationParameters p)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(p);

        if (p.Power is < 1 or > 5 ||
            p.Neighbors <= 0 ||
            p.MaxDistanceKm <= 0)
            throw new ArgumentOutOfRangeException(nameof(p));

        puntos = points.ToArray();
        parametros = p;
    }

    public double? Estimate(double lat, double lon)
    {
        var distancias = puntos
            .Select(p => new
            {
                p.Value,
                Distancia = GeoMath.DistanciaKm(
                    lat, lon, p.Lat, p.Lon)
            })
            .Where(p => p.Distancia <= parametros.MaxDistanceKm)
            .OrderBy(p => p.Distancia)
            .Take(parametros.Neighbors)
            .ToArray();

        if (distancias.Length == 0)
            return null;

        if (distancias[0].Distancia < 1e-9)
            return distancias[0].Value;

        double sumaPesos = 0;
        double sumaValores = 0;

        foreach (var punto in distancias)
        {
            double peso = 1.0 /
                Math.Pow(punto.Distancia, parametros.Power);

            sumaPesos += peso;
            sumaValores += peso * punto.Value;
        }

        return sumaValores / sumaPesos;
    }
}