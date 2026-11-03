namespace DisasterMaps.Business.Domain;

/// <summary>Reglas de campo de una afectación (RN-01, RN-03, RN-05) compartidas por la API y la importación.</summary>
public static class ImpactRules
{
    public const int CoordinateDecimals = 6;
    public const int DuplicateKeyDecimals = 5;
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static class Fields
    {
        public const string Latitude = "latitude";
        public const string Longitude = "longitude";
        public const string AffectedPeople = "affectedPeople";
        public const string ReportedAt = "reportedAt";
    }

    /// <summary>Devuelve los errores (campo, mensaje) de las reglas de campo. Lista vacía = válido.</summary>
    public static List<(string Field, string Message)> Check(double latitude, double longitude, int affectedPeople, DateTimeOffset? reportedAt, DateTimeOffset now)
    {
        var errors = new List<(string, string)>();
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            errors.Add((Fields.Latitude, "La latitud debe estar en el rango [-90, 90]."));
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            errors.Add((Fields.Longitude, "La longitud debe estar en el rango [-180, 180]."));
        if (latitude == 0 && longitude == 0)
            errors.Add((Fields.Latitude, "La coordenada (0, 0) no es válida: suele indicar una coordenada vacía."));
        if (affectedPeople < 0)
            errors.Add((Fields.AffectedPeople, "Las personas afectadas no pueden ser negativas."));
        if (reportedAt is { } r && r > now + FutureTolerance)
            errors.Add((Fields.ReportedAt, "La fecha de reporte no puede estar en el futuro."));
        return errors;
    }

    /// <summary>RN-01: se redondea a 6 decimales (~0,1 m).</summary>
    public static double RoundCoordinate(double value) =>
        Math.Round(value, CoordinateDecimals, MidpointRounding.AwayFromZero);
}
