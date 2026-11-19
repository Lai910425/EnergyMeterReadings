using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Data;
using EnergyMeter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Services;

public interface IMeterService
{
    Task<MeterResponse> CreateAsync(int siteId, string serialNumber, CancellationToken ct = default);
    Task<IReadOnlyList<MeterResponse>> ListForSiteAsync(int siteId, CancellationToken ct = default);
    Task<MeterResponse> GetAsync(int meterId, CancellationToken ct = default);
}

public sealed class MeterService : IMeterService
{
    private readonly EnergyMeterDbContext _db;
    private readonly TimeProvider _clock;

    public MeterService(EnergyMeterDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<MeterResponse> CreateAsync(int siteId, string serialNumber, CancellationToken ct = default)
    {
        await EnsureSiteExistsAsync(siteId, ct);

        var meter = new Meter
        {
            SiteId = siteId,
            SerialNumber = serialNumber.Trim(),
            CreatedUtc = _clock.GetUtcNow().UtcDateTime
        };

        _db.Meters.Add(meter);
        await _db.SaveChangesAsync(ct);
        return ToResponse(meter);
    }

    public async Task<IReadOnlyList<MeterResponse>> ListForSiteAsync(int siteId, CancellationToken ct = default)
    {
        await EnsureSiteExistsAsync(siteId, ct);

        return await _db.Meters
            .AsNoTracking()
            .Where(m => m.SiteId == siteId)
            .OrderBy(m => m.Id)
            .Select(m => new MeterResponse(m.Id, m.SiteId, m.SerialNumber, m.CreatedUtc))
            .ToListAsync(ct);
    }

    public async Task<MeterResponse> GetAsync(int meterId, CancellationToken ct = default)
    {
        var meter = await _db.Meters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == meterId, ct)
                    ?? throw new NotFoundException("Meter", meterId);
        return ToResponse(meter);
    }

    // An empty list and "site does not exist" are different answers; the client should get a 404 for the latter.
    private async Task EnsureSiteExistsAsync(int siteId, CancellationToken ct)
    {
        if (!await _db.Sites.AnyAsync(s => s.Id == siteId, ct))
        {
            throw new NotFoundException("Site", siteId);
        }
    }

    private static MeterResponse ToResponse(Meter m) => new(m.Id, m.SiteId, m.SerialNumber, m.CreatedUtc);
}
