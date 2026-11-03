using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Validation;

namespace DisasterMaps.Business.Application;

/// <summary>Casos de uso de eventos de desastre. Sin estado, scoped.</summary>
public sealed class EventService(IDataGateway gateway, CreateEventValidator validator, TimeProvider clock)
{
    public Task<IReadOnlyList<EventDto>> ListAsync(CancellationToken ct) => gateway.ListEventsAsync(ct);

    public Task<EventDto> CreateAsync(CreateEventRequest request, CancellationToken ct)
    {
        validator.Validate(request).ThrowIfInvalid();
        var evt = new EventDto(
            Guid.CreateVersion7(),
            request.Name!.Trim(),
            request.Type!.Value,
            (request.StartedAt ?? clock.GetUtcNow()).ToUniversalTime(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());
        return gateway.CreateEventAsync(evt, ct);
    }
}
