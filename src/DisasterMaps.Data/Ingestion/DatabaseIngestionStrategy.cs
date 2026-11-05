using DisasterMaps.Contracts.Data.V1;
using DisasterMaps.Data.Repositories;
using Npgsql;

namespace DisasterMaps.Data.Ingestion;

/// <summary>
/// Estrategia DATABASE: la aplicación solo transporta. Copia el paquete crudo a <c>impacts_staging</c> con COPY
/// y la función <c>ingest_impacts_from_staging</c> clasifica, inserta y limpia en una sola transacción.
/// </summary>
public sealed class DatabaseIngestionStrategy(NpgsqlDataSource dataSource) : IImpactIngestionStrategy
{
    public IngestStrategy Kind => IngestStrategy.Database;

    public async Task<IReadOnlyList<IngestRow>> IngestChunkAsync(IReadOnlyList<Impact> chunk, CancellationToken ct)
    {
        var batch = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await ImpactBulkWriter.CopyAsync(connection, chunk, batch, ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT r_id, r_outcome, r_reason FROM ingest_impacts_from_staging(@batch)", connection, tx);
        cmd.Parameters.AddWithValue("batch", batch);
        var results = new List<IngestRow>(chunk.Count);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var outcome = reader.GetString(1) switch
                {
                    "INSERTED" => IngestOutcome.Inserted,
                    "DUPLICATE" => IngestOutcome.Duplicate,
                    "CONFLICT" => IngestOutcome.Conflict,
                    _ => IngestOutcome.Rejected,
                };
                results.Add(new IngestRow(reader.GetGuid(0).ToString(), outcome, reader.GetString(2)));
            }
        }
        await tx.CommitAsync(ct);
        return results;
    }
}
