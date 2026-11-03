namespace DisasterMaps.Business.Errors;

/// <summary>Violación de reglas de negocio. Se responde 400 con ValidationProblemDetails.</summary>
public sealed class BusinessValidationException(IDictionary<string, string[]> errors)
    : Exception("Uno o más campos no son válidos.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public BusinessValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }
}
