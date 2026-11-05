using DisasterMaps.Contracts.Data.V1;

namespace DisasterMaps.Data.Services;

/// <summary>Red de seguridad del tier de Datos: reglas de forma de una afectación antes de tocar la BD.</summary>
public static class ImpactValidation
{
    /// <summary>Devuelve el motivo del rechazo o null si la afectación es válida.</summary>
    public static string? Check(Impact? i)
    {
        if (i is null) return "Falta la afectación.";
        if (!Guid.TryParse(i.Id, out _) || !Guid.TryParse(i.EventId, out _)) return "Identificador UUID inválido.";
        if (!double.IsFinite(i.Latitude) || !double.IsFinite(i.Longitude)
            || i.Latitude < -90 || i.Latitude > 90 || i.Longitude < -180 || i.Longitude > 180
            || (i.Latitude == 0 && i.Longitude == 0))
            return "Coordenadas inválidas.";
        if (i.AffectedPeople < 0 || (int)i.Severity < 1 || (int)i.Severity > 4
            || (int)i.Source < 1 || (int)i.Source > 4 || (int)i.Status < 1 || (int)i.Status > 3
            || i.AffectationLevel < 0 || i.AffectationLevel > 5)
            return "Severidad, nivel, fuente, estado o personas afectadas inválidos.";
        if (i.ReportedAt is null) return "Falta reported_at.";
        if (i.DeviceId.Length > 64) return "El identificador del dispositivo supera 64 caracteres.";
        return null;
    }
}
