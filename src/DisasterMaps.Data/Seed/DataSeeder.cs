using DisasterMaps.Data.Entities;
using DisasterMaps.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisasterMaps.Data.Seed;

public static class DataSeeder
{
    // Todos los puntos son DATOS SINTETICOS, no reportes oficiales.
    public static async Task SeedAsync(DisasterMapsDbContext db, CancellationToken ct = default)
    {
        if (await db.DisasterEvents.AnyAsync(ct)) return;
        var examples = new (string Name, string Type, string City, string Department, double Lat, double Lon, DateTime Started)[]
        {
            ("Mocoa - ejemplo", "LANDSLIDE", "Mocoa", "Putumayo", 1.1522, -76.6466, new DateTime(2017,4,1,0,0,0,DateTimeKind.Utc)),
            ("La Mojana - ejemplo", "FLOOD", "San Marcos", "Sucre", 8.6623, -75.1295, new DateTime(2025,5,1,0,0,0,DateTimeKind.Utc)),
            ("Providencia - ejemplo", "WINDSTORM", "Providencia", "San Andrés", 13.3489, -81.3740, new DateTime(2020,11,16,0,0,0,DateTimeKind.Utc)),
            ("Bogotá - ejemplo", "WILDFIRE", "Bogotá", "Bogotá D.C.", 4.6650, -74.0530, new DateTime(2024,1,22,0,0,0,DateTimeKind.Utc)),
            ("Eje Cafetero - ejemplo", "EARTHQUAKE", "Armenia", "Quindío", 4.5343, -75.6757, new DateTime(2023,5,1,0,0,0,DateTimeKind.Utc)),
        };
        var rng = new Random(42);
        foreach (var e in examples)
        {
            var evt = new DisasterEventEntity { Id = Guid.NewGuid(), Name = e.Name, Type = e.Type,
                StartedAt = e.Started, Description = "Datos de demostración sintéticos; no oficiales." };
            db.DisasterEvents.Add(evt);
            for (int j = 0; j < 8; j++)
            {
                var people = j * 17;
                db.Impacts.Add(new ImpactEntity
                {
                    Id = Guid.NewGuid(), EventId = evt.Id,
                    Latitude = Math.Round(e.Lat + (rng.NextDouble() - .5) * .12, 6),
                    Longitude = Math.Round(e.Lon + (rng.NextDouble() - .5) * .12, 6),
                    Severity = people == 0 ? "LOW" : people < 50 ? "MEDIUM" : "HIGH",
                    AffectedPeople = people, Municipality = e.City, Department = e.Department,
                    Description = "Punto simulado para demostración académica.",
                    ReportedAt = e.Started.AddHours(j + 1), Source = "MANUAL", Status = "REPORTED"
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
