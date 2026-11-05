using DisasterMaps.Business.Visualization;
using Xunit;

namespace DisasterMaps.Business.Tests;

public sealed class GridBuilderTests
{
    private static readonly SamplePoint[] Points =
    [
        new(4.600, -74.100, 10),
        new(4.610, -74.100, 20),
        new(4.600, -74.090, 30),
        new(4.610, -74.090, 40),
        new(4.605, -74.095, 25),
        new(4.615, -74.095, 35)
    ];

    private static IdwInterpolator CreateModel()
    {
        var model = new IdwInterpolator();
        model.Fit(Points, new InterpolationParameters());
        return model;
    }

    [Fact]
    public void Grid_GeneratesValidDimensions()
    {
        var grid = GridBuilder.Build(
            Points,
            CreateModel(),
            cellSizeKm: 1,
            bounds: new GeographicBounds(
                4.59, 4.62, -74.11, -74.08));

        Assert.True(grid.Rows > 0);
        Assert.True(grid.Cols > 0);
        Assert.Equal(grid.Rows, grid.Values.Length);
        Assert.All(grid.Values,
            row => Assert.Equal(grid.Cols, row.Length));
    }

    [Fact]
    public void Grid_ContainsInterpolatedValues()
    {
        var grid = GridBuilder.Build(
            Points,
            CreateModel(),
            cellSizeKm: 1,
            bounds: new GeographicBounds(
                4.59, 4.62, -74.11, -74.08));

        Assert.Contains(
            grid.Values.SelectMany(row => row),
            value => value.HasValue &&
                     double.IsFinite(value.Value));
    }

    [Fact]
    public void Grid_RespectsMaximumCellCount()
    {
        var grid = GridBuilder.Build(
            Points,
            CreateModel(),
            maxCells: 4,
            bounds: new GeographicBounds(
                4.59, 4.62, -74.11, -74.08));

        Assert.InRange(
            (long)grid.Rows * grid.Cols,
            1, 4);
    }

    [Fact]
    public void Grid_FarFromSamples_HasNoValues()
    {
        var grid = GridBuilder.Build(
            Points,
            CreateModel(),
            maxDistanceKm: 1,
            cellSizeKm: 1,
            bounds: new GeographicBounds(
                5.0, 5.02, -75.0, -74.98));

        Assert.All(
            grid.Values.SelectMany(row => row),
            value => Assert.Null(value));
    }

    [Fact]
    public void Classifier_UsesFourSeverityLevels()
    {
        Assert.Equal(
            "LOW",
            AffectationLevelClassifier.ClassifySeverity(1));

        Assert.Equal(
            "MEDIUM",
            AffectationLevelClassifier.ClassifySeverity(2));

        Assert.Equal(
            "HIGH",
            AffectationLevelClassifier.ClassifySeverity(3));

        Assert.Equal(
            "CRITICAL",
            AffectationLevelClassifier.ClassifySeverity(4));
    }
}