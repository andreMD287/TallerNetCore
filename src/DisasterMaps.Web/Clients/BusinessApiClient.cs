using System.Net;
using System.Text.Json.Nodes;

namespace DisasterMaps.Web.Clients;

public record ApiResult(int Status, JsonNode? Body, string? Failure = null)
{
    public bool Ok => Status is >= 200 and < 300;
    public string Message => Failure ?? (Body?["title"]?.ToString() ?? "No fue posible completar la operación.");
}
public interface IBusinessApiClient
{
    Task<ApiResult> SendAsync(HttpMethod method, string path, HttpContent? body = null, CancellationToken ct = default);
}
public sealed class BusinessApiClient(HttpClient client, ILogger<BusinessApiClient> logger) : IBusinessApiClient
{
    public async Task<ApiResult> SendAsync(HttpMethod method, string path, HttpContent? body = null, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(method, path) { Content = body };
            using var resp = await client.SendAsync(req, ct);
            string raw = await resp.Content.ReadAsStringAsync(ct);
            JsonNode? data = null;
            if (!string.IsNullOrWhiteSpace(raw))
                try { data = JsonNode.Parse(raw); } catch (System.Text.Json.JsonException) { /* no JSON */ }
            return new ApiResult((int)resp.StatusCode, data,
                resp.IsSuccessStatusCode ? null :
                (int)resp.StatusCode == 503 ? "No fue posible conectarse con el servicio. Intenta de nuevo en unos segundos." : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Business no disponible");
            return new ApiResult(503, null, "No fue posible conectarse con el servicio. Intenta de nuevo en unos segundos.");
        }
    }
}
// Se utiliza solo si Business:UseFake=true. Los datos son de DEMOSTRACION.
public sealed class FakeBusinessApiClient : IBusinessApiClient
{
    private const string EventId = "11111111-1111-4111-8111-111111111111";
    public Task<ApiResult> SendAsync(HttpMethod method, string path, HttpContent? body = null, CancellationToken ct = default)
    {
        string json = path.StartsWith("/api/v1/events") ?
            """[{"id":"11111111-1111-4111-8111-111111111111","name":"Mocoa - ejemplo","type":"LANDSLIDE","startedAt":"2017-04-01T00:00:00Z","description":"Datos simulados"}]""" :
            path.Contains("geojson") ?
            """{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[-76.6466,1.1522]},"properties":{"id":"22222222-2222-4222-8222-222222222222","eventId":"11111111-1111-4111-8111-111111111111","severity":"HIGH","affectedPeople":120,"municipality":"Mocoa","department":"Putumayo","description":"Ejemplo","reportedAt":"2017-04-01T04:00:00Z","source":"MANUAL","status":"REPORTED"}}]}""" :
            path.Contains("stats") ? """[{"municipality":"Mocoa","department":"Putumayo","impacts":1,"affectedPeople":120,"maxSeverity":"HIGH"}]""" :
            """{"items":[],"total":0,"page":1,"pageSize":100}""";
        if (method != HttpMethod.Get) return Task.FromResult(new ApiResult(400, null, "El cliente simulado es de solo lectura."));
        return Task.FromResult(new ApiResult(200, JsonNode.Parse(json)));
    }
}
