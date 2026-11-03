using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.Tests;

public class SeverityPolicyTests
{
    [Theory] // RN-04: 0 → LOW; 1–49 → MEDIUM; 50–499 → HIGH; ≥ 500 → CRITICAL
    [InlineData(0, Severity.Low)]
    [InlineData(1, Severity.Medium)]
    [InlineData(49, Severity.Medium)]
    [InlineData(50, Severity.High)]
    [InlineData(499, Severity.High)]
    [InlineData(500, Severity.Critical)]
    [InlineData(100_000, Severity.Critical)]
    public void Derives_severity_from_affected_people(int people, Severity expected) =>
        Assert.Equal(expected, Samples.DefaultSeverityPolicy().Resolve(null, people));

    [Fact]
    public void Respects_requested_severity() =>
        Assert.Equal(Severity.Low, Samples.DefaultSeverityPolicy().Resolve(Severity.Low, 10_000));

    [Fact] // Modificabilidad: cambiar umbrales en appsettings sin recompilar
    public void Uses_current_thresholds_when_configuration_changes()
    {
        var monitor = new TestOptionsMonitor<SeverityThresholds>(new SeverityThresholds());
        var policy = new SeverityPolicy(monitor);
        Assert.Equal(Severity.High, policy.Derive(100));

        monitor.CurrentValue = new SeverityThresholds { Medium = 1, High = 200, Critical = 1000 };
        Assert.Equal(Severity.Medium, policy.Derive(100));
    }
}

public class StatusTransitionsTests
{
    [Theory] // RN-10: REPORTED → VERIFIED → ATTENDED, paso a paso, sin retroceder
    [InlineData(ImpactStatus.Reported, ImpactStatus.Reported, true)]
    [InlineData(ImpactStatus.Reported, ImpactStatus.Verified, true)]
    [InlineData(ImpactStatus.Verified, ImpactStatus.Attended, true)]
    [InlineData(ImpactStatus.Attended, ImpactStatus.Attended, true)]
    [InlineData(ImpactStatus.Reported, ImpactStatus.Attended, false)]
    [InlineData(ImpactStatus.Verified, ImpactStatus.Reported, false)]
    [InlineData(ImpactStatus.Attended, ImpactStatus.Verified, false)]
    [InlineData(ImpactStatus.Attended, ImpactStatus.Reported, false)]
    public void Only_allows_forward_single_steps(ImpactStatus from, ImpactStatus to, bool allowed) =>
        Assert.Equal(allowed, StatusTransitions.IsAllowed(from, to));

    [Fact]
    public void Initial_status_is_reported() => Assert.Equal(ImpactStatus.Reported, StatusTransitions.Initial);
}

public class ImpactRulesTests
{
    [Theory] // RN-01: redondeo a 6 decimales
    [InlineData(1.15221234567, 1.152212)]
    [InlineData(-76.6466665, -76.646667)]
    [InlineData(4.5, 4.5)]
    public void Rounds_coordinates_to_six_decimals(double input, double expected) =>
        Assert.Equal(expected, ImpactRules.RoundCoordinate(input));
}
