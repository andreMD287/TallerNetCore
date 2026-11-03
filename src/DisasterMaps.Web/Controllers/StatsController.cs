using DisasterMaps.Web.Clients;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace DisasterMaps.Web.Controllers;

public sealed class StatsController(IBusinessApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? eventId, CancellationToken ct)
    {
        var events = await api.SendAsync(HttpMethod.Get, "/api/v1/events", ct: ct);
        ViewBag.Events = events.Body as JsonArray ?? new JsonArray();
        var path = "/api/v1/stats/by-municipality" +
            (string.IsNullOrEmpty(eventId) ? "" : "?eventId=" + Uri.EscapeDataString(eventId));
        var response = await api.SendAsync(HttpMethod.Get, path, ct: ct);
        ViewBag.Warning = !response.Ok ? response.Message : null;
        return View(response.Body as JsonArray ?? new JsonArray());
    }
}
