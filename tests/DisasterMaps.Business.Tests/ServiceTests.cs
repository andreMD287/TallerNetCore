using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Application;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Errors;
using DisasterMaps.Business.Import;
using DisasterMaps.Business.Validation;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisasterMaps.Business.Tests;

public class ImportServiceTests
{
    private readonly FakeDataGateway _gateway = new();
    private readonly ImportService _service;
    private static readonly Guid EventId = FakeDataGateway.MocoaEventId;

    public ImportServiceTests()
    {
        _service = new ImportService(
            [new CsvImpactParser(), new GeoJsonImpactParser()],
            new ImportRowConverter(Samples.DefaultSeverityPolicy()),
            _gateway, new FixedTime(Samples.Now), NullLogger<ImportService>.Instance);
    }

    private async Task<ImportOutcome> ImportFile(string file, Guid? eventId = null)
    {
        await using var stream = File.OpenRead(Samples.Path(file));
        return await _service.ImportAsync(stream, file, stream.Length, eventId ?? EventId, CancellationToken.None);
    }

    private Task<ImportOutcome> ImportText(string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return _service.ImportAsync(new MemoryStream(bytes), name, bytes.Length, EventId, CancellationToken.None);
    }

    private async Task<int> StoredCount() =>
        (await _gateway.ListImpactsAsync(new ImpactQuery { EventId = EventId, PageSize = 2000 }, CancellationToken.None)).Total;

    [Fact] // RN-06 con el archivo de ejemplo: 17 aceptadas / 3 rechazadas
    public async Task Sample_csv_accepts_17_and_rejects_3_with_their_lines()
    {
        var before = await StoredCount();

        var outcome = await ImportFile("impacts-sample.csv");

        Assert.True(outcome.Accepted);
        Assert.Equal(20, outcome.Report.Received);
        Assert.Equal(17, outcome.Report.Accepted);
        Assert.Equal(3, outcome.Report.Rejected);
        Assert.Equal([5, 10, 16], outcome.Report.Errors.Select(e => e.Line));
        Assert.Contains("latitud", outcome.Report.Errors[0].Reason);
        Assert.Contains("(0, 0)", outcome.Report.Errors[1].Reason);
        Assert.Contains("negativas", outcome.Report.Errors[2].Reason);
        Assert.Equal(before + 17, await StoredCount());
    }

    [Fact]
    public async Task Sample_geojson_accepts_all_10()
    {
        var outcome = await ImportFile("impacts-sample.geojson");
        Assert.Equal(10, outcome.Report.Accepted);
        Assert.Empty(outcome.Report.Errors);
    }

    [Fact] // RN-09 / RN-10 / RN-04 aplicados a filas importadas
    public async Task Imported_rows_are_file_source_reported_and_get_derived_severity()
    {
        await ImportText("x.csv", "latitude,longitude,severity,affected_people,municipality,department,description,reported_at\n" +
                                  "2.5,-75.5,,600,Neiva,Huila,prueba,2026-11-01T00:00:00Z\n");
        var stored = (await _gateway.ListImpactsAsync(new ImpactQuery { EventId = EventId, PageSize = 2000 }, CancellationToken.None))
            .Items.Single(i => i.Municipality == "Neiva");
        Assert.Equal(ImpactSource.File, stored.Source);
        Assert.Equal(ImpactStatus.Reported, stored.Status);
        Assert.Equal(Severity.Critical, stored.Severity);
    }

    [Fact] // RN-07: mismas coordenadas (5 decimales) y misma fecha
    public async Task Duplicate_rows_in_same_file_are_rejected()
    {
        var outcome = await ImportText("dup.csv",
            "latitude,longitude,severity,affected_people,municipality,department,description,reported_at\n" +
            "2.123451,-75.5,,1,A,B,,2026-11-01T00:00:00Z\n" +
            "2.123449,-75.5,,1,A,B,,2026-11-01T00:00:00Z\n" +   // misma coordenada a 5 decimales
            "2.123451,-75.5,,1,A,B,,2026-11-02T00:00:00Z\n");   // otra fecha: no es duplicada

        Assert.Equal(2, outcome.Report.Accepted);
        var error = Assert.Single(outcome.Report.Errors);
        Assert.Equal(3, error.Line);
        Assert.Contains("línea 2", error.Reason);
    }

    [Fact] // RN-06: si no hay ninguna válida, no se llama al tier de Datos
    public async Task All_invalid_rows_returns_not_accepted_and_inserts_nothing()
    {
        var before = await StoredCount();
        var outcome = await ImportText("bad.csv", "latitude,longitude\n95,1\n0,0\n");
        Assert.False(outcome.Accepted);
        Assert.Equal(2, outcome.Report.Rejected);
        Assert.Equal(before, await StoredCount());
    }

    [Fact]
    public async Task Unsupported_extension_is_rejected() =>
        await Assert.ThrowsAsync<BusinessValidationException>(() => ImportText("data.xlsx", "x"));

    [Fact]
    public async Task File_over_5_mb_is_rejected() =>
        await Assert.ThrowsAsync<BusinessValidationException>(() =>
            _service.ImportAsync(new MemoryStream(), "big.csv", ImportService.MaxFileBytes + 1, EventId, CancellationToken.None));

    [Fact] // RN-02: el evento debe existir (Data responde FAILED_PRECONDITION → 422)
    public async Task Unknown_event_fails_with_failed_precondition()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() => ImportFile("impacts-sample.csv", Guid.NewGuid()));
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }
}

public class ImpactServiceTests
{
    private readonly FakeDataGateway _gateway = new();
    private readonly ImpactService _service;

    public ImpactServiceTests()
    {
        var clock = new FixedTime(Samples.Now);
        var create = new CreateImpactValidator(clock);
        _service = new ImpactService(_gateway, create, new UpdateImpactValidator(create), Samples.DefaultSeverityPolicy(), clock);
    }

    private static CreateImpactRequest Request(ImpactSource? source = null, Severity? severity = null, int? people = 120) =>
        new(FakeDataGateway.MocoaEventId, 1.15221234567, -76.6466, severity, people, " Mocoa ", "Putumayo", null, null, source);

    [Fact]
    public async Task Create_applies_business_rules()
    {
        var created = await _service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(1.152212, created.Latitude);                 // RN-01 redondeo
        Assert.Equal(Severity.High, created.Severity);            // RN-04 derivada (120)
        Assert.Equal(Samples.Now, created.ReportedAt);            // RN-05 hora actual por defecto
        Assert.Equal(ImpactSource.Manual, created.Source);        // RN-09 por defecto
        Assert.Equal(ImpactStatus.Reported, created.Status);      // RN-10 estado inicial
        Assert.Equal("Mocoa", created.Municipality);
        Assert.Equal(7, created.Id.Version);                      // UUID v7 generado en Business
    }

    [Theory] // RN-09: el request puede traer DEVICE; FILE solo lo asigna la importación
    [InlineData(ImpactSource.Device, ImpactSource.Device)]
    [InlineData(ImpactSource.Manual, ImpactSource.Manual)]
    [InlineData(ImpactSource.File, ImpactSource.Manual)]
    public async Task Create_assigns_source_by_channel(ImpactSource requested, ImpactSource expected) =>
        Assert.Equal(expected, (await _service.CreateAsync(Request(requested), CancellationToken.None)).Source);

    [Fact]
    public async Task Create_respects_requested_severity() =>
        Assert.Equal(Severity.Low, (await _service.CreateAsync(Request(severity: Severity.Low), CancellationToken.None)).Severity);

    [Fact]
    public async Task Create_with_invalid_data_throws_validation() =>
        await Assert.ThrowsAsync<BusinessValidationException>(() => _service.CreateAsync(Request(people: -5), CancellationToken.None));

    [Fact] // RN-10 en el flujo completo
    public async Task Update_moves_status_forward_and_rejects_going_back()
    {
        var created = await _service.CreateAsync(Request(), CancellationToken.None);
        UpdateImpactRequest Update(ImpactStatus s) => new(created.EventId, created.Latitude, created.Longitude, null, 120, "Mocoa", "Putumayo", null, null, null, s);

        var verified = await _service.UpdateAsync(created.Id, Update(ImpactStatus.Verified), CancellationToken.None);
        Assert.Equal(ImpactStatus.Verified, verified.Status);
        Assert.Equal(created.Source, verified.Source);

        await Assert.ThrowsAsync<BusinessValidationException>(() =>
            _service.UpdateAsync(created.Id, Update(ImpactStatus.Reported), CancellationToken.None));
    }

    [Fact] // RN-08: borrado lógico, deja de aparecer
    public async Task Deleted_impact_is_not_found_afterwards()
    {
        var created = await _service.CreateAsync(Request(), CancellationToken.None);
        Assert.True(await _service.DeleteAsync(created.Id, CancellationToken.None));

        var ex = await Assert.ThrowsAsync<RpcException>(() => _service.GetAsync(created.Id, CancellationToken.None));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
        var list = await _service.ListAsync(new ImpactQuery { EventId = created.EventId, PageSize = 2000 }, CancellationToken.None);
        Assert.DoesNotContain(list.Items, i => i.Id == created.Id);
    }

    [Fact]
    public async Task Map_query_is_capped_at_2000_without_pagination()
    {
        var query = new ImpactQuery { Page = 5, PageSize = 10 };
        var result = await _service.ListForMapAsync(query, CancellationToken.None);
        Assert.Equal(1, result.Page);
        Assert.Equal(2000, result.PageSize);
    }
}
