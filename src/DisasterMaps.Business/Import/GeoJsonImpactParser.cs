using System.Text.Json;
using DisasterMaps.Business.Errors;

namespace DisasterMaps.Business.Import;

/// <summary>
/// GeoJSON: FeatureCollection de Point. OJO: coordinates = [longitud, latitud].
/// Las properties usan los mismos nombres que las columnas del CSV.
/// </summary>
public sealed class GeoJsonImpactParser : IImpactFileParser
{
    public IReadOnlyCollection<string> Extensions { get; } = [".geojson", ".json"];

    public IReadOnlyList<RawImpactRow> Parse(Stream content, int maxRows)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            throw new BusinessValidationException("file", "El archivo no es un JSON válido.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "FeatureCollection"
                || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                throw new BusinessValidationException("file", "El GeoJSON debe ser un FeatureCollection con un arreglo 'features'.");

            if (features.GetArrayLength() > maxRows)
                throw new BusinessValidationException("file", $"El archivo supera el máximo de {maxRows:N0} filas.");

            var rows = new List<RawImpactRow>(features.GetArrayLength());
            var line = 0;
            foreach (var feature in features.EnumerateArray())
            {
                line++;
                rows.Add(ParseFeature(feature, line));
            }
            return rows;
        }
    }

    private static RawImpactRow ParseFeature(JsonElement feature, int line)
    {
        if (feature.ValueKind != JsonValueKind.Object
            || !feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object
            || !geometry.TryGetProperty("type", out var gType) || gType.ValueKind != JsonValueKind.String || gType.GetString() != "Point")
            return new RawImpactRow(line, Error: "La geometría debe ser de tipo Point.");

        if (!geometry.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array
            || coords.GetArrayLength() < 2 || coords[0].ValueKind != JsonValueKind.Number || coords[1].ValueKind != JsonValueKind.Number)
            return new RawImpactRow(line, Error: "Las coordenadas deben ser [longitud, latitud] numéricas.");

        // GeoJSON (RFC 7946): primero longitud, luego latitud.
        var longitude = coords[0].GetRawText();
        var latitude = coords[1].GetRawText();

        feature.TryGetProperty("properties", out var props);
        string? Prop(string name)
        {
            if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
                _ => null,
            };
        }

        return new RawImpactRow(
            line, latitude, longitude,
            Prop(ImportColumns.Severity), Prop(ImportColumns.AffectedPeople), Prop(ImportColumns.Municipality),
            Prop(ImportColumns.Department), Prop(ImportColumns.Description), Prop(ImportColumns.ReportedAt));
    }
}
