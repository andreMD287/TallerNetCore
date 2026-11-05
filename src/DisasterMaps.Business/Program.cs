// Tier 2 - Negocio. REST /api/v1 hacia arriba, gRPC hacia el tier de Datos.
using System.Diagnostics;
using DisasterMaps.Business.Api;
using DisasterMaps.Business.Application;
using DisasterMaps.Business.DataGateway;
using DisasterMaps.Business.Domain;
using DisasterMaps.Business.Errors;
using DisasterMaps.Business.Import;
using DisasterMaps.Business.Json;
using DisasterMaps.Business.Validation;
using DisasterMaps.Contracts.Data.V1;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging estructurado: JSON por lÃ­nea con TraceId/SpanId (decisiÃ³n DEC-02) ----
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // tildes legibles
    };
});
builder.Logging.Configure(o => o.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

// ---- ConfiguraciÃ³n (equivalente a CDI/JNDI: direcciones y umbrales fuera del cÃ³digo) ----
builder.Services.Configure<DataTierOptions>(builder.Configuration.GetSection(DataTierOptions.Section));
builder.Services.Configure<SeverityThresholds>(builder.Configuration.GetSection(SeverityThresholds.Section));
var dataTier = builder.Configuration.GetSection(DataTierOptions.Section).Get<DataTierOptions>() ?? new DataTierOptions();

// ---- Fachada hacia Datos: fake en memoria o cliente gRPC real ----
if (dataTier.UseFake)
{
    builder.Services.AddSingleton<IDataGateway>(_ => new FakeDataGateway());
}
else
{
    var address = new Uri(dataTier.Address);
    builder.Services.AddGrpcClient<DisasterData.DisasterDataClient>(o => o.Address = address)
        .ConfigureChannel(c => c.ServiceConfig = GrpcRetryPolicy.Create());
    builder.Services.AddGrpcClient<Health.HealthClient>(o => o.Address = address);
    builder.Services.AddScoped<IDataGateway, GrpcDataGateway>();
}

// ---- Dominio, validaciÃ³n, importaciÃ³n y servicios de aplicaciÃ³n (sin estado) ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SeverityPolicy>();
builder.Services.AddSingleton<CreateImpactValidator>();
builder.Services.AddSingleton<UpdateImpactValidator>();
builder.Services.AddSingleton<CreateEventValidator>();
builder.Services.AddSingleton<IImpactFileParser, CsvImpactParser>();
builder.Services.AddSingleton<IImpactFileParser, GeoJsonImpactParser>();
builder.Services.AddSingleton<ImportRowConverter>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<ImpactService>();
builder.Services.AddScoped<DisasterMaps.Business.Visualization.VisualizationService>();
builder.Services.AddScoped<ImportService>();

// ---- API REST ----
builder.Services.AddControllers(o =>
    {
        var m = o.ModelBindingMessageProvider;
        m.SetValueIsInvalidAccessor(v => $"El valor '{v}' no es vÃ¡lido.");
        m.SetAttemptedValueIsInvalidAccessor((v, f) => $"El valor '{v}' no es vÃ¡lido para {f}.");
        m.SetValueMustNotBeNullAccessor(f => $"El campo {f} es obligatorio.");
        m.SetMissingRequestBodyRequiredValueAccessor(() => "El cuerpo de la solicitud es obligatorio.");
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(v => $"El valor '{v}' no es vÃ¡lido.");
        m.SetUnknownValueIsInvalidAccessor(f => $"El valor de {f} no es vÃ¡lido.");
    })
    .AddJsonOptions(o => JsonSetup.Configure(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = InvalidModelState);
builder.Services.ConfigureHttpJsonOptions(o => JsonSetup.Configure(o.SerializerOptions));

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = GlobalExceptionHandler.Customize);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddBusinessHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("DisasterMaps Â· Business API v1"));
}

app.MapControllers();
app.MapBusinessHealth();
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.Logger.LogInformation("Business iniciado. Tier de Datos: {Mode} {Address}",
    dataTier.UseFake ? "FAKE" : "gRPC", dataTier.UseFake ? "(memoria)" : dataTier.Address);

app.Run();

// Errores de binding/JSON â†’ ValidationProblemDetails en espaÃ±ol, con nombres de campo limpios.
static IActionResult InvalidModelState(ActionContext ctx)
{
    var errors = new Dictionary<string, string[]>();
    foreach (var (key, entry) in ctx.ModelState)
    {
        if (entry.Errors.Count == 0) continue;
        if (key.StartsWith("$.", StringComparison.Ordinal) || key == "$")
        {
            var field = key == "$" ? "body" : char.ToLowerInvariant(key[2]) + key[3..];
            errors[field] = ["El valor no es vÃ¡lido o no tiene el formato esperado."];
        }
        else
        {
            errors[string.IsNullOrEmpty(key) ? "body" : char.ToLowerInvariant(key[0]) + key[1..]] =
                entry.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "El valor no es vÃ¡lido." : e.ErrorMessage).ToArray();
        }
    }
    // Si el JSON era invÃ¡lido, ASP.NET agrega ademÃ¡s "request: obligatorio"; se omite si hay errores mÃ¡s precisos.
    if (errors.Count > 1) errors.Remove("request");

    var problem = new ValidationProblemDetails(errors)
    {
        Title = "Uno o mÃ¡s campos no son vÃ¡lidos.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier },
    };
    return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
}

/// <summary>Visible para WebApplicationFactory en las pruebas.</summary>
public partial class Program;
