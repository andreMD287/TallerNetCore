namespace DisasterMaps.Business.Visualization;

public static class GeoMath
{
    private const double RadioTierraKm = 6371.0088;

    public static double DistanciaKm(
        double lat1, double lon1, double lat2, double lon2)
    {
        double a1 = lat1 * Math.PI / 180;
        double a2 = lat2 * Math.PI / 180;
        double deltaLat = (lat2 - lat1) * Math.PI / 180;
        double deltaLon = (lon2 - lon1) * Math.PI / 180;

        double h = Math.Pow(Math.Sin(deltaLat / 2), 2)
            + Math.Cos(a1) * Math.Cos(a2)
            * Math.Pow(Math.Sin(deltaLon / 2), 2);

        return 2 * RadioTierraKm *
            Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }

    public static double GradosLatitud(double km) =>
        km / 111.32;

    public static double GradosLongitud(double km, double lat)
    {
        double coseno = Math.Cos(lat * Math.PI / 180);
        if (Math.Abs(coseno) < 1e-9)
            throw new ArgumentOutOfRangeException(nameof(lat));

        return km / (111.32 * coseno);
    }
}