using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Errors;
using DisasterMaps.Business.Import;

namespace DisasterMaps.Business.Application;

/// <summary>Resultado de una importación: el reporte y si se insertó algo (si no, la API responde 400).</summary>
public sealed record ImportOutcome(ImportReport Report, bool Accepted);

/// <summary>
/// "Leer coordenadas" (RN-06, RN-07): parser por extensión, validación fila por fila con número de línea
/// y UN solo CreateImpactsBatch atómico con las filas válidas. Sin estado (scoped).
/// </summary>
public sealed class ImportService(
    IEnumerable<IImpactFileParser> parsers,
    ImportRowConverter converter,
    IDataGateway gateway,
    TimeProvider clock,
    ILogger<ImportService> logger)
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRows = 10_000;

    public async Task<ImportOutcome> ImportAsync(Stream content, string fileName, long length, Guid? eventId, CancellationToken ct)
    {
        var errors = new Validation.ValidationErrors();
        if (eventId is null || eventId == Guid.Empty)
            errors.Add("eventId", "El evento es obligatorio.");
        if (length <= 0)
            errors.Add("file", "El archivo está vacío.");
        else if (length > MaxFileBytes)
            errors.Add("file", "El archivo supera el tamaño máximo de 5 MB.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var parser = parsers.FirstOrDefault(p => p.Extensions.Contains(extension));
        if (parser is null)
            errors.Add("file", "Formato no soportado. Use un archivo .csv, .geojson o .json.");
        errors.ThrowIfInvalid();

        var started = clock.GetTimestamp();
        var rows = parser!.Parse(content, MaxRows);
        var now = clock.GetUtcNow();

        var accepted = new List<ImpactDto>(rows.Count);
        var rejected = new List<ImportError>();
        var seen = new Dictionary<(double, double, DateTimeOffset), int>();

        foreach (var row in rows)
        {
            var (impact, reason) = converter.Convert(row, eventId!.Value, now);
            if (impact is null)
            {
                rejected.Add(new ImportError(row.Line, reason!));
                continue;
            }

            // RN-07: mismas coordenadas (5 decimales) y misma fecha dentro del archivo => duplicada.
            var key = (Math.Round(impact.Latitude, ImpactRules.DuplicateKeyDecimals), Math.Round(impact.Longitude, ImpactRules.DuplicateKeyDecimals), impact.ReportedAt);
            if (seen.TryGetValue(key, out var firstLine))
            {
                rejected.Add(new ImportError(row.Line, $"Fila duplicada de la línea {firstLine} (mismas coordenadas y fecha)."));
                continue;
            }
            seen[key] = row.Line;
            accepted.Add(impact);
        }

        var report = new ImportReport(rows.Count, accepted.Count, rejected.Count, rejected.OrderBy(e => e.Line).ToList());
        if (accepted.Count == 0)
            return new ImportOutcome(report, false);

        await gateway.CreateImpactsBatchAsync(accepted, ct);
        logger.LogInformation("Importación {File}: {Accepted} aceptadas, {Rejected} rechazadas en {ElapsedMs} ms",
            fileName, accepted.Count, rejected.Count, (long)clock.GetElapsedTime(started).TotalMilliseconds);
        return new ImportOutcome(report, true);
    }
}
