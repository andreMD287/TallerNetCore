namespace DisasterMaps.Business.Visualization;

public sealed record ValidationMetrics(
    string Method,
    int Total,
    int Evaluated,
    int Skipped,
    double? Mae,
    double? Rmse);

public static class CrossValidator
{
    public static ValidationMetrics Evaluate(
        IReadOnlyList<SamplePoint> samples,
        Func<ISpatialInterpolator> factory,
        InterpolationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(parameters);

        if (samples.Count == 0)
            throw new ArgumentException(
                "Se necesita al menos una muestra.", nameof(samples));

        string method = factory().Method;
        int evaluated = 0;
        double sumAbsolute = 0;
        double sumSquared = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            var training = samples
                .Where((_, index) => index != i)
                .ToArray();

            var model = factory();

            try
            {
                model.Fit(training, parameters);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var actual = samples[i];
            double? predicted = model.Estimate(actual.Lat, actual.Lon);

            if (predicted is null || !double.IsFinite(predicted.Value))
                continue;

            double error = predicted.Value - actual.Value;

            evaluated++;
            sumAbsolute += Math.Abs(error);
            sumSquared += error * error;
        }

        return new ValidationMetrics(
            method,
            samples.Count,
            evaluated,
            samples.Count - evaluated,
            evaluated > 0 ? sumAbsolute / evaluated : null,
            evaluated > 0 ? Math.Sqrt(sumSquared / evaluated) : null);
    }
}