namespace DisasterMaps.Business.Import;

/// <summary>
/// Fila leída de un archivo, todavía sin validar (valores como texto).
/// <c>Line</c>: línea física en CSV (el encabezado es la línea 1); número de feature (base 1) en GeoJSON.
/// <c>Error</c>: problema de formato de la fila (si lo hay, el resto de campos se ignora).
/// </summary>
public sealed record RawImpactRow(
    int Line,
    string? Latitude = null,
    string? Longitude = null,
    string? Severity = null,
    string? AffectedPeople = null,
    string? Municipality = null,
    string? Department = null,
    string? Description = null,
    string? ReportedAt = null,
    string? Error = null);

/// <summary>Lee un formato de archivo de afectaciones (sección 3.3 del contrato).</summary>
public interface IImpactFileParser
{
    /// <summary>Extensiones soportadas, en minúscula y con punto (".csv").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>
    /// Devuelve las filas del archivo. Lanza <see cref="Errors.BusinessValidationException"/> (campo "file")
    /// si el archivo completo es inválido o supera <paramref name="maxRows"/>.
    /// </summary>
    IReadOnlyList<RawImpactRow> Parse(Stream content, int maxRows);
}

public static class ImportColumns
{
    public const string Latitude = "latitude";
    public const string Longitude = "longitude";
    public const string Severity = "severity";
    public const string AffectedPeople = "affected_people";
    public const string Municipality = "municipality";
    public const string Department = "department";
    public const string Description = "description";
    public const string ReportedAt = "reported_at";
}
