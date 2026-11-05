using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Visualization;

namespace DisasterMaps.Business.Tests;

/// <summary>Mapa de calor del nivel de afectación 1..5 capturado por la app móvil.</summary>
public sealed class VisualizationLevelTests
{
    private readonly FakeDataGateway _gateway = new();

    private VisualizationService Service() =>
        new(_gateway, new TestOptionsMonitor<SeverityThresholds>(new SeverityThresholds()));

    /// <summary>Puntos alrededor de Mocoa: nivel 5 en el centro y nivel 1 en la periferia.</summary>
    private async Task SeedAsync(int count)
    {
        var random = new Random(3);
        var impacts = Enumerable.Range(0, count).Select(_ =>
        {
            double lat = 1.15 + (random.NextDouble() - 0.5) * 0.1;
            double lon = -76.65 + (random.NextDouble() - 0.5) * 0.1;
            double km = Math.Sqrt(Math.Pow(lat - 1.15, 2) + Math.Pow(lon + 76.65, 2)) * 111;
            int level = Math.Clamp(5 - (int)(km / 1.5), 1, 5);
            return new ImpactDto(Guid.CreateVersion7(), FakeDataGateway.MocoaEventId, lat, lon,
                AffectationLevels.ToSeverity(level), 0, null, null, null, Samples.Now,
                ImpactSource.Mobile, ImpactStatus.Reported) { AffectationLevel = level };
        }).ToList();
        await _gateway.IngestImpactsBatchAsync(Guid.NewGuid(), impacts, null, null, default);
    }

    [Fact]
    public async Task Default_metric_is_the_affectation_level_with_five_classes()
    {
        await SeedAsync(60);

        var result = await Service().InterpolateAsync(
            new VisualizationRequest { EventId = FakeDataGateway.MocoaEventId }, default);

        Assert.Equal("level", result.Metric);
        Assert.Equal(5, result.Levels.Count);
        Assert.Equal("#d53745", result.Levels[^1].Color);
        Assert.Equal(5.0, result.Levels[^1].To);
    }

    [Fact]
    public async Task Level_five_is_not_flattened_to_severity_four()
    {
        await SeedAsync(80);

        var level = await Service().CompareAsync(
            new VisualizationRequest { EventId = FakeDataGateway.MocoaEventId, Metric = "level" }, default);
        var interpolation = await Service().InterpolateAsync(
            new VisualizationRequest { EventId = FakeDataGateway.MocoaEventId, Metric = "level" }, default);

        Assert.Equal(3, level.Methods.Count);
        Assert.Equal(AffectationLevelClassifier.FieldLevels, interpolation.Levels);
    }

    [Fact]
    public async Task Comparison_with_more_than_100_points_uses_a_sample_instead_of_failing()
    {
        await SeedAsync(150);

        var result = await Service().CompareAsync(
            new VisualizationRequest { EventId = FakeDataGateway.MocoaEventId }, default);

        Assert.Equal(100, result.SampleCount);
        Assert.Equal(3, result.Methods.Count);
    }

    [Fact]
    public async Task Unknown_metric_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().InterpolateAsync(
            new VisualizationRequest { EventId = FakeDataGateway.MocoaEventId, Metric = "otra" }, default));
    }
}
