using DisasterMaps.Business.Visualization;
using Xunit;

namespace DisasterMaps.Business.Tests;

public sealed class VisualizationTests
{
    private static readonly SamplePoint[] Points =
    [
        new(4.60, -74.10, 10),
        new(4.60, -74.08, 30)
    ];

    [Fact]
    public void Idw_AtSample_ReturnsExactValue()
    {
        var model = new IdwInterpolator();
        model.Fit(Points, new InterpolationParameters());

        Assert.Equal(10.0, model.Estimate(4.60, -74.10));
    }

    [Fact]
    public void Idw_AtMidpoint_ReturnsAverage()
    {
        var model = new IdwInterpolator();
        model.Fit(Points, new InterpolationParameters());

        double? result = model.Estimate(4.60, -74.09);

        Assert.NotNull(result);
        Assert.InRange(result.Value, 19.99, 20.01);
    }

    [Fact]
    public void Idw_OutsideRadius_ReturnsNull()
    {
        var model = new IdwInterpolator();
        model.Fit(Points, new InterpolationParameters(
            MaxDistanceKm: 1));

        Assert.Null(model.Estimate(5.5, -75.0));
    }

    [Fact]
    public void Nearest_ReturnsClosestSample()
    {
        var model = new NearestNeighborInterpolator();
        model.Fit(Points, new InterpolationParameters());

        Assert.Equal(10.0, model.Estimate(4.60, -74.099));
        Assert.Equal(30.0, model.Estimate(4.60, -74.081));
    }

    [Fact]
    public void Nearest_OutsideRadius_ReturnsNull()
    {
        var model = new NearestNeighborInterpolator();
        model.Fit(Points, new InterpolationParameters(
            MaxDistanceKm: 1));

        Assert.Null(model.Estimate(5.5, -75.0));
    }

    [Fact]
    public void GeoMath_SamePoint_HasZeroDistance()
    {
        Assert.Equal(
            0,
            GeoMath.DistanciaKm(4.6, -74.1, 4.6, -74.1));
    }

    [Fact]
    public void Idw_InvalidPower_IsRejected()
    {
        var model = new IdwInterpolator();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => model.Fit(
                Points,
                new InterpolationParameters(Power: 0)));
    }
}