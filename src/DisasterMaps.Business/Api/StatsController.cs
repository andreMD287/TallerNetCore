using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Application;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Business.Api;

[ApiController]
[Route("api/v1/stats")]
[Produces("application/json")]
public sealed class StatsController(ImpactService service) : ControllerBase
{
    /// <summary>Resumen de afectaciones por municipio (opcionalmente de un evento).</summary>
    [HttpGet("by-municipality")]
    public Task<IReadOnlyList<MunicipalitySummaryDto>> ByMunicipality([FromQuery] Guid? eventId, CancellationToken ct) =>
        service.SummaryAsync(eventId, ct);
}
