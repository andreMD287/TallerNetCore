using System.ComponentModel.DataAnnotations;

namespace DisasterMaps.Web.Models;

public sealed class EventForm
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    public string Name { get; set; } = "";
    public string Type { get; set; } = "FLOOD";
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = "";
}
public sealed class ImpactForm
{
    [Required(ErrorMessage = "Selecciona un evento.")]
    public string EventId { get; set; } = "";
    [Range(-90, 90, ErrorMessage = "La latitud debe estar entre -90 y 90.")]
    public double Latitude { get; set; }
    [Range(-180, 180, ErrorMessage = "La longitud debe estar entre -180 y 180.")]
    public double Longitude { get; set; }
    [Range(0, int.MaxValue, ErrorMessage = "El número de personas no puede ser negativo.")]
    public int AffectedPeople { get; set; }
    public string? Severity { get; set; }
    public string Municipality { get; set; } = "";
    public string Department { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "MANUAL";
}
