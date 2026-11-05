using DisasterMaps.Web.Clients;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Web.Controllers;

[Route("visualizacion")]
public sealed class VisualizacionController(IBusinessApiClient api) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("events")]
    public async Task<IActionResult> Events(CancellationToken ct)
    {
        var result = await api.SendAsync(
            HttpMethod.Get, "/api/v1/events", ct: ct);

        return Reply(result);
    }

    [HttpGet("data")]
    public async Task<IActionResult> Data(CancellationToken ct)
    {
        var result = await api.SendAsync(
            HttpMethod.Get,
            "/api/v1/visualization/interpolation" +
                Request.QueryString.Value,
            ct: ct);

        return Reply(result);
    }

    [HttpGet("compare")]
    public async Task<IActionResult> Compare(CancellationToken ct)
    {
        var result = await api.SendAsync(
            HttpMethod.Get,
            "/api/v1/visualization/compare" +
                Request.QueryString.Value,
            ct: ct);

        return Reply(result);
    }

    private IActionResult Reply(ApiResult result)
    {
        if (!result.Ok)
            return StatusCode(
                result.Status is >= 400 and < 600
                    ? result.Status : 502,
                new { message = result.Message });

        return Content(
            result.Body?.ToJsonString() ?? "{}",
            "application/json");
    }
}