using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.DataGateway;

/// <summary>Resultado de un registro en la carga por lotes.</summary>
public sealed record IngestItem(Guid Id, SyncItemStatus Status, string? Reason);

/// <summary>Resultado de IngestImpactsBatch: un elemento por registro, la estrategia usada y su costo.</summary>
public sealed record IngestResult(IReadOnlyList<IngestItem> Items, IngestStrategy Strategy, int Chunks, long ElapsedMs);
