namespace DisasterMaps.Business.Visualization;

public interface ISpatialInterpolator
{
    string Method { get; }
    void Fit(IReadOnlyList<SamplePoint> points, InterpolationParameters p);
    double? Estimate(double lat, double lon);
}