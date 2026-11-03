using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Validation;

namespace DisasterMaps.Business.Application;

/// <summary>Casos de uso de afectaciones. Servicio sin estado (equivalente a un EJB @Stateless), scoped.</summary>
public sealed class ImpactService(
    IDataGateway gateway,
    CreateImpactValidator createValidator,
    UpdateImpactValidator updateValidator,
    SeverityPolicy severityPolicy,
    TimeProvider clock)
{
    public Task<PagedResult<ImpactDto>> ListAsync(ImpactQuery query, CancellationToken ct)
    {
        ImpactQueryValidator.Validate(query).ThrowIfInvalid();
        return gateway.ListImpactsAsync(query, ct);
    }

    /// <summary>Datos para el mapa: mismos filtros, sin paginación, tope de 2000 afectaciones.</summary>
    public Task<PagedResult<ImpactDto>> ListForMapAsync(ImpactQuery query, CancellationToken ct)
    {
        query.Page = 1;
        query.PageSize = ImpactQuery.MaxPageSize;
        return ListAsync(query, ct);
    }

    public Task<ImpactDto> GetAsync(Guid id, CancellationToken ct) => gateway.GetImpactAsync(id, ct);

    /// <summary>"Recibir coordenadas": RN-01..RN-05, RN-09 y RN-10 (estado inicial).</summary>
    public Task<ImpactDto> CreateAsync(CreateImpactRequest request, CancellationToken ct)
    {
        createValidator.Validate(request).ThrowIfInvalid();

        var affected = request.AffectedPeople ?? 0;
        var impact = new ImpactDto(
            Guid.CreateVersion7(), // id generado en Business => CreateImpact idempotente
            request.EventId!.Value,
            ImpactRules.RoundCoordinate(request.Latitude!.Value),
            ImpactRules.RoundCoordinate(request.Longitude!.Value),
            severityPolicy.Resolve(request.Severity, affected),
            affected,
            Clean(request.Municipality), Clean(request.Department), Clean(request.Description),
            (request.ReportedAt ?? clock.GetUtcNow()).ToUniversalTime(),
            ResolveSource(request.Source),
            StatusTransitions.Initial);
        return gateway.CreateImpactAsync(impact, ct);
    }

    public async Task<ImpactDto> UpdateAsync(Guid id, UpdateImpactRequest request, CancellationToken ct)
    {
        var current = await gateway.GetImpactAsync(id, ct); // NOT_FOUND => 404
        updateValidator.Validate(request, current.Status).ThrowIfInvalid();

        var affected = request.AffectedPeople ?? 0;
        var updated = current with
        {
            EventId = request.EventId!.Value,
            Latitude = ImpactRules.RoundCoordinate(request.Latitude!.Value),
            Longitude = ImpactRules.RoundCoordinate(request.Longitude!.Value),
            Severity = severityPolicy.Resolve(request.Severity, affected),
            AffectedPeople = affected,
            Municipality = Clean(request.Municipality),
            Department = Clean(request.Department),
            Description = Clean(request.Description),
            ReportedAt = (request.ReportedAt ?? current.ReportedAt).ToUniversalTime(),
            Status = request.Status ?? current.Status,
            // Source se conserva: indica el canal por el que llegó originalmente (RN-09).
        };
        return await gateway.UpdateImpactAsync(updated, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => gateway.DeleteImpactAsync(id, ct);

    public Task<IReadOnlyList<MunicipalitySummaryDto>> SummaryAsync(Guid? eventId, CancellationToken ct) =>
        gateway.SummaryByMunicipalityAsync(new ImpactQuery { EventId = eventId }, ct);

    /// <summary>RN-09: el formulario es MANUAL; un dispositivo/simulador puede declarar DEVICE. FILE solo lo asigna la importación.</summary>
    public static ImpactSource ResolveSource(ImpactSource? requested) =>
        requested == ImpactSource.Device ? ImpactSource.Device : ImpactSource.Manual;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
