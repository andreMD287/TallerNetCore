namespace DisasterMaps.Business.Domain;

/// <summary>
/// Nivel de afectación 1..5 que se captura en campo. Convive con la severidad de 4 valores:
/// 1 → LOW, 2 → MEDIUM, 3 → HIGH, 4 y 5 → CRITICAL. Los registros que solo traen severidad
/// se llevan a nivel con LOW = 1, MEDIUM = 2, HIGH = 3 y CRITICAL = 4.
/// </summary>
public static class AffectationLevels
{
    public const int Min = 1;
    public const int Max = 5;

    public static bool IsValid(int level) => level is >= Min and <= Max;

    public static Severity ToSeverity(int level) => level switch
    {
        <= 1 => Severity.Low,
        2 => Severity.Medium,
        3 => Severity.High,
        _ => Severity.Critical,
    };

    public static int FromSeverity(Severity severity) => Math.Clamp((int)severity, Min, 4);
}
