using EnergyMeter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Data;

/// <summary>
/// Optional demo data (Database:SeedDemoData = true). One site, one meter, readings over two UTC days.
/// Runs only when the database has no sites, so it never touches real data.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(EnergyMeterDbContext db, CancellationToken ct = default)
    {
        if (await db.Sites.AnyAsync(ct))
        {
            return;
        }

        var site = new Site { Name = "Demo Site", CreatedUtc = DateTime.UtcNow };
        var meter = new Meter { SerialNumber = "DEMO-0001", CreatedUtc = DateTime.UtcNow, Site = site };

        static DateTime Utc(int day, int hour) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

        meter.Readings.AddRange(new[]
        {
            new Reading { TimestampUtc = Utc(1, 0), ValueKwh = 12500.0m },
            new Reading { TimestampUtc = Utc(1, 8), ValueKwh = 12512.5m },
            new Reading { TimestampUtc = Utc(1, 18), ValueKwh = 12530.0m },
            new Reading { TimestampUtc = Utc(2, 6), ValueKwh = 12543.7m },
            new Reading { TimestampUtc = Utc(2, 18), ValueKwh = 12561.2m },
        });

        db.Sites.Add(site);
        db.Meters.Add(meter);
        await db.SaveChangesAsync(ct);
    }
}
