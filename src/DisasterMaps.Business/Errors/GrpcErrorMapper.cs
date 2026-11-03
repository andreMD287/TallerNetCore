using Grpc.Core;

namespace DisasterMaps.Business.Errors;

/// <summary>Tabla de la sección 3.1 del contrato: status gRPC de Data → respuesta HTTP de Business.</summary>
public static class GrpcErrorMapper
{
    public sealed record HttpError(int Status, string Title, bool ExposeDetail);

    public static HttpError Map(StatusCode code) => code switch
    {
        StatusCode.NotFound => new(StatusCodes.Status404NotFound, "El recurso no existe.", true),
        StatusCode.InvalidArgument => new(StatusCodes.Status400BadRequest, "Los datos fueron rechazados por el tier de datos.", true),
        StatusCode.FailedPrecondition => new(StatusCodes.Status422UnprocessableEntity, "El evento indicado no existe.", true),
        StatusCode.AlreadyExists => new(StatusCodes.Status409Conflict, "El recurso ya existe.", true),
        StatusCode.Unavailable => new(StatusCodes.Status503ServiceUnavailable, "El servicio de datos no está disponible. Intenta de nuevo en unos segundos.", false),
        StatusCode.DeadlineExceeded => new(StatusCodes.Status503ServiceUnavailable, "El servicio de datos no respondió a tiempo. Intenta de nuevo en unos segundos.", false),
        _ => new(StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado.", false),
    };
}
