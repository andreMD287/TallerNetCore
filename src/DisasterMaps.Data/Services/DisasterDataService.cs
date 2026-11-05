using Enum = System.Enum;
using DisasterMaps.Contracts.Data.V1;
using DisasterMaps.Data.Entities;
using DisasterMaps.Data.Ingestion;
using DisasterMaps.Data.Persistence;
using DisasterMaps.Data.Repositories;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProtoEvent = DisasterMaps.Contracts.Data.V1.DisasterEvent;
using ProtoImpact = DisasterMaps.Contracts.Data.V1.Impact;

namespace DisasterMaps.Data.Services;

public sealed class DisasterDataService(
    DisasterMapsDbContext db, ImpactBulkWriter bulkWriter, ImpactIngestor ingestor, ILogger<DisasterDataService> logger)
    : DisasterData.DisasterDataBase
{
    private static RpcException Fail(StatusCode code, string message) => new(new Status(code, message));
    private static Guid ParseId(string text)
    {
        if (!Guid.TryParse(text, out var value)) throw Fail(StatusCode.InvalidArgument, "Identificador UUID inválido.");
        return value;
    }
    private static DateTime Date(Timestamp? timestamp) => timestamp?.ToDateTime() ?? DateTime.UtcNow;
    private static Timestamp Stamp(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DisasterEventEntity ToEntity(ProtoEvent e) => new()
    {
        Id = ParseId(e.Id), Name = e.Name, Type = e.Type.ToString().ToUpperInvariant(),
        StartedAt = Date(e.StartedAt), Description = e.Description
    };
    private static ImpactEntity ToEntity(ProtoImpact i) => new()
    {
        Id = ParseId(i.Id), EventId = ParseId(i.EventId),
        Latitude = i.Latitude, Longitude = i.Longitude,
        Severity = i.Severity.ToString().ToUpperInvariant(),
        AffectedPeople = i.AffectedPeople, Municipality = i.Municipality,
        Department = i.Department, Description = i.Description,
        ReportedAt = Date(i.ReportedAt), Source = i.Source.ToString().ToUpperInvariant(),
        Status = i.Status.ToString().ToUpperInvariant(),
        AffectationLevel = ImpactBulkWriter.LevelOf(i),
        DeviceId = string.IsNullOrEmpty(i.DeviceId) ? null : i.DeviceId
    };
    private static ProtoEvent ToProto(DisasterEventEntity e) => new()
    {
        Id = e.Id.ToString(), Name = e.Name,
        Type = Enum.Parse<EventType>(e.Type, true), StartedAt = Stamp(e.StartedAt),
        Description = e.Description
    };
    private static ProtoImpact ToProto(ImpactEntity i) => new()
    {
        Id = i.Id.ToString(), EventId = i.EventId.ToString(), Latitude = i.Latitude,
        Longitude = i.Longitude, Severity = Enum.Parse<Severity>(i.Severity, true),
        AffectedPeople = i.AffectedPeople, Municipality = i.Municipality,
        Department = i.Department, Description = i.Description, ReportedAt = Stamp(i.ReportedAt),
        Source = Enum.Parse<ImpactSource>(i.Source, true), Status = Enum.Parse<ImpactStatus>(i.Status, true),
        AffectationLevel = i.AffectationLevel, DeviceId = i.DeviceId ?? "", ReceivedAt = Stamp(i.ReceivedAt)
    };
    private static void Validate(ProtoImpact i)
    {
        if (ImpactValidation.Check(i) is { } error) throw Fail(StatusCode.InvalidArgument, error);
    }
    private static RpcException ConvertError(Exception ex)
    {
        if (ex is RpcException rpc) return rpc;
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        if (pg is not null)
        {
            return pg.SqlState switch
            {
                "23505" => Fail(StatusCode.AlreadyExists, "El identificador ya existe."),
                "23503" => Fail(StatusCode.FailedPrecondition, "El evento asociado no existe."),
                "23514" or "23502" or "22003" or "22001" => Fail(StatusCode.InvalidArgument, "Los datos incumplen las restricciones de la base de datos."),
                _ => Fail(StatusCode.Internal, "Error al consultar la base de datos.")
            };
        }
        if (ex is NpgsqlException or TimeoutException or OperationCanceledException)
            return Fail(StatusCode.Unavailable, "El servicio de base de datos no está disponible.");
        return Fail(StatusCode.Internal, "Ocurrió un error interno en el servicio de datos.");
    }
    private async Task<T> Execute<T>(ServerCallContext ctx, Func<Task<T>> action)
    {
        try
        {
            logger.LogInformation("gRPC {Method} trace={TraceId}", ctx.Method,
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.RequestHeaders.FirstOrDefault(x => x.Key == "traceparent")?.Value);
            return await action();
        }
        catch (Exception ex)
        {
            var rpc = ConvertError(ex);
            if (rpc.StatusCode == StatusCode.Internal)
                logger.LogError(ex, "Error gRPC interno en {Method}", ctx.Method);
            else logger.LogWarning("gRPC {Method}: {Status}", ctx.Method, rpc.StatusCode);
            throw rpc;
        }
    }
    private IQueryable<ImpactEntity> Query(ImpactFilter f)
    {
        IQueryable<ImpactEntity> q = db.Impacts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.EventId))
        {
            var id = ParseId(f.EventId);
            q = q.Where(x => x.EventId == id);
        }
        if ((int)f.EventType > 0)
        {
            var type = f.EventType.ToString().ToUpperInvariant();
            q = q.Where(x => x.Event.Type == type);
        }
        if ((int)f.MinSeverity > 0)
        {
            var accepted = Enumerable.Range((int)f.MinSeverity, 5 - (int)f.MinSeverity)
                .Select(x => ((Severity)x).ToString().ToUpperInvariant()).ToArray();
            q = q.Where(x => accepted.Contains(x.Severity));
        }
        if (f.MinLat.HasValue || f.MaxLat.HasValue || f.MinLon.HasValue || f.MaxLon.HasValue)
        {
            if (!f.MinLat.HasValue || !f.MaxLat.HasValue || !f.MinLon.HasValue || !f.MaxLon.HasValue
                || f.MinLat > f.MaxLat || f.MinLon > f.MaxLon)
                throw Fail(StatusCode.InvalidArgument, "La caja geográfica requiere cuatro límites válidos.");
            var minLat = f.MinLat.Value; var maxLat = f.MaxLat.Value;
            var minLon = f.MinLon.Value; var maxLon = f.MaxLon.Value;
            q = q.Where(x => x.Latitude >= minLat && x.Latitude <= maxLat
                          && x.Longitude >= minLon && x.Longitude <= maxLon);
        }
        if (f.From is not null) { var from = f.From.ToDateTime(); q = q.Where(x => x.ReportedAt >= from); }
        if (f.To is not null) { var to = f.To.ToDateTime(); q = q.Where(x => x.ReportedAt <= to); }
        return q;
    }

    public override Task<EventReply> CreateEvent(CreateEventRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        if (request.Event is null || string.IsNullOrWhiteSpace(request.Event.Name))
            throw Fail(StatusCode.InvalidArgument, "El nombre del evento es obligatorio.");
        var item = ToEntity(request.Event);
        db.DisasterEvents.Add(item);
        await db.SaveChangesAsync(context.CancellationToken);
        return new EventReply { Event = ToProto(item) };
    });
    public override Task<ListEventsReply> ListEvents(ListEventsRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        var result = new ListEventsReply();
        result.Events.AddRange((await db.DisasterEvents.AsNoTracking().OrderByDescending(x => x.StartedAt)
            .ToListAsync(context.CancellationToken)).Select(ToProto));
        return result;
    });
    public override Task<ImpactReply> CreateImpact(CreateImpactRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        Validate(request.Impact);
        var item = ToEntity(request.Impact);
        db.Impacts.Add(item);
        await db.SaveChangesAsync(context.CancellationToken);
        return new ImpactReply { Impact = ToProto(item) };
    });
    public override Task<BatchReply> CreateImpactsBatch(CreateImpactsBatchRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        foreach (var impact in request.Impacts) Validate(impact);
        int inserted = await bulkWriter.WriteAsync(request.Impacts, context.CancellationToken);
        return new BatchReply { Inserted = inserted };
    });
    public override Task<IngestBatchReply> IngestImpactsBatch(IngestBatchRequest request, ServerCallContext context) =>
        Execute(context, () => ingestor.IngestAsync(request, context.CancellationToken));
    public override Task<ImpactReply> GetImpact(GetByIdRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        var id = ParseId(request.Id);
        var item = await db.Impacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, context.CancellationToken)
            ?? throw Fail(StatusCode.NotFound, "No se encontró la afectación.");
        return new ImpactReply { Impact = ToProto(item) };
    });
    public override Task<ListImpactsReply> ListImpacts(ImpactFilter request, ServerCallContext context) => Execute(context, async () =>
    {
        var query = Query(request);
        int total = await query.CountAsync(context.CancellationToken);
        int page = Math.Max(1, request.Page);
        int size = request.PageSize <= 0 ? 100 : Math.Min(2000, request.PageSize);
        var pagedQuery = query.OrderByDescending(x => x.ReportedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size);
        logger.LogInformation("SQL ListImpacts: {Sql}", pagedQuery.ToQueryString());
        var rows = await pagedQuery.ToListAsync(context.CancellationToken);
        var result = new ListImpactsReply { Total = total };
        result.Impacts.AddRange(rows.Select(ToProto));
        return result;
    });
    public override Task<ImpactReply> UpdateImpact(UpdateImpactRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        Validate(request.Impact);
        var id = ParseId(request.Impact.Id);
        var item = await db.Impacts.FirstOrDefaultAsync(x => x.Id == id, context.CancellationToken)
            ?? throw Fail(StatusCode.NotFound, "No se encontró la afectación.");
        var replacement = ToEntity(request.Impact);
        item.EventId = replacement.EventId; item.Latitude = replacement.Latitude;
        item.Longitude = replacement.Longitude; item.Severity = replacement.Severity;
        item.AffectedPeople = replacement.AffectedPeople; item.Municipality = replacement.Municipality;
        item.Department = replacement.Department; item.Description = replacement.Description;
        item.ReportedAt = replacement.ReportedAt; item.Source = replacement.Source; item.Status = replacement.Status;
        item.AffectationLevel = replacement.AffectationLevel;
        await db.SaveChangesAsync(context.CancellationToken);
        return new ImpactReply { Impact = ToProto(item) };
    });
    public override Task<DeleteReply> DeleteImpact(GetByIdRequest request, ServerCallContext context) => Execute(context, async () =>
    {
        var id = ParseId(request.Id);
        var item = await db.Impacts.FirstOrDefaultAsync(x => x.Id == id, context.CancellationToken)
            ?? throw Fail(StatusCode.NotFound, "No se encontró la afectación.");
        item.IsDeleted = true;
        await db.SaveChangesAsync(context.CancellationToken);
        return new DeleteReply { Deleted = true };
    });
    public override Task<SummaryReply> SummaryByMunicipality(ImpactFilter request, ServerCallContext context) => Execute(context, async () =>
    {
        var summaryQuery = Query(request).GroupBy(x => new { x.Municipality, x.Department })
            .Select(g => new
            {
                g.Key.Municipality, g.Key.Department, Impacts = g.Count(),
                People = g.Sum(x => x.AffectedPeople),
                MaxSeverity = g.Max(x => x.Severity == "CRITICAL" ? 4 :
                    x.Severity == "HIGH" ? 3 : x.Severity == "MEDIUM" ? 2 : 1)
            }).OrderByDescending(x => x.Impacts);
        logger.LogInformation("SQL SummaryByMunicipality: {Sql}", summaryQuery.ToQueryString());
        var rows = await summaryQuery.ToListAsync(context.CancellationToken);
        var result = new SummaryReply();
        foreach (var row in rows)
            result.Items.Add(new MunicipalitySummary
            {
                Municipality = row.Municipality, Department = row.Department,
                Impacts = row.Impacts, AffectedPeople = row.People, MaxSeverity = (Severity)row.MaxSeverity
            });
        return result;
    });
}
