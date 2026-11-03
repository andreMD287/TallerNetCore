using DisasterMaps.Web.Clients;
using DisasterMaps.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace DisasterMaps.Web.Controllers;

public sealed class ImpactsController(IBusinessApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, string? eventId = null, CancellationToken ct = default)
    {
        var query = $"?page={Math.Max(1,page)}&pageSize=30" +
            (string.IsNullOrWhiteSpace(eventId) ? "" : "&eventId=" + Uri.EscapeDataString(eventId));
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/impacts" + query, ct: ct);
        ViewBag.Warning = !response.Ok ? response.Message : null;
        ViewBag.Page = Math.Max(1, page);
        return View(response.Body ?? new JsonObject { ["items"] = new JsonArray(), ["total"] = 0 });
    }
    [HttpGet]
    public async Task<IActionResult> Create(double? latitude, double? longitude, CancellationToken ct)
    {
        await LoadEvents(ct);
        return View(new ImpactForm { Latitude = latitude ?? 4.6, Longitude = longitude ?? -74.1 });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImpactForm form, CancellationToken ct)
    {
        await LoadEvents(ct);
        if (!ModelState.IsValid) return View(form);
        if (form.Latitude == 0 && form.Longitude == 0)
        {
            ModelState.AddModelError(nameof(form.Latitude), "La ubicación (0, 0) no es válida.");
            return View(form);
        }
        using var body = JsonContent.Create(new
        {
            form.EventId, form.Latitude, form.Longitude, form.Severity,
            form.AffectedPeople, form.Municipality, form.Department, form.Description, form.Source
        });
        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/impacts", body, ct);
        if (response.Ok) return RedirectToAction("Index");
        AddErrors(response);
        return View(form);
    }
    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/impacts/" + Uri.EscapeDataString(id), ct: ct);
        if (!response.Ok || response.Body is null) return RedirectToAction("Index");
        return View(response.Body);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, string status, CancellationToken ct)
    {
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/impacts/" + Uri.EscapeDataString(id), ct: ct);
        if (!response.Ok || response.Body is not JsonObject obj) return RedirectToAction("Index");
        string old = obj["status"]?.ToString() ?? "REPORTED";
        string expected = old switch { "REPORTED" => "VERIFIED", "VERIFIED" => "ATTENDED", _ => "" };
        if (status != expected) { TempData["Warning"] = "Solo puedes avanzar al siguiente estado."; return RedirectToAction("Edit", new { id }); }
        obj["status"] = status;
        // PUT incluye únicamente campos del contrato; id se proporciona en la ruta.
        obj.Remove("id");
        using var body = JsonContent.Create(obj);
        var update = await api.SendAsync(HttpMethod.Put, "/api/v1/impacts/" + Uri.EscapeDataString(id), body, ct);
        if (update.Ok) return RedirectToAction("Index");
        TempData["Warning"] = update.Message;
        return RedirectToAction("Edit", new { id });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var response = await api.SendAsync(HttpMethod.Delete, "/api/v1/impacts/" + Uri.EscapeDataString(id), ct: ct);
        if (!response.Ok) TempData["Warning"] = response.Message;
        return RedirectToAction("Index");
    }
    private async Task LoadEvents(CancellationToken ct)
    {
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/events", ct: ct);
        ViewBag.Events = response.Body as JsonArray ?? new JsonArray();
        if (!response.Ok) ViewBag.Warning = response.Message;
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
