using DisasterMaps.Web.Clients;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace DisasterMaps.Web.Controllers;

[Route("")]
public sealed class MapController(IBusinessApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var events = await api.SendAsync(HttpMethod.Get, "/api/v1/events", ct: ct);
        ViewBag.Events = events.Body as JsonArray ?? new JsonArray();
        ViewBag.Warning = !events.Ok ? events.Message : null;
        return View();
    }

    [HttpGet("map/data")]
    public async Task<IActionResult> Data(CancellationToken ct)
    {
        // Arquitectura cerrada: el navegador solo accede a Web.
        string filters = Request.QueryString.Value ?? "";
        var result = await api.SendAsync(HttpMethod.Get, "/api/v1/impacts/geojson" + filters, ct: ct);
        if (!result.Ok)
            return StatusCode(result.Status == 503 ? 503 : 502,
                new { message = result.Message, type = "FeatureCollection", features = Array.Empty<object>() });
        return Content(result.Body?.ToJsonString() ?? "{\"type\":\"FeatureCollection\",\"features\":[]}",
            "application/geo+json");
    }
}
