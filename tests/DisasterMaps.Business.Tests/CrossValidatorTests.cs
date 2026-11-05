using DisasterMaps.Business.Visualization;
using Xunit;

namespace DisasterMaps.Business.Tests;

public sealed class CrossValidatorTests
{
    private static readonly SamplePoint[] Samples =
    [
        new(4.60, -74.10, 10),
        new(4.61, -74.10, 20),
        new(4.60, -74.09, 30),
        new(4.61, -74.09, 40),
        new(4.605, -74.095, 25),
        new(4.615, -74.095, 35)
    ];

    [Fact]
    public void Idw_CalculatesValidMetrics()
    {
        var result = CrossValidator.Evaluate(
            Samples,
            () => new IdwInterpolator(),
            new InterpolationParameters());

        Assert.Equal("IDW", result.Method);
        Assert.Equal(6, result.Evaluated);
        Assert.Equal(0, result.Skipped);
        Assert.NotNull(result.Mae);
        Assert.NotNull(result.Rmse);
        Assert.True(result.Rmse >= result.Mae);
    }

    [Fact]
    public void Nearest_CalculatesValidMetrics()
    {
        var result = CrossValidator.Evaluate(
            Samples,
            () => new NearestNeighborInterpolator(),
            new InterpolationParameters());

        Assert.Equal(6, result.Evaluated);
        Assert.True(result.Mae >= 0);
        Assert.True(result.Rmse >= result.Mae);
    }

    [Fact]
    public void Kriging_CalculatesMetrics()
    {
        var result = CrossValidator.Evaluate(
            Samples,
            () => new OrdinaryKrigingInterpolator(),
            new InterpolationParameters());

        Assert.Equal("KRIGING", result.Method);
        Assert.Equal(6, result.Total);
        Assert.Equal(result.Total, result.Evaluated + result.Skipped);
        Assert.True(result.Evaluated > 0);
        Assert.NotNull(result.Rmse);
    }

    [Fact]
    public void Kriging_InsufficientSamples_ReturnsNoMetrics()
    {
        var result = CrossValidator.Evaluate(
            Samples.Take(4).ToArray(),
            () => new OrdinaryKrigingInterpolator(),
            new InterpolationParameters());

        Assert.Equal(0, result.Evaluated);
        Assert.Equal(4, result.Skipped);
        Assert.Null(result.Mae);
        Assert.Null(result.Rmse);
    }
}