using DisasterMaps.Data.Entities;
using DisasterMaps.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisasterMaps.Data.Tests;

public sealed class PersistenceModelTests
{
    private static DisasterMapsDbContext Context() => new(
        new DbContextOptionsBuilder<DisasterMapsDbContext>()
            .UseNpgsql("Host=localhost;Database=disastermaps;Username=postgres;Password=postgres")
            .Options);

    [Fact]
    public void DisasterEvent_uses_disaster_events_table()
    {
        using var db = Context();
        Assert.Equal("disaster_events", db.Model.FindEntityType(typeof(DisasterEventEntity))?.GetTableName());
    }

    [Fact]
    public void Impact_has_soft_delete_global_filter()
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(typeof(ImpactEntity));
        Assert.NotNull(entity?.GetQueryFilter());
    }

    [Fact]
    public void Impact_has_event_and_coordinates_indexes()
    {
        using var db = Context();
        var entity = db.Model.FindEntityType(typeof(ImpactEntity));
        Assert.NotNull(entity);
        var indexNames = entity.GetIndexes().Select(x => x.GetDatabaseName()).ToArray();
        Assert.Contains("ix_impacts_event_id", indexNames);
        Assert.Contains("ix_impacts_coordinates", indexNames);
    }
}
