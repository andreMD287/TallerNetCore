using Grpc.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace DisasterMaps.Business.Errors;

/// <summary>
/// Traduce excepciones a application/problem+json (RFC 7807) con mensajes en español:
/// validación → 400 ValidationProblemDetails; RpcException → tabla 3.1; resto → 500 sin detalles internos.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case BusinessValidationException v:
                problem = new ValidationProblemDetails(v.Errors)
                {
                    Title = "Uno o más campos no son válidos.",
                    Status = StatusCodes.Status400BadRequest,
                };
                break;

            case RpcException rpc:
                var map = GrpcErrorMapper.Map(rpc.StatusCode);
                problem = new ProblemDetails
                {
                    Title = map.Title,
                    Status = map.Status,
                    Detail = map.ExposeDetail && !string.IsNullOrWhiteSpace(rpc.Status.Detail) ? rpc.Status.Detail : null,
                };
                if (map.Status >= 500)
                    logger.LogWarning("Error del tier de Datos {GrpcStatus}: {Detail}", rpc.StatusCode, rpc.Status.Detail);
                break;

            case BadHttpRequestException bad:
                problem = new ProblemDetails { Title = "La solicitud no es válida.", Status = bad.StatusCode, Detail = bad.StatusCode == StatusCodes.Status413PayloadTooLarge ? "El archivo supera el tamaño máximo permitido." : null };
                break;

            case OperationCanceledException when http.RequestAborted.IsCancellationRequested:
                return true; // el cliente cerró la conexión

            default:
                logger.LogError(exception, "Error no controlado");
                problem = new ProblemDetails { Title = "Ocurrió un error inesperado.", Status = StatusCodes.Status500InternalServerError };
                break;
        }

        http.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>Agrega traceId a todos los ProblemDetails (útil para correlacionar con los logs).</summary>
    public static void Customize(ProblemDetailsContext ctx)
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString()
                      ?? ctx.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString()
                      ?? ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Extensions["traceId"] = traceId;
    }
}
