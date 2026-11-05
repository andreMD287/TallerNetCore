using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Application;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Errors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisasterMaps.Business.Tests;

/// <summary>Sincronización de reportes capturados sin conexión (POST /api/v1/sync/batch).</summary>
public sealed class SyncServiceTests
{
    private readonly FakeDataGateway _gateway = new();
    private readonly SyncService _service;

    public SyncServiceTests() =>
        _service = new SyncService(_gateway, new TestOptionsMonitor<SyncOptions>(new SyncOptions()),
            new FixedTime(Samples.Now), NullLogger<SyncService>.Instance);

    private static SyncItemRequest Item(Guid? id = null, double lat = 1.1522, double lon = -76.6466, int? level = 3,
        Guid? eventId = null, DateTimeOffset? at = null, int people = 10) =>
        new(id ?? Guid.CreateVersion7(), eventId ?? FakeDataGateway.MocoaEventId, lat, lon, level, people,
            "Mocoa", "Putumayo", "Reporte de campo", at ?? Samples.Now.AddHours(-6));

    private static SyncBatchRequest Batch(params SyncItemRequest[] items) => new(Guid.CreateVersion7(), "tablet-01", items);

    [Fact]
    public async Task Valid_batch_is_inserted_with_mobile_source_level_and_device()
    {
        var item = Item(level: 5);
        var report = await _service.SyncAsync(Batch(item, Item(level: 1)), null, null, default);

        Assert.Equal(2, report.Inserted);
        Assert.All(report.Results, r => Assert.Equal(SyncItemStatus.Inserted, r.Status));
        var stored = await _gateway.GetImpactAsync(item.ClientId!.Value, default);
        Assert.Equal(ImpactSource.Mobile, stored.Source);
        Assert.Equal(5, stored.AffectationLevel);
        Assert.Equal(Severity.Critical, stored.Severity);
        Assert.Equal("tablet-01", stored.DeviceId);
        Assert.Equal(item.ReportedAt, stored.ReportedAt); // T_eff: cuándo ocurrió, no cuándo llegó
        Assert.NotNull(stored.ReceivedAt);                // T_sys: cuándo lo incorporó la plataforma
    }

    [Fact] // Idempotencia: el dispositivo reenvía el lote porque no recibió la respuesta.
    public async Task Resending_the_same_batch_returns_duplicate_and_inserts_nothing()
    {
        var batch = Batch(Item(), Item());
        await _service.SyncAsync(batch, null, null, default);
        var before = (await _gateway.ListImpactsAsync(new ImpactQuery { PageSize = 2000 }, default)).Total;

        var again = await _service.SyncAsync(batch, null, null, default);

        Assert.Equal(2, again.Duplicates);
        Assert.Equal(0, again.Inserted);
        Assert.Equal(before, (await _gateway.ListImpactsAsync(new ImpactQuery { PageSize = 2000 }, default)).Total);
    }

    [Fact] // Nunca se sobrescribe en silencio (SAD 4.8).
    public async Task Same_id_with_different_content_is_a_conflict_and_keeps_the_original()
    {
        var id = Guid.CreateVersion7();
        await _service.SyncAsync(Batch(Item(id, level: 2)), null, null, default);

        var report = await _service.SyncAsync(Batch(Item(id, level: 4)), null, null, default);

        Assert.Equal(SyncItemStatus.Conflict, report.Results[0].Status);
        Assert.Equal(2, (await _gateway.GetImpactAsync(id, default)).AffectationLevel);
    }

    [Fact] // Un registro inválido no bloquea a los demás.
    public async Task Invalid_items_are_rejected_one_by_one_and_valid_ones_are_inserted()
    {
        var report = await _service.SyncAsync(Batch(
            Item(),
            Item(lat: 95),
            Item(level: 6),
            Item(level: null),
            Item(at: Samples.Now.AddHours(2)),
            Item(eventId: Guid.CreateVersion7())), null, null, default);

        Assert.Equal(SyncItemStatus.Inserted, report.Results[0].Status);
        Assert.Contains("latitude", report.Results[1].Errors!.Keys);
        Assert.Contains("affectationLevel", report.Results[2].Errors!.Keys);
        Assert.Contains("affectationLevel", report.Results[3].Errors!.Keys);
        Assert.Contains("reportedAt", report.Results[4].Errors!.Keys);
        Assert.Equal("El evento asociado no existe.", report.Results[5].Reason);
        Assert.Equal(1, report.Inserted);
        Assert.Equal(5, report.Rejected);
    }

    [Fact]
    public async Task Repeated_client_id_inside_the_batch_is_reported_as_duplicate()
    {
        var item = Item();
        var report = await _service.SyncAsync(Batch(item, item), null, null, default);

        Assert.Equal(SyncItemStatus.Inserted, report.Results[0].Status);
        Assert.Equal(SyncItemStatus.Duplicate, report.Results[1].Status);
    }

    [Fact]
    public async Task Malformed_envelope_is_rejected_completely()
    {
        var bad = new SyncBatchRequest(null, " ", [Item() with { ClientId = null }]);

        var ex = await Assert.ThrowsAsync<BusinessValidationException>(() => _service.SyncAsync(bad, null, 0, default));

        Assert.Contains("batchId", ex.Errors.Keys);
        Assert.Contains("deviceId", ex.Errors.Keys);
        Assert.Contains("items[0].clientId", ex.Errors.Keys);
        Assert.Contains("chunkSize", ex.Errors.Keys);
    }

    [Fact]
    public async Task Batch_over_the_limit_is_rejected()
    {
        var service = new SyncService(_gateway, new TestOptionsMonitor<SyncOptions>(new SyncOptions { MaxItems = 2 }),
            new FixedTime(Samples.Now), NullLogger<SyncService>.Instance);

        var ex = await Assert.ThrowsAsync<BusinessValidationException>(() => service.SyncAsync(Batch(Item(), Item(), Item()), null, null, default));

        Assert.Contains("items", ex.Errors.Keys);
    }

    [Fact]
    public async Task Requested_strategy_and_chunk_size_reach_the_data_tier()
    {
        var report = await _service.SyncAsync(Batch(Item(), Item(), Item()), IngestStrategy.Dal, 2, default);

        Assert.Equal(IngestStrategy.Dal, report.Strategy);
        Assert.Equal(2, report.Chunks);
    }
}

public sealed class AffectationLevelTests
{
    [Theory]
    [InlineData(1, Severity.Low)]
    [InlineData(2, Severity.Medium)]
    [InlineData(3, Severity.High)]
    [InlineData(4, Severity.Critical)]
    [InlineData(5, Severity.Critical)]
    public void Level_maps_to_severity(int level, Severity expected) =>
        Assert.Equal(expected, AffectationLevels.ToSeverity(level));

    [Fact]
    public void Impact_without_level_derives_it_from_severity()
    {
        var dto = new ImpactDto(Guid.NewGuid(), Guid.NewGuid(), 1, 1, Severity.High, 0, null, null, null,
            Samples.Now, ImpactSource.Manual, ImpactStatus.Reported);

        Assert.Equal(3, dto.AffectationLevel);
        Assert.Equal(5, (dto with { AffectationLevel = 5 }).AffectationLevel);
    }
}

public sealed class SyncHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

    private static object Item(Guid id, int level = 4) => new
    {
        clientId = id, eventId = FakeDataGateway.MocoaEventId, latitude = 1.1531234567, longitude = -76.6481,
        affectationLevel = level, affectedPeople = 12, reportedAt = DateTimeOffset.UtcNow.AddHours(-3),
    };

    [Fact]
    public async Task Post_sync_batch_returns_report_with_one_result_per_item()
    {
        var ok = Guid.CreateVersion7();
        var response = await _http.PostAsJsonAsync("/api/v1/sync/batch?strategy=DAL", new
        {
            batchId = Guid.CreateVersion7(), deviceId = "celular-campo-7", items = new[] { Item(ok), Item(Guid.CreateVersion7(), level: 9) },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("DAL", json.GetProperty("strategy").GetString());
        Assert.Equal(1, json.GetProperty("inserted").GetInt32());
        Assert.Equal("INSERTED", json.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.Equal("REJECTED", json.GetProperty("results")[1].GetProperty("status").GetString());

        var impact = JsonDocument.Parse(await _http.GetStringAsync($"/api/v1/impacts/{ok}")).RootElement;
        Assert.Equal("MOBILE", impact.GetProperty("source").GetString());
        Assert.Equal(4, impact.GetProperty("affectationLevel").GetInt32());
        Assert.Equal(1.153123, impact.GetProperty("latitude").GetDouble()); // RN-01: 6 decimales
        Assert.Equal("celular-campo-7", impact.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task Malformed_sync_batch_returns_400_problem_details()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/sync/batch", new { deviceId = "x", items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("errors").TryGetProperty("items", out _));
        Assert.True(json.GetProperty("errors").TryGetProperty("batchId", out _));
    }

    [Fact]
    public async Task Post_impact_accepts_affectation_level()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/impacts", new
        {
            eventId = FakeDataGateway.MocoaEventId, latitude = 1.15, longitude = -76.64, affectationLevel = 5,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5, json.GetProperty("affectationLevel").GetInt32());
        Assert.Equal("CRITICAL", json.GetProperty("severity").GetString());
    }
}
