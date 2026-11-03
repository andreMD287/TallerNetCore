namespace DisasterMaps.Data.Entities;

public sealed class ImpactEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public DisasterEventEntity Event { get; set; } = null!;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Severity { get; set; } = "LOW";
    public int AffectedPeople { get; set; }
    public string Municipality { get; set; } = "";
    public string Department { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime ReportedAt { get; set; }
    public string Source { get; set; } = "MANUAL";
    public string Status { get; set; } = "REPORTED";
    public bool IsDeleted { get; set; }
}
