using DisasterMaps.Data.Ingestion;
using DisasterMaps.Data.Persistence;
using DisasterMaps.Data.Repositories;
using DisasterMaps.Data.Seed;
using DisasterMaps.Data.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DisasterMaps")
    ?? "Host=localhost;Port=5432;Database=disastermaps;Username=postgres;Password=postgres";

// Un mismo data source de Npgsql para EF Core y la importación ADO.NET/COPY.
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddDbContext<DisasterMapsDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
        // Las migraciones son SQL escrito a mano: el esquema lo definen ellas y no el snapshot,
        // que se arma sin convenciones y siempre difiere del modelo en tiempo de ejecución.
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddScoped<ImpactBulkWriter>();
// Carga por lotes de la app móvil: dos estrategias intercambiables por configuración o por solicitud.
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection(IngestionOptions.Section));
builder.Services.AddScoped<IImpactIngestionStrategy, DalIngestionStrategy>();
builder.Services.AddScoped<IImpactIngestionStrategy, DatabaseIngestionStrategy>();
builder.Services.AddScoped<ImpactIngestor>();
builder.Services.AddGrpc();
builder.Services.AddGrpcHealthChecks().AddCheck("postgres", () =>
{
    try
    {
        using var connection = NpgsqlDataSource.Create(connectionString);
        using var cmdConnection = connection.OpenConnection();
        return HealthCheckResult.Healthy();
    }
    catch (Exception ex) { return HealthCheckResult.Unhealthy("Sin conexión a PostgreSQL", ex); }
});
builder.Services.AddGrpcReflection();
// h2c explícito: Business usa gRPC sin TLS en la red de Docker.
builder.WebHost.ConfigureKestrel(server =>
    server.ListenAnyIP(5003, listen => listen.Protocols = HttpProtocols.Http2));
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // PostgreSQL puede tardar algunos segundos en iniciar dentro de compose.
    for (var attempt = 1; attempt <= 15; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DisasterMapsDbContext>();
            await db.Database.MigrateAsync();
            await DataSeeder.SeedAsync(db);
            break;
        }
        catch (Exception ex) when (attempt < 15)
        {
            app.Logger.LogWarning(ex, "PostgreSQL no está listo, intento {Attempt}/15", attempt);
            await Task.Delay(1500);
        }
    }
}
app.MapGrpcService<DisasterDataService>();
app.MapGrpcHealthChecksService();
if (app.Environment.IsDevelopment()) app.MapGrpcReflectionService();
app.Run();

public partial class Program { }
