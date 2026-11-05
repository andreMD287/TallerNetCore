using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.Api.Dtos;

// POST /api/v1/sync/batch: reportes capturados sin conexión en la app móvil.

/// <summary>Lote enviado por un dispositivo al recuperar la conexión.</summary>
public sealed record SyncBatchRequest(Guid? BatchId, string? DeviceId, IReadOnlyList<SyncItemRequest>? Items);

/// <summary>Reporte capturado en campo. <c>clientId</c> lo genera el dispositivo y es el id definitivo.</summary>
public sealed record SyncItemRequest(
    Guid? ClientId, Guid? EventId, double? Latitude, double? Longitude, int? AffectationLevel,
    int? AffectedPeople, string? Municipality, string? Department, string? Description, DateTimeOffset? ReportedAt);

/// <summary>
/// Resultado por registro. INSERTED y DUPLICATE significan que el dato ya está en el sistema central
/// (el dispositivo lo marca como sincronizado). CONFLICT y REJECTED requieren revisión y no se reintentan.
/// </summary>
public sealed record SyncItemResult(
    Guid? ClientId, SyncItemStatus Status, string? Reason, IDictionary<string, string[]>? Errors);

public sealed record SyncReport(
    Guid BatchId, string DeviceId, int Received, int Inserted, int Duplicates, int Conflicts, int Rejected,
    IngestStrategy? Strategy, int Chunks, long ElapsedMs, DateTimeOffset ServerTime,
    IReadOnlyList<SyncItemResult> Results);
