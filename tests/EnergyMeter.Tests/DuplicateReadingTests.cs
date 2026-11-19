using EnergyMeter.Api.Data;
using EnergyMeter.Api.Domain;
using EnergyMeter.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Tests;

public sealed class DuplicateReadingTests : IDisposable
{
    private static readonly DateTime Timestamp = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<(int MeterA, int MeterB)> SeedTwoMetersAsync()
    {
        await using var db = _database.CreateContext();
        var site = new Site { Name = "Test Site", CreatedUtc = DateTime.UtcNow };
        var a = new Meter { SerialNumber = "A", CreatedUtc = DateTime.UtcNow, Site = site };
        var b = new Meter { SerialNumber = "B", CreatedUtc = DateTime.UtcNow, Site = site };
        db.AddRange(site, a, b);
        await db.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task CreatingSecondReadingWithSameTimestampForSameMeter_IsRejected()
    {
        var (meterId, _) = await SeedTwoMetersAsync();

        await using (var db = _database.CreateContext())
        {
            await new ReadingService(db).CreateAsync(meterId, Timestamp, 100m);
        }

        await using (var db = _database.CreateContext())
        {
            var service = new ReadingService(db);
            var ex = await Assert.ThrowsAsync<DuplicateReadingException>(
                () => service.CreateAsync(meterId, Timestamp, 999m));

            Assert.Equal(meterId, ex.MeterId);
            Assert.Equal(Timestamp, ex.TimestampUtc);
        }

        // The original reading is untouched and no second row was written.
        await using (var db = _database.CreateContext())
        {
            var stored = await db.Readings.Where(r => r.MeterId == meterId).ToListAsync();
            var only = Assert.Single(stored);
            Assert.Equal(100m, only.ValueKwh);
        }
    }

    [Fact]
    public async Task DuplicateIsRejectedConsistently_OnRepeatedAttempts()
    {
        var (meterId, _) = await SeedTwoMetersAsync();

        await using var db = _database.CreateContext();
        var service = new ReadingService(db);
        await service.CreateAsync(meterId, Timestamp, 100m);

        // Same context, several retries: always the same exception, never a raw database error.
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<DuplicateReadingException>(() => service.CreateAsync(meterId, Timestamp, 100m + i));
        }

        Assert.Equal(1, await db.Readings.CountAsync(r => r.MeterId == meterId));
    }

    [Fact]
    public async Task SameTimestampOnDifferentMeters_IsAllowed()
    {
        var (meterA, meterB) = await SeedTwoMetersAsync();

        await using var db = _database.CreateContext();
        var service = new ReadingService(db);

        await service.CreateAsync(meterA, Timestamp, 100m);
        await service.CreateAsync(meterB, Timestamp, 200m);

        Assert.Equal(2, await db.Readings.CountAsync(r => r.TimestampUtc == Timestamp));
    }

    [Fact]
    public async Task DifferentTimestampsOnSameMeter_AreAllowed()
    {
        var (meterId, _) = await SeedTwoMetersAsync();

        await using var db = _database.CreateContext();
        var service = new ReadingService(db);

        await service.CreateAsync(meterId, Timestamp, 100m);
        await service.CreateAsync(meterId, Timestamp.AddSeconds(1), 100.5m);

        Assert.Equal(2, await db.Readings.CountAsync(r => r.MeterId == meterId));
    }

    [Fact]
    public async Task DatabaseUniqueIndex_RejectsDuplicate_EvenWhenServiceIsBypassed()
    {
        // Proves the rule does not depend on the service's pre-check: the schema enforces it.
        var (meterId, _) = await SeedTwoMetersAsync();

        await using var db = _database.CreateContext();
        db.Readings.Add(new Reading { MeterId = meterId, TimestampUtc = Timestamp, ValueKwh = 1m });
        db.Readings.Add(new Reading { MeterId = meterId, TimestampUtc = Timestamp, ValueKwh = 2m });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReadingForUnknownMeter_ThrowsNotFound()
    {
        await using var db = _database.CreateContext();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new ReadingService(db).CreateAsync(meterId: 12345, Timestamp, 1m));
    }

    [Fact]
    public void Model_DeclaresUniqueIndexOnMeterIdAndTimestamp()
    {
        // Checks the EF model that the SQL Server migration is generated from. No database needed.
        var options = new DbContextOptionsBuilder<EnergyMeterDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;
        using var db = new EnergyMeterDbContext(options);

        var entity = db.Model.FindEntityType(typeof(Reading))!;
        var index = Assert.Single(entity.GetIndexes(), i => i.IsUnique);

        Assert.Equal(
            new[] { nameof(Reading.MeterId), nameof(Reading.TimestampUtc) },
            index.Properties.Select(p => p.Name).ToArray());
    }
}
