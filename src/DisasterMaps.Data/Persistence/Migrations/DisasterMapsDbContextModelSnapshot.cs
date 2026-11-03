using DisasterMaps.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DisasterMaps.Data.Persistence.Migrations;

[DbContext(typeof(DisasterMapsDbContext))]
public sealed class DisasterMapsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        DisasterMapsDbContext.ConfigureModel(modelBuilder);
    }
}
