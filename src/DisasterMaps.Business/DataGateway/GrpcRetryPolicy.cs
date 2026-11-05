using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace DisasterMaps.Business.DataGateway;

/// <summary>
/// Reintento automático (máx. 3 intentos, backoff 0,2 s → 1 s, solo ante UNAVAILABLE) únicamente
/// en operaciones idempotentes: lecturas, CreateImpact (el id lo genera Business) e IngestImpactsBatch
/// (el id lo genera el dispositivo y un reenvío se clasifica como DUPLICATE).
/// CreateImpactsBatch, UpdateImpact y DeleteImpact no se reintentan.
/// </summary>
public static class GrpcRetryPolicy
{
    public const string Service = "disastermaps.data.v1.DisasterData";

    public static readonly string[] IdempotentMethods =
        ["ListEvents", "GetImpact", "ListImpacts", "SummaryByMunicipality", "CreateImpact", "IngestImpactsBatch"];

    public static ServiceConfig Create()
    {
        var methodConfig = new MethodConfig
        {
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = 3,
                InitialBackoff = TimeSpan.FromSeconds(0.2),
                MaxBackoff = TimeSpan.FromSeconds(1),
                BackoffMultiplier = 5,
                RetryableStatusCodes = { StatusCode.Unavailable },
            },
        };
        foreach (var m in IdempotentMethods) methodConfig.Names.Add(new MethodName { Service = Service, Method = m });
        return new ServiceConfig { MethodConfigs = { methodConfig } };
    }
}
