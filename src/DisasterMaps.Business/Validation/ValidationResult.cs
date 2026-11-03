using DisasterMaps.Business.Errors;

namespace DisasterMaps.Business.Validation;

/// <summary>Acumula errores por campo (formato de ValidationProblemDetails.errors).</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(message);
        return this;
    }

    public ValidationErrors AddRange(IEnumerable<(string Field, string Message)> errors)
    {
        foreach (var (f, m) in errors) Add(f, m);
        return this;
    }

    public IDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new BusinessValidationException(ToDictionary());
    }
}
