using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Application;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Business.Api;

[ApiController]
[Route("api/v1/events")]
[Produces("application/json")]
public sealed class EventsController(EventService service) : ControllerBase
{
    /// <summary>Lista los eventos de desastre.</summary>
    [HttpGet]
    public Task<IReadOnlyList<EventDto>> List(CancellationToken ct) => service.ListAsync(ct);

    /// <summary>Crea un evento de desastre.</summary>
    [HttpPost]
    [ProducesResponseType<EventDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return Created($"/api/v1/events/{created.Id}", created);
    }
}
