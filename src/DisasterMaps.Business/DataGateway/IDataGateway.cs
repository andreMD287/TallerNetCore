using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.DataGateway;

/// <summary>
/// Fachada hacia el tier de Datos (equivalente a la fachada de bajo acoplamiento del EJB).
/// Los servicios de aplicación nunca usan el cliente gRPC directamente.
/// Los errores llegan como <see cref="Grpc.Core.RpcException"/> según la tabla de la sección 3.1 del contrato.
/// </summary>
public interface IDataGateway
{
    Task<IReadOnlyList<EventDto>> ListEventsAsync(CancellationToken ct);
    Task<EventDto> CreateEventAsync(EventDto evt, CancellationToken ct);

    Task<ImpactDto> CreateImpactAsync(ImpactDto impact, CancellationToken ct);
    /// <summary>Lote atómico (todo o nada). Sin reintento automático.</summary>
    Task<int> CreateImpactsBatchAsync(IReadOnlyList<ImpactDto> impacts, CancellationToken ct);
    /// <summary>
    /// Carga por lotes de la app móvil. No es atómica: devuelve un resultado por registro y nunca sobrescribe.
    /// Idempotente, porque el id lo genera el dispositivo (un reenvío vuelve como DUPLICATE).
    /// </summary>
    Task<IngestResult> IngestImpactsBatchAsync(Guid batchId, IReadOnlyList<ImpactDto> impacts, IngestStrategy? strategy, int? chunkSize, CancellationToken ct);
    Task<ImpactDto> GetImpactAsync(Guid id, CancellationToken ct);
    Task<PagedResult<ImpactDto>> ListImpactsAsync(ImpactQuery query, CancellationToken ct);
    Task<ImpactDto> UpdateImpactAsync(ImpactDto impact, CancellationToken ct);
    Task<bool> DeleteImpactAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MunicipalitySummaryDto>> SummaryByMunicipalityAsync(ImpactQuery query, CancellationToken ct);
}
