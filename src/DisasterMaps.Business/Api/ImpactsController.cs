using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Application;
using DisasterMaps.Business.GeoJson;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Business.Api;

[ApiController]
[Route("api/v1/impacts")]
[Produces("application/json")]
public sealed class ImpactsController(ImpactService service) : ControllerBase
{
    /// <summary>Lista afectaciones con filtros y paginación.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ImpactDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ImpactDto>> List([FromQuery] ImpactQuery query, CancellationToken ct) =>
        service.ListAsync(query, ct);

    /// <summary>Afectaciones como GeoJSON FeatureCollection para el mapa (mismos filtros, tope 2000).</summary>
    [HttpGet("geojson")]
    [Produces("application/geo+json", "application/json")]
    public async Task<FeatureCollection> GeoJson([FromQuery] ImpactQuery query, CancellationToken ct)
    {
        var page = await service.ListForMapAsync(query, ct);
        return FeatureCollectionBuilder.Build(page.Items);
    }

    /// <summary>Detalle de una afectación.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ImpactDto> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>Recibe una coordenada de afectación (formulario o dispositivo).</summary>
    [HttpPost]
    [ProducesResponseType<ImpactDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ImpactDto>> Create(CreateImpactRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Actualiza una afectación (incluye cambio de estado, RN-10).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ImpactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ImpactDto> Update(Guid id, UpdateImpactRequest request, CancellationToken ct) =>
        service.UpdateAsync(id, request, ct);

    /// <summary>Borrado lógico (RN-08).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await service.DeleteAsync(id, ct)
            ? NoContent()
            : Problem(title: "El recurso no existe.", statusCode: StatusCodes.Status404NotFound);

    /// <summary>Lee coordenadas desde un archivo CSV o GeoJSON (multipart: file, eventId).</summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ImportService.MaxFileBytes + 1024 * 1024)] // 5 MB + margen para el sobre multipart
    [RequestFormLimits(MultipartBodyLengthLimit = ImportService.MaxFileBytes + 1024 * 1024)]
    [ProducesResponseType<ImportReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ImportReport>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import(IFormFile? file, [FromForm] Guid? eventId, [FromServices] ImportService importService, CancellationToken ct)
    {
        if (file is null)
            throw new Errors.BusinessValidationException("file", "Debe adjuntar un archivo .csv, .geojson o .json.");

        await using var stream = file.OpenReadStream();
        var outcome = await importService.ImportAsync(stream, file.FileName, file.Length, eventId, ct);
        return outcome.Accepted ? Ok(outcome.Report) : BadRequest(outcome.Report);
    }
}
