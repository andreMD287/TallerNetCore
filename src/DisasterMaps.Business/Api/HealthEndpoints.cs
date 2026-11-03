using System.Text.Json;
using DisasterMaps.Business.DataGateway;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DisasterMaps.Business.Api;

/// <summary>Chequea el servicio de salud gRPC estándar (grpc.health.v1) del tier de Datos.</summary>
public sealed class DataTierHealthCheck(IServiceProvider services, IOptions<DataTierOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (options.Value.UseFake)
            return HealthCheckResult.Healthy("FakeDataGateway en memoria (DataTier:UseFake=true).");

        try
        {
            var client = services.GetRequiredService<Health.HealthClient>();
            var reply = await client.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: ct);
            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"Data en {options.Value.Address}: SERVING")
                : HealthCheckResult.Unhealthy($"Data en {options.Value.Address}: {reply.Status}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Data en {options.Value.Address} no responde.", ex is Grpc.Core.RpcException ? null : ex);
        }
    }
}

public static class HealthEndpoints
{
    public static IServiceCollection AddBusinessHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy())
            .AddCheck<DataTierHealthCheck>("data-tier");
        return services;
    }

    /// <summary>GET /health → 200 si todo está sano, 503 si Data no responde.</summary>
    public static IEndpointRouteBuilder MapBusinessHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteJson });
        return app;
    }

    private static Task WriteJson(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json";
        return http.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        }, JsonSerializerOptions.Web));
    }
}
