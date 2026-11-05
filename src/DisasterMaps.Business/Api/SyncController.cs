using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Application;
using DisasterMaps.Business.Domain;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Business.Api;

[ApiController]
[Route("api/v1/sync")]
[Produces("application/json")]
public sealed class SyncController(SyncService service) : ControllerBase
{
    /// <summary>
    /// Recibe el lote de reportes que un dispositivo capturó sin conexión. Responde 200 con un resultado
    /// por registro aunque algunos se rechacen. <c>strategy</c> (DAL o DATABASE) y <c>chunkSize</c> son
    /// opcionales y sirven para comparar las dos formas de carga; si no vienen, decide el tier de Datos.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType<SyncReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<SyncReport> Batch(SyncBatchRequest request, [FromQuery] IngestStrategy? strategy, [FromQuery] int? chunkSize, CancellationToken ct) =>
        service.SyncAsync(request, strategy, chunkSize, ct);
}
