using System.Globalization;
using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.Import;

/// <summary>Convierte una fila cruda en afectación aplicando RN-01, RN-03, RN-04, RN-05 y RN-09 (source = FILE).</summary>
public sealed class ImportRowConverter(SeverityPolicy severityPolicy)
{
    public (ImpactDto? Impact, string? Reason) Convert(RawImpactRow row, Guid eventId, DateTimeOffset now)
    {
        if (row.Error is not null) return (null, row.Error);

        var reasons = new List<string>();

        var lat = ParseDouble(row.Latitude, "latitud", reasons);
        var lon = ParseDouble(row.Longitude, "longitud", reasons);

        Severity? severity = null;
        if (row.Severity is { } sevText)
        {
            if (!int.TryParse(sevText, out _) && Enum.TryParse<Severity>(sevText, ignoreCase: true, out var s) && Enum.IsDefined(s))
                severity = s;
            else
                reasons.Add($"Severidad '{sevText}' no válida (LOW, MEDIUM, HIGH o CRITICAL).");
        }

        var affected = 0;
        if (row.AffectedPeople is { } apText && !int.TryParse(apText, NumberStyles.Integer, CultureInfo.InvariantCulture, out affected))
            reasons.Add($"Personas afectadas '{apText}' no es un entero.");

        DateTimeOffset? reportedAt = null;
        if (row.ReportedAt is { } dateText)
        {
            if (DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                reportedAt = d.ToUniversalTime();
            else
                reasons.Add($"Fecha '{dateText}' no válida (use ISO-8601, p. ej. 2026-11-03T15:20:00Z).");
        }

        if (lat is { } la && lon is { } lo)
            reasons.AddRange(ImpactRules.Check(la, lo, affected, reportedAt, now).Select(e => e.Message));

        if (reasons.Count > 0 || lat is null || lon is null)
            return (null, string.Join(" ", reasons));

        var impact = new ImpactDto(
            Guid.CreateVersion7(),
            eventId,
            ImpactRules.RoundCoordinate(lat.Value),
            ImpactRules.RoundCoordinate(lon.Value),
            severityPolicy.Resolve(severity, affected),
            affected,
            row.Municipality, row.Department, row.Description,
            reportedAt ?? now,
            ImpactSource.File,
            StatusTransitions.Initial);
        return (impact, null);
    }

    private static double? ParseDouble(string? text, string name, List<string> reasons)
    {
        if (text is null) { reasons.Add($"La {name} es obligatoria."); return null; }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) return v;
        reasons.Add($"La {name} '{text}' no es un número válido (use punto decimal).");
        return null;
    }
}
