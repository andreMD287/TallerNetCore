using System.Text.Json;
using DisasterMaps.Business.Visualization;
using Xunit;

namespace DisasterMaps.Business.Tests;

public sealed class VisualizationSerializationTests
{
    [Fact]
    public void PeopleLevels_CanBeSerializedAsJson()
    {
        var levels = AffectationLevelClassifier.PeopleLevels(
            0, 50, 500);

        string json = JsonSerializer.Serialize(levels);

        Assert.Contains("CRITICAL", json);
        Assert.DoesNotContain("Infinity", json);
    }

    [Fact]
    public void PeopleLevels_HaveFiniteLimits()
    {
        var levels = AffectationLevelClassifier.PeopleLevels(
            0, 50, 500);

        Assert.All(levels, level =>
        {
            Assert.True(double.IsFinite(level.From));
            Assert.True(double.IsFinite(level.To));
        });
    }
}