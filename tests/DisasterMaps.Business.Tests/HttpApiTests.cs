using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisasterMaps.Business.DataGateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisasterMaps.Business.Tests;

/// <summary>Pruebas HTTP de punta a punta contra Business con FakeDataGateway (entorno Development).</summary>
public class HttpApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

    [Fact]
    public async Task Post_impact_returns_201_with_location_and_contract_json()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/impacts", new
        {
            eventId = FakeDataGateway.MocoaEventId, latitude = 1.1522, longitude = -76.6466, affectedPeople = 600, source = "DEVICE",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("CRITICAL", json.GetProperty("severity").GetString());
        Assert.Equal("DEVICE", json.GetProperty("source").GetString());
        Assert.Equal("REPORTED", json.GetProperty("status").GetString());
        Assert.EndsWith("Z", json.GetProperty("reportedAt").GetString());
    }

    [Fact] // ValidationProblemDetails con errores por campo, en español
    public async Task Invalid_impact_returns_validation_problem_details()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/impacts", new
        {
            eventId = FakeDataGateway.MocoaEventId, latitude = 95, longitude = -76.6,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("La latitud debe estar en el rango [-90, 90].", json.GetProperty("errors").GetProperty("latitude")[0].GetString());
        Assert.True(json.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Import_sample_csv_returns_report_17_3()
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(Samples.Path("impacts-sample.csv"))), "file", "impacts-sample.csv" },
            { new StringContent(FakeDataGateway.MocoaEventId.ToString()), "eventId" },
        };
        var response = await _http.PostAsync("/api/v1/impacts/import", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(17, json.GetProperty("accepted").GetInt32());
        Assert.Equal(3, json.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public async Task Geojson_endpoint_returns_feature_collection_lon_lat()
    {
        var json = JsonDocument.Parse(await _http.GetStringAsync($"/api/v1/impacts/geojson?eventId={FakeDataGateway.MocoaEventId}")).RootElement;
        Assert.Equal("FeatureCollection", json.GetProperty("type").GetString());
        var coords = json.GetProperty("features")[0].GetProperty("geometry").GetProperty("coordinates");
        Assert.True(coords[0].GetDouble() < -70); // longitud de Colombia primero
    }

    [Fact]
    public async Task Unknown_impact_returns_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/v1/impacts/{Guid.NewGuid()}")).StatusCode);
}

/// <summary>Meta de disponibilidad (sección 5): con Data caído, Business responde 503 en ≤ 3,5 s.</summary>
public class DataTierDownTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Returns_503_quickly_when_data_tier_is_down()
    {
        var http = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("DataTier:UseFake", "false")
            .UseSetting("DataTier:Address", "http://localhost:1")) // puerto cerrado
            .CreateClient();

        var watch = Stopwatch.StartNew();
        var response = await http.GetAsync("/api/v1/events");
        watch.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3.5), $"Tardó {watch.Elapsed.TotalSeconds:F2} s");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        // UNAVAILABLE o DEADLINE_EXCEEDED (según cuánto tarde el SO en rechazar la conexión): ambos → 503 amigable.
        Assert.Contains("Intenta de nuevo", json.GetProperty("title").GetString());

        var health = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
    }
}
