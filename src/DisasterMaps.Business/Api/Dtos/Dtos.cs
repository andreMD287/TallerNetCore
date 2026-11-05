using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.Api.Dtos;

// Contrato REST v1 (contracts/rest/api-v1.md). JSON en camelCase, enums como string, fechas ISO-8601 UTC.

public sealed record EventDto(Guid Id, string Name, EventType Type, DateTimeOffset StartedAt, string? Description);

public sealed record CreateEventRequest(string? Name, EventType? Type, DateTimeOffset? StartedAt, string? Description);

public sealed record ImpactDto(
    Guid Id, Guid EventId, double Latitude, double Longitude, Severity Severity, int AffectedPeople,
    string? Municipality, string? Department, string? Description, DateTimeOffset ReportedAt,
    ImpactSource Source, ImpactStatus Status)
{
    /// <summary>Nivel 1..5. Si no se asigna, se deriva de la severidad (LOW=1 … CRITICAL=4).</summary>
    public int AffectationLevel { get; init; } = AffectationLevels.FromSeverity(Severity);

    /// <summary>Dispositivo que capturó el reporte (solo reportes MOBILE).</summary>
    public string? DeviceId { get; init; }

    /// <summary>Cuándo lo incorporó la plataforma (T_sys). reportedAt es cuándo ocurrió (T_eff).</summary>
    public DateTimeOffset? ReceivedAt { get; init; }
}

public record CreateImpactRequest(
    Guid? EventId, double? Latitude, double? Longitude, Severity? Severity, int? AffectedPeople,
    string? Municipality, string? Department, string? Description, DateTimeOffset? ReportedAt,
    ImpactSource? Source)
{
    /// <summary>Nivel de afectación 1..5 (opcional). Si viene sin severidad, la severidad se deriva de él.</summary>
    public int? AffectationLevel { get; init; }
}

public sealed record UpdateImpactRequest(
    Guid? EventId, double? Latitude, double? Longitude, Severity? Severity, int? AffectedPeople,
    string? Municipality, string? Department, string? Description, DateTimeOffset? ReportedAt,
    ImpactSource? Source, ImpactStatus? Status)
    : CreateImpactRequest(EventId, Latitude, Longitude, Severity, AffectedPeople, Municipality, Department, Description, ReportedAt, Source);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ImportError(int Line, string Reason);

public sealed record ImportReport(int Received, int Accepted, int Rejected, IReadOnlyList<ImportError> Errors);

public sealed record MunicipalitySummaryDto(string Municipality, string Department, int Impacts, int AffectedPeople, Severity MaxSeverity);

/// <summary>Filtros de GET /impacts y /impacts/geojson (query string).</summary>
public sealed class ImpactQuery
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 2000;

    public Guid? EventId { get; set; }
    public EventType? EventType { get; set; }
    public Severity? MinSeverity { get; set; }
    public double? MinLat { get; set; }
    public double? MaxLat { get; set; }
    public double? MinLon { get; set; }
    public double? MaxLon { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public int EffectivePage => Page is > 0 ? Page.Value : 1;
    public int EffectivePageSize => PageSize is > 0 ? Math.Min(PageSize.Value, MaxPageSize) : DefaultPageSize;
    public bool HasBoundingBox => MinLat.HasValue || MaxLat.HasValue || MinLon.HasValue || MaxLon.HasValue;
}
