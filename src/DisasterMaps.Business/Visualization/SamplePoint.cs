namespace DisasterMaps.Business.Visualization;

public sealed record SamplePoint(double Lat, double Lon, double Value);

public sealed record InterpolationParameters(
    double Power = 2,
    int Neighbors = 12,
    double MaxDistanceKm = 15);