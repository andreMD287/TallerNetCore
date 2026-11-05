using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.GeoJson;

public sealed record PointGeometry(double[] Coordinates)
{
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public string Type => "Point";
}

public sealed record FeatureProperties(
    Guid Id, Guid EventId, Severity Severity, int AffectedPeople, string? Municipality, string? Department,
    string? Description, DateTimeOffset ReportedAt, ImpactSource Source, ImpactStatus Status,
    int AffectationLevel, DateTimeOffset? ReceivedAt);

public sealed record Feature(PointGeometry Geometry, FeatureProperties Properties)
{
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public string Type => "Feature";
}

public sealed record FeatureCollection(IReadOnlyList<Feature> Features)
{
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public string Type => "FeatureCollection";
}

/// <summary>Arma el FeatureCollection del mapa. GeoJSON (RFC 7946) usa [longitud, latitud].</summary>
public static class FeatureCollectionBuilder
{
    public static FeatureCollection Build(IEnumerable<ImpactDto> impacts) =>
        new(impacts.Select(ToFeature).ToList());

    public static Feature ToFeature(ImpactDto i) => new(
        new PointGeometry([i.Longitude, i.Latitude]),
        new FeatureProperties(i.Id, i.EventId, i.Severity, i.AffectedPeople, i.Municipality, i.Department,
            i.Description, i.ReportedAt, i.Source, i.Status, i.AffectationLevel, i.ReceivedAt));
}
