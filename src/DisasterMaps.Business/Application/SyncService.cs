using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Validation;
using Microsoft.Extensions.Options;

namespace DisasterMaps.Business.Application;

/// <summary>
/// Sincronización de reportes capturados sin conexión (módulo móvil). Valida cada registro por separado,
/// envía los válidos al tier de Datos en una sola carga por lotes y arma un resultado por registro.
/// La identidad la define el dispositivo (clientId), así que reenviar el mismo lote es seguro.
/// </summary>
public sealed class SyncService(
    IDataGateway gateway,
    IOptionsMonitor<SyncOptions> options,
    TimeProvider clock,
    ILogger<SyncService> logger)
{
    public async Task<SyncReport> SyncAsync(SyncBatchRequest request, IngestStrategy? strategy, int? chunkSize, CancellationToken ct)
    {
        ValidateEnvelope(request, chunkSize, options.CurrentValue.MaxItems);
        var deviceId = request.DeviceId!.Trim();
        var now = clock.GetUtcNow();

        var results = new SyncItemResult?[request.Items!.Count];
        var toSend = new List<(int Index, ImpactDto Impact)>();
        var seen = new HashSet<Guid>();
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (!seen.Add(item.ClientId!.Value))
            {
                results[index] = new SyncItemResult(item.ClientId, SyncItemStatus.Duplicate, "El registro viene repetido en el mismo lote.", null);
                continue;
            }
            var errors = ValidateItem(item, now);
            if (!errors.IsValid)
            {
                results[index] = new SyncItemResult(item.ClientId, SyncItemStatus.Rejected, "El registro no cumple las reglas de negocio.", errors.ToDictionary());
                continue;
            }
            toSend.Add((index, ToImpact(item, deviceId)));
        }

        IngestResult? ingest = null;
        if (toSend.Count > 0)
        {
            ingest = await gateway.IngestImpactsBatchAsync(request.BatchId!.Value, toSend.Select(x => x.Impact).ToList(), strategy, chunkSize, ct);
            var byId = ingest.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var (index, impact) in toSend)
            {
                results[index] = byId.TryGetValue(impact.Id, out var r)
                    ? new SyncItemResult(impact.Id, r.Status, r.Reason, null)
                    : new SyncItemResult(impact.Id, SyncItemStatus.Rejected, "El tier de datos no reportó resultado para este registro.", null);
            }
        }

        var final = results.Select(r => r!).ToList();
        var report = new SyncReport(
            request.BatchId!.Value, deviceId, final.Count,
            final.Count(r => r.Status == SyncItemStatus.Inserted),
            final.Count(r => r.Status == SyncItemStatus.Duplicate),
            final.Count(r => r.Status == SyncItemStatus.Conflict),
            final.Count(r => r.Status == SyncItemStatus.Rejected),
            ingest?.Strategy, ingest?.Chunks ?? 0, ingest?.ElapsedMs ?? 0, clock.GetUtcNow(), final);

        logger.LogInformation(
            "Sincronización {BatchId} del dispositivo {DeviceId}: {Received} recibidos, {Inserted} insertados, {Duplicates} duplicados, {Conflicts} en conflicto, {Rejected} rechazados; carga {Strategy} en {ElapsedMs} ms",
            report.BatchId, deviceId, report.Received, report.Inserted, report.Duplicates, report.Conflicts, report.Rejected,
            report.Strategy, report.ElapsedMs);
        return report;
    }

    /// <summary>Errores de forma del lote: responden 400 completo, porque el dispositivo debe corregir su envío.</summary>
    private static void ValidateEnvelope(SyncBatchRequest r, int? chunkSize, int maxItems)
    {
        var errors = new ValidationErrors();
        if (r.BatchId is null || r.BatchId == Guid.Empty)
            errors.Add("batchId", "El identificador del lote es obligatorio.");
        if (string.IsNullOrWhiteSpace(r.DeviceId))
            errors.Add("deviceId", "El identificador del dispositivo es obligatorio.");
        else if (r.DeviceId.Trim().Length > SyncOptions.MaxDeviceIdLength)
            errors.Add("deviceId", $"El identificador del dispositivo no puede superar {SyncOptions.MaxDeviceIdLength} caracteres.");
        if (r.Items is null || r.Items.Count == 0)
            errors.Add("items", "El lote debe traer al menos un registro.");
        else
        {
            if (r.Items.Count > maxItems)
                errors.Add("items", $"El lote supera el máximo de {maxItems} registros.");
            for (var i = 0; i < r.Items.Count; i++)
            {
                if (r.Items[i] is null)
                    errors.Add($"items[{i}]", "El registro está vacío.");
                else if (r.Items[i].ClientId is null || r.Items[i].ClientId == Guid.Empty)
                    errors.Add($"items[{i}].clientId", "Cada registro debe traer el identificador generado en el dispositivo.");
            }
        }
        if (chunkSize is < 1 or > SyncOptions.MaxChunkSize)
            errors.Add("chunkSize", $"El tamaño de paquete debe estar entre 1 y {SyncOptions.MaxChunkSize}.");
        errors.ThrowIfInvalid();
    }

    /// <summary>RN-01, RN-02 (presencia), RN-03, RN-05 y el nivel 1..5 obligatorio.</summary>
    private static ValidationErrors ValidateItem(SyncItemRequest item, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        if (item.EventId is null || item.EventId == Guid.Empty)
            errors.Add("eventId", "El evento es obligatorio.");
        if (item.Latitude is null)
            errors.Add(ImpactRules.Fields.Latitude, "La latitud es obligatoria.");
        if (item.Longitude is null)
            errors.Add(ImpactRules.Fields.Longitude, "La longitud es obligatoria.");
        if (item.AffectationLevel is null)
            errors.Add("affectationLevel", "El nivel de afectación es obligatorio.");
        else if (!AffectationLevels.IsValid(item.AffectationLevel.Value))
            errors.Add("affectationLevel", "El nivel de afectación debe estar entre 1 y 5.");
        if (item.ReportedAt is null)
            errors.Add(ImpactRules.Fields.ReportedAt, "La hora de captura es obligatoria.");
        if (item.Latitude is { } lat && item.Longitude is { } lon)
            errors.AddRange(ImpactRules.Check(lat, lon, item.AffectedPeople ?? 0, item.ReportedAt, now));
        else if (item.AffectedPeople is < 0)
            errors.Add(ImpactRules.Fields.AffectedPeople, "Las personas afectadas no pueden ser negativas.");
        return errors;
    }

    private static ImpactDto ToImpact(SyncItemRequest item, string deviceId)
    {
        var level = item.AffectationLevel!.Value;
        return new ImpactDto(
            item.ClientId!.Value, item.EventId!.Value,
            ImpactRules.RoundCoordinate(item.Latitude!.Value), ImpactRules.RoundCoordinate(item.Longitude!.Value),
            AffectationLevels.ToSeverity(level), item.AffectedPeople ?? 0,
            Clean(item.Municipality), Clean(item.Department), Clean(item.Description),
            item.ReportedAt!.Value.ToUniversalTime(), ImpactSource.Mobile, StatusTransitions.Initial)
        {
            AffectationLevel = level,
            DeviceId = deviceId,
        };
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
