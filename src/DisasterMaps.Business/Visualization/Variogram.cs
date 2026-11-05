namespace DisasterMaps.Business.Visualization;

public sealed class Variogram
{
    public double RangeKm { get; }
    public double Sill { get; }
    public double Nugget { get; }

    public Variogram(double rangeKm, double sill, double nugget = 0)
    {
        if (!double.IsFinite(rangeKm) || rangeKm <= 0 ||
            !double.IsFinite(sill) || sill <= 0 ||
            !double.IsFinite(nugget) || nugget < 0)
            throw new ArgumentOutOfRangeException(nameof(rangeKm));

        RangeKm = rangeKm;
        Sill = sill;
        Nugget = nugget;
    }

    public double Covariance(double distanceKm)
    {
        if (distanceKm >= RangeKm) return 0;

        double r = Math.Max(0, distanceKm) / RangeKm;

        return Sill * (1 - 1.5 * r + 0.5 * r * r * r);
    }
}