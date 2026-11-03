using DisasterMaps.Web.Clients;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace DisasterMaps.Web.Controllers;

public sealed class ImportController(IBusinessApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await LoadEvents(ct);
        return View();
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(IFormFile? file, string eventId, CancellationToken ct)
    {
        await LoadEvents(ct);
        if (file is null || file.Length == 0 || string.IsNullOrWhiteSpace(eventId))
        { ViewBag.Warning = "Selecciona un archivo y un evento."; return View(); }
        if (file.Length > 5 * 1024 * 1024)
        { ViewBag.Warning = "El archivo no puede superar 5 MB."; return View(); }
        using var multipart = new MultipartFormDataContent();
        using var stream = file.OpenReadStream();
        multipart.Add(new StreamContent(stream), "file", file.FileName);
        multipart.Add(new StringContent(eventId), "eventId");
        var result = await api.SendAsync(HttpMethod.Post, "/api/v1/impacts/import", multipart, ct);
        // Un 400 puede traer ImportReport (ninguna fila aceptada), no ProblemDetails.
        if (result.Body?["received"] is not null) ViewBag.Report = result.Body;
        else if (!result.Ok) ViewBag.Warning = result.Message;
        return View();
    }
    private async Task LoadEvents(CancellationToken ct)
    {
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/events", ct: ct);
        ViewBag.Events = response.Body as JsonArray ?? new JsonArray();
        if (!response.Ok) ViewBag.Warning = response.Message;
    }
}
