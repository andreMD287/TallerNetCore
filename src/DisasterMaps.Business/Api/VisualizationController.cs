using DisasterMaps.Business.Visualization;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Business.Api;

[ApiController]
[Route("api/v1/visualization")]
[Produces("application/json")]
public sealed class VisualizationController(
    VisualizationService service) : ControllerBase
{
    [HttpGet("interpolation")]
    public async Task<IActionResult> Interpolation(
        [FromQuery] VisualizationRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await service.InterpolateAsync(request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                title = "Solicitud de visualizacion invalida.",
                detail = ex.Message
            });
        }
    }

    [HttpGet("compare")]
    public async Task<IActionResult> Compare(
        [FromQuery] VisualizationRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await service.CompareAsync(request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                title = "Solicitud de comparacion invalida.",
                detail = ex.Message
            });
        }
    }
}