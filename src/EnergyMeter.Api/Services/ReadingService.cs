using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Data;
using EnergyMeter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Services;

public interface IReadingService
{
    Task<ReadingResponse> CreateAsync(int meterId, DateTime timestampUtc, decimal valueKwh, CancellationToken ct = default);
    Task<IReadOnlyList<ReadingResponse>> ListAsync(int meterId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
    Task<ConsumptionSummaryResponse> GetSummaryAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

public sealed class ReadingService : IReadingService
{
    private readonly EnergyMeterDbContext _db;

    public ReadingService(EnergyMeterDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Enforces "one reading per meter per timestamp" in two layers:
    /// 1. A pre-check, so the common case returns a clean 409 without relying on a database error.
    /// 2. The unique index (MeterId, TimestampUtc). If two requests race past the pre-check, the
    ///    database rejects the second insert; we confirm it was a duplicate and raise the same exception,
    ///    so clients see the same 409 either way.
    /// </summary>
    public async Task<ReadingResponse> CreateAsync(int meterId, DateTime timestampUtc, decimal valueKwh, CancellationToken ct = default)
    {
        await EnsureMeterExistsAsync(meterId, ct);
        var timestamp = EnsureUtc(timestampUtc);

        if (await ExistsAsync(meterId, timestamp, ct))
        {
            throw new DuplicateReadingException(meterId, timestamp);
        }

        var reading = new Reading { MeterId = meterId, TimestampUtc = timestamp, ValueKwh = valueKwh };
        _db.Readings.Add(reading);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Don't leave the failed entity tracked on this context.
            _db.Entry(reading).State = EntityState.Detached;

            // Checking the data rather than parsing provider-specific error numbers (SQL Server 2601/2627)
            // keeps this correct on any provider, including SQLite in the tests.
            if (await ExistsAsync(meterId, timestamp, ct))
            {
                throw new DuplicateReadingException(meterId, timestamp, ex);
            }

            throw;
        }

        return ToResponse(reading);
    }

    public async Task<IReadOnlyList<ReadingResponse>> ListAsync(int meterId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        await EnsureMeterExistsAsync(meterId, ct);

        var query = _db.Readings.AsNoTracking().Where(r => r.MeterId == meterId);

        if (fromUtc is { } fromValue)
        {
            var f = EnsureUtc(fromValue);
            query = query.Where(r => r.TimestampUtc >= f);
        }

        if (toUtc is { } toValue)
        {
            var t = EnsureUtc(toValue);
            query = query.Where(r => r.TimestampUtc <= t);
        }

        return await query
            .OrderBy(r => r.TimestampUtc)
            .Select(r => new ReadingResponse(r.Id, r.MeterId, r.TimestampUtc, r.ValueKwh))
            .ToListAsync(ct);
    }

    public async Task<ConsumptionSummaryResponse> GetSummaryAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await EnsureMeterExistsAsync(meterId, ct);

        var rangeFrom = EnsureUtc(fromUtc);
        var rangeTo = EnsureUtc(toUtc);

        // Filter in SQL (served by the unique index), calculate in memory with the pure calculator.
        var points = await _db.Readings
            .AsNoTracking()
            .Where(r => r.MeterId == meterId && r.TimestampUtc >= rangeFrom && r.TimestampUtc <= rangeTo)
            .OrderBy(r => r.TimestampUtc)
            .Select(r => new ReadingPoint(r.TimestampUtc, r.ValueKwh))
            .ToListAsync(ct);

        var summary = ConsumptionCalculator.Calculate(points, rangeFrom, rangeTo);

        return new ConsumptionSummaryResponse(
            meterId,
            rangeFrom,
            rangeTo,
            summary.ReadingCount,
            summary.TotalConsumptionKwh,
            summary.DecreaseDetected,
            summary.Daily.Select(d => new DailyConsumptionResponse(d.DateUtc, d.ConsumptionKwh)).ToList());
    }

    private Task<bool> ExistsAsync(int meterId, DateTime timestampUtc, CancellationToken ct)
        => _db.Readings.AnyAsync(r => r.MeterId == meterId && r.TimestampUtc == timestampUtc, ct);

    private async Task EnsureMeterExistsAsync(int meterId, CancellationToken ct)
    {
        if (!await _db.Meters.AnyAsync(m => m.Id == meterId, ct))
        {
            throw new NotFoundException("Meter", meterId);
        }
    }

    // The HTTP layer already guarantees UTC; this keeps the service safe if it is called from elsewhere.
    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static ReadingResponse ToResponse(Reading r) => new(r.Id, r.MeterId, r.TimestampUtc, r.ValueKwh);
}
