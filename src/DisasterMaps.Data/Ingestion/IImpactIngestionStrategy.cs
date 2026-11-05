using DisasterMaps.Contracts.Data.V1;

namespace DisasterMaps.Data.Ingestion;

/// <summary>Resultado de clasificar e insertar un registro del lote.</summary>
public sealed record IngestRow(string Id, IngestOutcome Outcome, string Reason = "");

/// <summary>
/// Estrategia de carga de un paquete de afectaciones ya validadas en forma.
/// Cada llamada es una transacción: o se aplica completo el paquete o no se aplica nada.
/// </summary>
public interface IImpactIngestionStrategy
{
    IngestStrategy Kind { get; }
    Task<IReadOnlyList<IngestRow>> IngestChunkAsync(IReadOnlyList<Impact> chunk, CancellationToken ct);
}
