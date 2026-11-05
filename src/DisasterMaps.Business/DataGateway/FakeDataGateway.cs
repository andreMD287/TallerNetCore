using System.Collections.Concurrent;
using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;
using Grpc.Core;

namespace DisasterMaps.Business.DataGateway;

/// <summary>
/// Tier de Datos simulado en memoria (DataTier:UseFake=true). Imita el contrato gRPC, incluidos
/// los errores de la tabla 3.1 (NOT_FOUND, INVALID_ARGUMENT, FAILED_PRECONDITION, ALREADY_EXISTS)
/// y el borrado lógico (RN-08). Datos de ejemplo, no oficiales.
/// </summary>
public sealed class FakeDataGateway : IDataGateway
{
    public static readonly Guid MocoaEventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    public static readonly Guid MojanaEventId = Guid.Parse("01900000-0000-7000-8000-000000000002");

    private readonly ConcurrentDictionary<Guid, EventDto> _events = new();
    private readonly ConcurrentDictionary<Guid, (ImpactDto Impact, bool Deleted)> _impacts = new();
    private readonly Lock _batchLock = new();

    public FakeDataGateway(bool seed = true)
    {
        if (!seed) return;
        Add(new EventDto(MocoaEventId, "Avalancha Mocoa (ejemplo)", EventType.Landslide, new DateTimeOffset(2017, 4, 1, 3, 0, 0, TimeSpan.Zero), "Datos de ejemplo, no oficiales."));
        Add(new EventDto(MojanaEventId, "Inundación La Mojana (ejemplo)", EventType.Flood, new DateTimeOffset(2021, 8, 27, 0, 0, 0, TimeSpan.Zero), "Datos de ejemplo, no oficiales."));
        AddImpact(MocoaEventId, 1.1522, -76.6466, Severity.Critical, 850, "Mocoa", "Putumayo", "Avalancha sobre barrio San Miguel", new(2017, 4, 1, 3, 10, 0, TimeSpan.Zero));
        AddImpact(MocoaEventId, 1.1489, -76.6512, Severity.High, 320, "Mocoa", "Putumayo", "Colapso de vivienda", new(2017, 4, 1, 3, 25, 0, TimeSpan.Zero));
        AddImpact(MojanaEventId, 8.4697, -74.5379, Severity.Critical, 1200, "Guaranda", "Sucre", "Rompimiento del dique", new(2021, 8, 27, 11, 30, 0, TimeSpan.Zero));

        void Add(EventDto e) => _events[e.Id] = e;
        void AddImpact(Guid ev, double lat, double lon, Severity s, int people, string mun, string dep, string desc, DateTimeOffset at)
        {
            var id = Guid.CreateVersion7(at);
            _impacts[id] = (new ImpactDto(id, ev, lat, lon, s, people, mun, dep, desc, at, ImpactSource.Manual, ImpactStatus.Reported), false);
        }
    }

    public Task<IReadOnlyList<EventDto>> ListEventsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EventDto>>(_events.Values.OrderByDescending(e => e.StartedAt).ToList());

    public Task<EventDto> CreateEventAsync(EventDto evt, CancellationToken ct)
    {
        if (!_events.TryAdd(evt.Id, evt))
            throw Rpc(StatusCode.AlreadyExists, $"El evento {evt.Id} ya existe.");
        return Task.FromResult(evt);
    }

    public Task<ImpactDto> CreateImpactAsync(ImpactDto impact, CancellationToken ct)
    {
        CheckConstraints(impact);
        if (!_impacts.TryAdd(impact.Id, (impact, false)))
            throw Rpc(StatusCode.AlreadyExists, $"La afectación {impact.Id} ya existe.");
        return Task.FromResult(impact);
    }

    public Task<int> CreateImpactsBatchAsync(IReadOnlyList<ImpactDto> impacts, CancellationToken ct)
    {
        lock (_batchLock)
        {
            // Atómico: se valida todo antes de insertar nada.
            foreach (var i in impacts)
            {
                CheckConstraints(i);
                if (_impacts.ContainsKey(i.Id)) throw Rpc(StatusCode.AlreadyExists, $"La afectación {i.Id} ya existe.");
            }
            foreach (var i in impacts) _impacts[i.Id] = (i, false);
        }
        return Task.FromResult(impacts.Count);
    }

    public Task<IngestResult> IngestImpactsBatchAsync(Guid batchId, IReadOnlyList<ImpactDto> impacts, IngestStrategy? strategy, int? chunkSize, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var size = chunkSize is > 0 ? chunkSize.Value : 500;
        var items = new List<IngestItem>(impacts.Count);
        var chunks = 0;
        lock (_batchLock)
        {
            foreach (var chunk in impacts.DistinctBy(i => i.Id).Chunk(size))
            {
                chunks++;
                foreach (var i in chunk)
                {
                    if (_impacts.TryGetValue(i.Id, out var existing))
                        items.Add(IsSameContent(existing.Impact, i)
                            ? new IngestItem(i.Id, SyncItemStatus.Duplicate, null)
                            : new IngestItem(i.Id, SyncItemStatus.Conflict, "Ya existe un registro con el mismo identificador y contenido distinto; no se sobrescribe."));
                    else if (!_events.ContainsKey(i.EventId))
                        items.Add(new IngestItem(i.Id, SyncItemStatus.Rejected, "El evento asociado no existe."));
                    else
                    {
                        _impacts[i.Id] = (i with { ReceivedAt = DateTimeOffset.UtcNow }, false);
                        items.Add(new IngestItem(i.Id, SyncItemStatus.Inserted, null));
                    }
                }
            }
        }
        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return Task.FromResult(new IngestResult(items, strategy ?? IngestStrategy.Database, chunks, elapsed));
    }

    private static bool IsSameContent(ImpactDto a, ImpactDto b) =>
        a.EventId == b.EventId && a.Latitude.Equals(b.Latitude) && a.Longitude.Equals(b.Longitude)
        && a.AffectedPeople == b.AffectedPeople && a.AffectationLevel == b.AffectationLevel
        && Math.Abs((a.ReportedAt - b.ReportedAt).TotalSeconds) < 1;

    public Task<ImpactDto> GetImpactAsync(Guid id, CancellationToken ct) => Task.FromResult(GetActive(id));

    public Task<PagedResult<ImpactDto>> ListImpactsAsync(ImpactQuery query, CancellationToken ct)
    {
        var filtered = Filter(query).OrderByDescending(i => i.ReportedAt).ToList();
        var (page, size) = (query.EffectivePage, query.EffectivePageSize);
        var items = filtered.Skip((page - 1) * size).Take(size).ToList();
        return Task.FromResult(new PagedResult<ImpactDto>(items, filtered.Count, page, size));
    }

    public Task<ImpactDto> UpdateImpactAsync(ImpactDto impact, CancellationToken ct)
    {
        GetActive(impact.Id);
        CheckConstraints(impact);
        _impacts[impact.Id] = (impact, false);
        return Task.FromResult(impact);
    }

    public Task<bool> DeleteImpactAsync(Guid id, CancellationToken ct)
    {
        var impact = GetActive(id);
        _impacts[id] = (impact, true);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<MunicipalitySummaryDto>> SummaryByMunicipalityAsync(ImpactQuery query, CancellationToken ct)
    {
        IReadOnlyList<MunicipalitySummaryDto> result = Filter(query)
            .GroupBy(i => (Municipality: i.Municipality ?? "", Department: i.Department ?? ""))
            .Select(g => new MunicipalitySummaryDto(g.Key.Municipality, g.Key.Department, g.Count(), g.Sum(i => i.AffectedPeople), g.Max(i => i.Severity)))
            .OrderByDescending(s => s.AffectedPeople)
            .ToList();
        return Task.FromResult(result);
    }

    private IEnumerable<ImpactDto> Filter(ImpactQuery q)
    {
        var items = _impacts.Values.Where(x => !x.Deleted).Select(x => x.Impact);
        if (q.EventId is { } eventId) items = items.Where(i => i.EventId == eventId);
        if (q.EventType is { } type) items = items.Where(i => _events.TryGetValue(i.EventId, out var e) && e.Type == type);
        if (q.MinSeverity is { } min) items = items.Where(i => i.Severity >= min);
        if (q is { MinLat: { } minLat, MaxLat: { } maxLat, MinLon: { } minLon, MaxLon: { } maxLon })
            items = items.Where(i => i.Latitude >= minLat && i.Latitude <= maxLat && i.Longitude >= minLon && i.Longitude <= maxLon);
        if (q.From is { } from) items = items.Where(i => i.ReportedAt >= from);
        if (q.To is { } to) items = items.Where(i => i.ReportedAt <= to);
        return items;
    }

    private ImpactDto GetActive(Guid id) =>
        _impacts.TryGetValue(id, out var x) && !x.Deleted ? x.Impact : throw Rpc(StatusCode.NotFound, $"La afectación {id} no existe.");

    /// <summary>Espejo de las CHECK y la FK de la BD (última barrera).</summary>
    private void CheckConstraints(ImpactDto i)
    {
        if (i.Latitude is < -90 or > 90 || i.Longitude is < -180 or > 180 || (i.Latitude == 0 && i.Longitude == 0) || i.AffectedPeople < 0)
            throw Rpc(StatusCode.InvalidArgument, "La afectación viola una restricción de la base de datos.");
        if (!_events.ContainsKey(i.EventId))
            throw Rpc(StatusCode.FailedPrecondition, $"El evento {i.EventId} no existe.");
    }

    private static RpcException Rpc(StatusCode code, string detail) => new(new Status(code, detail));
}
