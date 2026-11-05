using DisasterMaps.Contracts.Data.V1;
using DisasterMaps.Data.Persistence;
using DisasterMaps.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisasterMaps.Data.Ingestion;

/// <summary>
/// Estrategia DAL: la clasificación ocurre en la aplicación. Consulta con LINQ (EF Core) qué ids y eventos
/// ya existen, decide en C# y luego inserta solo los nuevos con COPY binario.
/// </summary>
public sealed class DalIngestionStrategy(DisasterMapsDbContext db, NpgsqlDataSource dataSource, ILogger<DalIngestionStrategy> logger)
    : IImpactIngestionStrategy
{
    public IngestStrategy Kind => IngestStrategy.Dal;

    public async Task<IReadOnlyList<IngestRow>> IngestChunkAsync(IReadOnlyList<Impact> chunk, CancellationToken ct)
    {
        try
        {
            return await TryIngestAsync(chunk, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // La lectura y el COPY no comparten transacción: si otra carga insertó alguno de estos ids
            // entre ambos pasos, el COPY falla completo y se reclasifica una vez.
            logger.LogWarning("Carga DAL: id insertado en paralelo, se reclasifica el paquete");
            return await TryIngestAsync(chunk, ct);
        }
    }

    private async Task<IReadOnlyList<IngestRow>> TryIngestAsync(IReadOnlyList<Impact> chunk, CancellationToken ct)
    {
        var distinct = chunk.GroupBy(i => i.Id).Select(g => g.First()).ToList();
        var ids = distinct.Select(i => Guid.Parse(i.Id)).ToList();
        var eventIds = distinct.Select(i => Guid.Parse(i.EventId)).Distinct().ToList();

        // Incluye los borrados lógicamente: un id ya usado nunca se reutiliza.
        var existing = await db.Impacts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.EventId, x.Latitude, x.Longitude, x.AffectedPeople, x.AffectationLevel, x.ReportedAt })
            .ToDictionaryAsync(x => x.Id, ct);
        var knownEvents = (await db.DisasterEvents.AsNoTracking()
            .Where(e => eventIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct)).ToHashSet();

        var results = new List<IngestRow>(distinct.Count);
        var toInsert = new List<Impact>();
        foreach (var impact in distinct)
        {
            var id = Guid.Parse(impact.Id);
            if (existing.TryGetValue(id, out var current))
            {
                var same = current.EventId == Guid.Parse(impact.EventId)
                    && current.Latitude.Equals(impact.Latitude) && current.Longitude.Equals(impact.Longitude)
                    && current.AffectedPeople == impact.AffectedPeople
                    && current.AffectationLevel == ImpactBulkWriter.LevelOf(impact)
                    && Math.Abs((current.ReportedAt - impact.ReportedAt.ToDateTime()).TotalSeconds) < 1;
                results.Add(same
                    ? new IngestRow(impact.Id, IngestOutcome.Duplicate)
                    : new IngestRow(impact.Id, IngestOutcome.Conflict, IngestReasons.Conflict));
            }
            else if (!knownEvents.Contains(Guid.Parse(impact.EventId)))
                results.Add(new IngestRow(impact.Id, IngestOutcome.Rejected, IngestReasons.MissingEvent));
            else
            {
                toInsert.Add(impact);
                results.Add(new IngestRow(impact.Id, IngestOutcome.Inserted));
            }
        }

        if (toInsert.Count > 0)
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await ImpactBulkWriter.CopyAsync(connection, toInsert, stagingBatch: null, ct);
            await tx.CommitAsync(ct);
        }
        return results;
    }
}
