using System.Text;
using DisasterMaps.Business.Errors;

namespace DisasterMaps.Business.Import;

/// <summary>
/// CSV UTF-8, separador ',', punto decimal y encabezado:
/// latitude,longitude,severity,affected_people,municipality,department,description,reported_at.
/// Soporta campos entre comillas dobles ("" como comilla escapada) dentro de una misma línea.
/// </summary>
public sealed class CsvImpactParser : IImpactFileParser
{
    public IReadOnlyCollection<string> Extensions { get; } = [".csv"];

    public IReadOnlyList<RawImpactRow> Parse(Stream content, int maxRows)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new BusinessValidationException("file", "El archivo CSV está vacío.");

        var header = SplitLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => header.IndexOf(name);
        if (Col(ImportColumns.Latitude) < 0 || Col(ImportColumns.Longitude) < 0)
            throw new BusinessValidationException("file", "El encabezado del CSV debe incluir las columnas latitude y longitude.");

        var rows = new List<RawImpactRow>();
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (rows.Count >= maxRows)
                throw new BusinessValidationException("file", $"El archivo supera el máximo de {maxRows:N0} filas.");

            var fields = SplitLine(line);
            if (fields.Count != header.Count)
            {
                rows.Add(new RawImpactRow(lineNumber, Error: $"La fila tiene {fields.Count} columnas y el encabezado {header.Count}."));
                continue;
            }

            string? Get(string name) => Col(name) is var i and >= 0 ? NullIfBlank(fields[i]) : null;
            rows.Add(new RawImpactRow(
                lineNumber,
                Get(ImportColumns.Latitude), Get(ImportColumns.Longitude), Get(ImportColumns.Severity),
                Get(ImportColumns.AffectedPeople), Get(ImportColumns.Municipality), Get(ImportColumns.Department),
                Get(ImportColumns.Description), Get(ImportColumns.ReportedAt)));
        }
        return rows;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Divide una línea CSV respetando comillas dobles.</summary>
    internal static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
