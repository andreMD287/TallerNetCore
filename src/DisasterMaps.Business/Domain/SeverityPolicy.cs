using Microsoft.Extensions.Options;

namespace DisasterMaps.Business.Domain;

/// <summary>Umbrales de RN-04 (sección "Severity" de appsettings). Se recargan sin recompilar.</summary>
public sealed class SeverityThresholds
{
    public const string Section = "Severity";

    /// <summary>Desde cuántas personas afectadas la severidad es MEDIUM.</summary>
    public int Medium { get; set; } = 1;
    public int High { get; set; } = 50;
    public int Critical { get; set; } = 500;
}

/// <summary>RN-04: si la severidad viene se respeta; si no, se deriva de las personas afectadas.</summary>
public sealed class SeverityPolicy(IOptionsMonitor<SeverityThresholds> options)
{
    public Severity Resolve(Severity? requested, int affectedPeople) =>
        requested ?? Derive(affectedPeople);

    public Severity Derive(int affectedPeople)
    {
        var t = options.CurrentValue;
        if (affectedPeople >= t.Critical) return Severity.Critical;
        if (affectedPeople >= t.High) return Severity.High;
        if (affectedPeople >= t.Medium) return Severity.Medium;
        return Severity.Low;
    }
}
