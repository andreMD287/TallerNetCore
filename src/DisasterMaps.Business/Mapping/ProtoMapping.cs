using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;
using Google.Protobuf.WellKnownTypes;
using Proto = DisasterMaps.Contracts.Data.V1;

namespace DisasterMaps.Business.Mapping;

/// <summary>Traducción DTO ↔ mensajes del .proto. Los enums comparten valor numérico.</summary>
public static class ProtoMapping
{
    // ---- enums ----
    public static Proto.EventType ToProto(this EventType v) => (Proto.EventType)(int)v;
    public static Proto.Severity ToProto(this Severity v) => (Proto.Severity)(int)v;
    public static Proto.ImpactSource ToProto(this ImpactSource v) => (Proto.ImpactSource)(int)v;
    public static Proto.ImpactStatus ToProto(this ImpactStatus v) => (Proto.ImpactStatus)(int)v;

    public static EventType ToDomain(this Proto.EventType v) => v == Proto.EventType.Unspecified ? EventType.Other : (EventType)(int)v;
    public static Severity ToDomain(this Proto.Severity v) => v == Proto.Severity.Unspecified ? Severity.Low : (Severity)(int)v;
    public static ImpactSource ToDomain(this Proto.ImpactSource v) => v == Proto.ImpactSource.Unspecified ? ImpactSource.Manual : (ImpactSource)(int)v;
    public static ImpactStatus ToDomain(this Proto.ImpactStatus v) => v == Proto.ImpactStatus.Unspecified ? ImpactStatus.Reported : (ImpactStatus)(int)v;

    // ---- eventos ----
    public static Proto.DisasterEvent ToProto(this EventDto e) => new()
    {
        Id = e.Id.ToString(),
        Name = e.Name,
        Type = e.Type.ToProto(),
        StartedAt = Timestamp.FromDateTimeOffset(e.StartedAt),
        Description = e.Description ?? string.Empty,
    };

    public static EventDto ToDto(this Proto.DisasterEvent e) => new(
        Guid.Parse(e.Id), e.Name, e.Type.ToDomain(),
        e.StartedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue, NullIfEmpty(e.Description));

    // ---- afectaciones ----
    public static Proto.Impact ToProto(this ImpactDto i) => new()
    {
        Id = i.Id.ToString(),
        EventId = i.EventId.ToString(),
        Latitude = i.Latitude,
        Longitude = i.Longitude,
        Severity = i.Severity.ToProto(),
        AffectedPeople = i.AffectedPeople,
        Municipality = i.Municipality ?? string.Empty,
        Department = i.Department ?? string.Empty,
        Description = i.Description ?? string.Empty,
        ReportedAt = Timestamp.FromDateTimeOffset(i.ReportedAt),
        Source = i.Source.ToProto(),
        Status = i.Status.ToProto(),
    };

    public static ImpactDto ToDto(this Proto.Impact i) => new(
        Guid.Parse(i.Id), Guid.Parse(i.EventId), i.Latitude, i.Longitude, i.Severity.ToDomain(), i.AffectedPeople,
        NullIfEmpty(i.Municipality), NullIfEmpty(i.Department), NullIfEmpty(i.Description),
        i.ReportedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue, i.Source.ToDomain(), i.Status.ToDomain());

    public static MunicipalitySummaryDto ToDto(this Proto.MunicipalitySummary s) =>
        new(s.Municipality, s.Department, s.Impacts, s.AffectedPeople, s.MaxSeverity.ToDomain());

    // ---- filtro ----
    public static Proto.ImpactFilter ToProto(this ImpactQuery q, int page, int pageSize)
    {
        var f = new Proto.ImpactFilter
        {
            EventId = q.EventId?.ToString() ?? string.Empty,
            EventType = q.EventType?.ToProto() ?? Proto.EventType.Unspecified,
            MinSeverity = q.MinSeverity?.ToProto() ?? Proto.Severity.Unspecified,
            MinLat = q.MinLat,
            MaxLat = q.MaxLat,
            MinLon = q.MinLon,
            MaxLon = q.MaxLon,
            Page = page,
            PageSize = pageSize,
        };
        if (q.From is { } from) f.From = Timestamp.FromDateTimeOffset(from);
        if (q.To is { } to) f.To = Timestamp.FromDateTimeOffset(to);
        return f;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
