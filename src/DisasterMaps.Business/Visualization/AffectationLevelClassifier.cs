namespace DisasterMaps.Business.Visualization;

public sealed record AffectationLevel(
    string Level, double From, double To, string Color);

public static class AffectationLevelClassifier
{
    public static readonly IReadOnlyList<AffectationLevel>
        SeverityLevels =
        [
            new("LOW", 1.0, 1.5, "#38a169"),
            new("MEDIUM", 1.5, 2.5, "#e5b135"),
            new("HIGH", 2.5, 3.5, "#f07835"),
            new("CRITICAL", 3.5, 4.0, "#d53745")
        ];

    public static string ClassifySeverity(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));

        return value < 1.5 ? "LOW"
            : value < 2.5 ? "MEDIUM"
            : value < 3.5 ? "HIGH"
            : "CRITICAL";
    }

    public static double ToPeopleScale(int affectedPeople)
    {
        if (affectedPeople < 0)
            throw new ArgumentOutOfRangeException(nameof(affectedPeople));

        return Math.Log10(1.0 + affectedPeople);
    }

    public static double FromPeopleScale(double value) =>
        Math.Max(0, Math.Pow(10, value) - 1);

    public static IReadOnlyList<AffectationLevel>
        PeopleLevels(int lowToMedium, int mediumToHigh, int highToCritical)
    {
        if (lowToMedium < 0 ||
            mediumToHigh <= lowToMedium ||
            highToCritical <= mediumToHigh)
            throw new ArgumentException("Umbrales invalidos.");

        return
        [
            new("LOW", 0, ToPeopleScale(lowToMedium), "#38a169"),
            new("MEDIUM",
                ToPeopleScale(lowToMedium),
                ToPeopleScale(mediumToHigh), "#e5b135"),
            new("HIGH",
                ToPeopleScale(mediumToHigh),
                ToPeopleScale(highToCritical), "#f07835"),
            new("CRITICAL",
                ToPeopleScale(highToCritical),
                double.PositiveInfinity, "#d53745")
        ];
    }
}