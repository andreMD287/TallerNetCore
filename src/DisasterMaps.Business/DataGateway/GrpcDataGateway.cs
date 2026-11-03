using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Mapping;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Proto = DisasterMaps.Contracts.Data.V1;

namespace DisasterMaps.Business.DataGateway;

/// <summary>Implementación de la fachada sobre el cliente gRPC generado desde contracts/protos/disaster_data.proto.</summary>
public sealed class GrpcDataGateway(
    Proto.DisasterData.DisasterDataClient client,
    IOptions<DataTierOptions> options,
    ILogger<GrpcDataGateway> logger) : IDataGateway
{
    private DateTime Deadline => DateTime.UtcNow.AddSeconds(options.Value.DeadlineSeconds);

    public async Task<IReadOnlyList<EventDto>> ListEventsAsync(CancellationToken ct)
    {
        var reply = await client.ListEventsAsync(new Proto.ListEventsRequest(), deadline: Deadline, cancellationToken: ct);
        return reply.Events.Select(e => e.ToDto()).ToList();
    }

    public async Task<EventDto> CreateEventAsync(EventDto evt, CancellationToken ct)
    {
        var reply = await client.CreateEventAsync(new Proto.CreateEventRequest { Event = evt.ToProto() }, deadline: Deadline, cancellationToken: ct);
        return reply.Event.ToDto();
    }

    public async Task<ImpactDto> CreateImpactAsync(ImpactDto impact, CancellationToken ct)
    {
        try
        {
            var reply = await client.CreateImpactAsync(new Proto.CreateImpactRequest { Impact = impact.ToProto() }, deadline: Deadline, cancellationToken: ct);
            return reply.Impact.ToDto();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Idempotencia: el id lo genera Business. Si un intento anterior (reintento automático) ya insertó
            // esta misma afectación, Data responde ALREADY_EXISTS: se recupera y se responde como éxito.
            var existing = await GetImpactAsync(impact.Id, ct);
            if (IsSameImpact(existing, impact))
            {
                logger.LogInformation("CreateImpact {ImpactId} ya existía (reintento); se resuelve con GetImpact", impact.Id);
                return existing;
            }
            throw;
        }
    }

    public async Task<int> CreateImpactsBatchAsync(IReadOnlyList<ImpactDto> impacts, CancellationToken ct)
    {
        var request = new Proto.CreateImpactsBatchRequest();
        request.Impacts.AddRange(impacts.Select(i => i.ToProto()));
        var reply = await client.CreateImpactsBatchAsync(request, deadline: Deadline, cancellationToken: ct);
        return reply.Inserted;
    }

    public async Task<ImpactDto> GetImpactAsync(Guid id, CancellationToken ct)
    {
        var reply = await client.GetImpactAsync(new Proto.GetByIdRequest { Id = id.ToString() }, deadline: Deadline, cancellationToken: ct);
        return reply.Impact.ToDto();
    }

    public async Task<PagedResult<ImpactDto>> ListImpactsAsync(ImpactQuery query, CancellationToken ct)
    {
        var (page, size) = (query.EffectivePage, query.EffectivePageSize);
        var reply = await client.ListImpactsAsync(query.ToProto(page, size), deadline: Deadline, cancellationToken: ct);
        return new PagedResult<ImpactDto>(reply.Impacts.Select(i => i.ToDto()).ToList(), reply.Total, page, size);
    }

    public async Task<ImpactDto> UpdateImpactAsync(ImpactDto impact, CancellationToken ct)
    {
        var reply = await client.UpdateImpactAsync(new Proto.UpdateImpactRequest { Impact = impact.ToProto() }, deadline: Deadline, cancellationToken: ct);
        return reply.Impact.ToDto();
    }

    public async Task<bool> DeleteImpactAsync(Guid id, CancellationToken ct)
    {
        var reply = await client.DeleteImpactAsync(new Proto.GetByIdRequest { Id = id.ToString() }, deadline: Deadline, cancellationToken: ct);
        return reply.Deleted;
    }

    public async Task<IReadOnlyList<MunicipalitySummaryDto>> SummaryByMunicipalityAsync(ImpactQuery query, CancellationToken ct)
    {
        var reply = await client.SummaryByMunicipalityAsync(query.ToProto(1, ImpactQuery.MaxPageSize), deadline: Deadline, cancellationToken: ct);
        return reply.Items.Select(s => s.ToDto()).ToList();
    }

    private static bool IsSameImpact(ImpactDto a, ImpactDto b) =>
        a.EventId == b.EventId && a.Latitude.Equals(b.Latitude) && a.Longitude.Equals(b.Longitude)
        && a.AffectedPeople == b.AffectedPeople && Math.Abs((a.ReportedAt - b.ReportedAt).TotalSeconds) < 1;
}
