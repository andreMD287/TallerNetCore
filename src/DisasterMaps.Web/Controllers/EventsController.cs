using DisasterMaps.Web.Clients;
using DisasterMaps.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace DisasterMaps.Web.Controllers;

public sealed class EventsController(IBusinessApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/events", ct: ct);
        ViewBag.Warning = !response.Ok ? response.Message : null;
        return View(response.Body as JsonArray ?? new JsonArray());
    }
    [HttpGet] public IActionResult Create() => View(new EventForm());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EventForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);
        using var body = JsonContent.Create(new
        {
            form.Name, form.Type, StartedAt = form.StartedAt.ToUniversalTime().ToString("O"), form.Description
        });
        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/events", body, ct);
        if (response.Ok) return RedirectToAction("Index");
        AddErrors(response);
        return View(form);
    }
    private void AddErrors(ApiResult result)
    {
        if (result.Body?["errors"] is JsonObject errors)
            foreach (var entry in errors)
                if (entry.Value is JsonArray messages)
                    foreach (var message in messages) ModelState.AddModelError(entry.Key, message?.ToString() ?? "Dato inválido.");
        ModelState.AddModelError("", result.Message);
    }
}
