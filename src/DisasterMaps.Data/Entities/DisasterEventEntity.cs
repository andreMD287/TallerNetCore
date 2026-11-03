namespace DisasterMaps.Data.Entities;

public sealed class DisasterEventEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public string Description { get; set; } = "";
    public List<ImpactEntity> Impacts { get; set; } = new();
}
