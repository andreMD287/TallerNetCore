using System.Diagnostics;
using DisasterMaps.Contracts.Data.V1;
using DisasterMaps.Data.Services;
using Microsoft.Extensions.Options;

namespace DisasterMaps.Data.Ingestion;

/// <summary>
/// Carga por lotes: valida cada registro, parte el lote en paquetes de <c>ChunkSize</c> y delega cada paquete
/// a la estrategia elegida (DAL o DATABASE). Un paquete fallido no deshace los anteriores.
/// </summary>
public sealed class ImpactIngestor(
    IEnumerable<IImpactIngestionStrategy> strategies,
    IOptionsMonitor<IngestionOptions> options,
    ILogger<ImpactIngestor> logger)
{
    public async Task<IngestBatchReply> IngestAsync(IngestBatchRequest request, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var kind = request.Strategy != IngestStrategy.Unspecified ? request.Strategy
            : !settings.AutoSelect ? settings.DefaultStrategy
            : request.Impacts.Count >= settings.AutoThreshold ? IngestStrategy.Database : IngestStrategy.Dal;
        var strategy = strategies.FirstOrDefault(s => s.Kind == kind)
            ?? throw new InvalidOperationException($"Estrategia de carga no registrada: {kind}");
        var chunkSize = Math.Clamp(request.ChunkSize > 0 ? request.ChunkSize : settings.ChunkSize, 1, IngestionOptions.MaxChunkSize);

        var watch = Stopwatch.StartNew();
        var reply = new IngestBatchReply { Strategy = kind };
        var valid = new List<Impact>(request.Impacts.Count);
        foreach (var impact in request.Impacts)
        {
            var error = ImpactValidation.Check(impact);
            if (error is null) valid.Add(impact);
            else reply.Results.Add(new IngestItemResult { Id = impact?.Id ?? "", Outcome = IngestOutcome.Rejected, Reason = error });
        }

        foreach (var chunk in valid.Chunk(chunkSize))
        {
            foreach (var row in await strategy.IngestChunkAsync(chunk, ct))
                reply.Results.Add(new IngestItemResult { Id = row.Id, Outcome = row.Outcome, Reason = row.Reason });
            reply.Chunks++;
        }
        reply.ElapsedMs = watch.ElapsedMilliseconds;

        logger.LogInformation(
            "Carga por lotes {Batch}: estrategia {Strategy}, {Count} registros, {Chunks} paquetes de {ChunkSize}, {Inserted} insertados, {ElapsedMs} ms",
            request.BatchId, kind, request.Impacts.Count, reply.Chunks, chunkSize,
            reply.Results.Count(r => r.Outcome == IngestOutcome.Inserted), reply.ElapsedMs);
        return reply;
    }
}
