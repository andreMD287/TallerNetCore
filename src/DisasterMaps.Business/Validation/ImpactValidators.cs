using DisasterMaps.Business.Api.Dtos;
using DisasterMaps.Business.Domain;

namespace DisasterMaps.Business.Validation;

/// <summary>Validación de POST /impacts: RN-01, RN-02 (presencia), RN-03, RN-05.</summary>
public sealed class CreateImpactValidator(TimeProvider clock)
{
    public ValidationErrors Validate(CreateImpactRequest r)
    {
        var errors = new ValidationErrors();
        if (r.EventId is null || r.EventId == Guid.Empty)
            errors.Add("eventId", "El evento es obligatorio.");
        if (r.Latitude is null)
            errors.Add(ImpactRules.Fields.Latitude, "La latitud es obligatoria.");
        if (r.Longitude is null)
            errors.Add(ImpactRules.Fields.Longitude, "La longitud es obligatoria.");
        if (r.Severity is { } s && !Enum.IsDefined(s))
            errors.Add("severity", "La severidad no es válida.");
        if (r.AffectationLevel is { } level && !AffectationLevels.IsValid(level))
            errors.Add("affectationLevel", "El nivel de afectación debe estar entre 1 y 5.");

        if (r.Latitude is { } lat && r.Longitude is { } lon)
            errors.AddRange(ImpactRules.Check(lat, lon, r.AffectedPeople ?? 0, r.ReportedAt, clock.GetUtcNow()));
        else if (r.AffectedPeople is < 0)
            errors.Add(ImpactRules.Fields.AffectedPeople, "Las personas afectadas no pueden ser negativas.");
        return errors;
    }
}

/// <summary>Validación de PUT /impacts/{id}: lo mismo que el POST + RN-10.</summary>
public sealed class UpdateImpactValidator(CreateImpactValidator createValidator)
{
    public ValidationErrors Validate(UpdateImpactRequest r, ImpactStatus currentStatus)
    {
        var errors = createValidator.Validate(r);
        if (r.Status is { } to && !StatusTransitions.IsAllowed(currentStatus, to))
            errors.Add("status", $"Transición de estado no permitida: {Name(currentStatus)} → {Name(to)}. El orden es REPORTED → VERIFIED → ATTENDED, sin saltos ni retrocesos.");
        return errors;
    }

    private static string Name(ImpactStatus s) => s.ToString().ToUpperInvariant();
}

public sealed class CreateEventValidator(TimeProvider clock)
{
    public ValidationErrors Validate(CreateEventRequest r)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(r.Name))
            errors.Add("name", "El nombre del evento es obligatorio.");
        else if (r.Name.Length > 200)
            errors.Add("name", "El nombre no puede superar 200 caracteres.");
        if (r.Type is null || !Enum.IsDefined(r.Type.Value))
            errors.Add("type", "El tipo de evento es obligatorio.");
        if (r.StartedAt is { } s && s > clock.GetUtcNow() + ImpactRules.FutureTolerance)
            errors.Add("startedAt", "La fecha de inicio no puede estar en el futuro.");
        return errors;
    }
}

public static class ImpactQueryValidator
{
    public static ValidationErrors Validate(ImpactQuery q)
    {
        var errors = new ValidationErrors();
        var bbox = new[] { q.MinLat, q.MaxLat, q.MinLon, q.MaxLon };
        if (q.HasBoundingBox && bbox.Any(v => v is null))
            errors.Add("bbox", "El área de búsqueda requiere los cuatro valores: minLat, maxLat, minLon y maxLon.");
        if (q.MinLat > q.MaxLat)
            errors.Add("minLat", "minLat no puede ser mayor que maxLat.");
        if (q.MinLon > q.MaxLon)
            errors.Add("minLon", "minLon no puede ser mayor que maxLon.");
        if (q.From > q.To)
            errors.Add("from", "La fecha inicial no puede ser posterior a la final.");
        if (q.PageSize > ImpactQuery.MaxPageSize)
            errors.Add("pageSize", $"El tamaño de página máximo es {ImpactQuery.MaxPageSize}.");
        return errors;
    }
}
