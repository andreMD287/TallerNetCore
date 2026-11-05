using DisasterMaps.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DisasterMaps.Data.Persistence;

public sealed class DisasterMapsDbContext(DbContextOptions<DisasterMapsDbContext> options) : DbContext(options)
{
    public DbSet<DisasterEventEntity> DisasterEvents => Set<DisasterEventEntity>();
    public DbSet<ImpactEntity> Impacts => Set<ImpactEntity>();

    protected override void OnModelCreating(ModelBuilder model) => ConfigureModel(model);

    public static void ConfigureModel(ModelBuilder model)
    {
        var events = model.Entity<DisasterEventEntity>();
        events.ToTable("disaster_events");
        events.HasKey(x => x.Id);
        events.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        events.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        events.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
        events.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
        events.Property(x => x.Description).HasColumnName("description");
        events.HasMany(x => x.Impacts).WithOne(x => x.Event).HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        var impacts = model.Entity<ImpactEntity>();
        impacts.ToTable("impacts", t =>
        {
            t.HasCheckConstraint("ck_impacts_latitude", "latitude BETWEEN -90 AND 90");
            t.HasCheckConstraint("ck_impacts_longitude", "longitude BETWEEN -180 AND 180");
            t.HasCheckConstraint("ck_impacts_not_zero", "NOT (latitude = 0 AND longitude = 0)");
            t.HasCheckConstraint("ck_impacts_people", "affected_people >= 0");
            t.HasCheckConstraint("ck_impacts_severity", "severity IN ('LOW','MEDIUM','HIGH','CRITICAL')");
            t.HasCheckConstraint("ck_impacts_status", "status IN ('REPORTED','VERIFIED','ATTENDED')");
            t.HasCheckConstraint("ck_impacts_level", "affectation_level BETWEEN 1 AND 5");
        });
        impacts.HasKey(x => x.Id);
        impacts.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        impacts.Property(x => x.EventId).HasColumnName("event_id");
        impacts.Property(x => x.Latitude).HasColumnName("latitude");
        impacts.Property(x => x.Longitude).HasColumnName("longitude");
        impacts.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(16).IsRequired();
        impacts.Property(x => x.AffectedPeople).HasColumnName("affected_people");
        impacts.Property(x => x.Municipality).HasColumnName("municipality").HasMaxLength(160);
        impacts.Property(x => x.Department).HasColumnName("department").HasMaxLength(160);
        impacts.Property(x => x.Description).HasColumnName("description");
        impacts.Property(x => x.ReportedAt).HasColumnName("reported_at").HasColumnType("timestamp with time zone");
        impacts.Property(x => x.Source).HasColumnName("source").HasMaxLength(16).IsRequired();
        impacts.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        impacts.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        impacts.Property(x => x.AffectationLevel).HasColumnName("affectation_level");
        impacts.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(64);
        impacts.Property(x => x.ReceivedAt).HasColumnName("received_at")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        impacts.HasQueryFilter(x => !x.IsDeleted);
        impacts.HasIndex(x => x.EventId).HasDatabaseName("ix_impacts_event_id");
        impacts.HasIndex(x => new { x.Latitude, x.Longitude }).HasDatabaseName("ix_impacts_coordinates");
        impacts.HasIndex(x => x.ReportedAt).HasDatabaseName("ix_impacts_reported_at");
        impacts.HasIndex(x => x.DeviceId).HasDatabaseName("ix_impacts_device_id");
    }
}
