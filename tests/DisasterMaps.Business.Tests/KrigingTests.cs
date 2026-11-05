using DisasterMaps.Business.Visualization;
using Xunit;

namespace DisasterMaps.Business.Tests;

public sealed class KrigingTests
{
    private static readonly SamplePoint[] Points =
    [
        new(4.60, -74.10, 10),
        new(4.61, -74.10, 20),
        new(4.60, -74.09, 30),
        new(4.61, -74.09, 40),
        new(4.605, -74.095, 25),
        new(4.615, -74.095, 35)
    ];

    [Fact]
    public void Kriging_AtSample_ReturnsExactValue()
    {
        var model = new OrdinaryKrigingInterpolator();
        model.Fit(Points, new InterpolationParameters());

        Assert.Equal(10.0, model.Estimate(4.60, -74.10));
    }

    [Fact]
    public void Kriging_InsideArea_ReturnsFiniteValue()
    {
        var model = new OrdinaryKrigingInterpolator();
        model.Fit(Points, new InterpolationParameters());

        var result = model.Estimate(4.607, -74.097);

        Assert.NotNull(result);
        Assert.True(double.IsFinite(result.Value));
    }

    [Fact]
    public void Kriging_ConstantField_ReturnsConstant()
    {
        var constantPoints = Points
            .Select(p => p with { Value = 25 })
            .ToArray();

        var model = new OrdinaryKrigingInterpolator();
        model.Fit(constantPoints, new InterpolationParameters());

        var result = model.Estimate(4.607, -74.097);

        Assert.NotNull(result);
        Assert.InRange(result.Value, 24.999, 25.001);
    }

    [Fact]
    public void Kriging_FewerThanFiveSamples_IsRejected()
    {
        var model = new OrdinaryKrigingInterpolator();

        Assert.Throws<ArgumentException>(() =>
            model.Fit(
                Points.Take(4).ToArray(),
                new InterpolationParameters()));
    }

    [Fact]
    public void Kriging_OutsideRadius_ReturnsNull()
    {
        var model = new OrdinaryKrigingInterpolator();
        model.Fit(Points, new InterpolationParameters());

        Assert.Null(model.Estimate(6.0, -76.0));
    }

    [Fact]
    public void Variogram_Covariance_DecreasesWithDistance()
    {
        var variogram = new Variogram(15, 100);

        Assert.True(
            variogram.Covariance(0) >
            variogram.Covariance(5));

        Assert.True(
            variogram.Covariance(5) >
            variogram.Covariance(10));

        Assert.Equal(0, variogram.Covariance(15));
    }
}