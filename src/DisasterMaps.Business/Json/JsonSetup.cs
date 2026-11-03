using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisasterMaps.Business.Json;

/// <summary>JSON del contrato: camelCase, enums como string en MAYÚSCULAS, fechas ISO-8601 UTC con 'Z'.</summary>
public static class JsonSetup
{
    public static void Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        o.Converters.Add(new UtcDateTimeOffsetConverter());
    }
}

/// <summary>Escribe DateTimeOffset siempre en UTC con sufijo 'Z' (2026-11-03T15:20:00Z).</summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
}
