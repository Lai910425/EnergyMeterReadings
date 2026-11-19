using EnergyMeter.Api.Contracts;
using EnergyMeter.Api.Data;
using EnergyMeter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Api.Services;

public interface ISiteService
{
    Task<SiteResponse> CreateAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<SiteResponse>> ListAsync(CancellationToken ct = default);
    Task<SiteResponse> GetAsync(int siteId, CancellationToken ct = default);
}

public sealed class SiteService : ISiteService
{
    private readonly EnergyMeterDbContext _db;
    private readonly TimeProvider _clock;

    public SiteService(EnergyMeterDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SiteResponse> CreateAsync(string name, CancellationToken ct = default)
    {
        var site = new Site
        {
            Name = name.Trim(),
            CreatedUtc = _clock.GetUtcNow().UtcDateTime
        };

        _db.Sites.Add(site);
        await _db.SaveChangesAsync(ct);
        return ToResponse(site);
    }

    public async Task<IReadOnlyList<SiteResponse>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Sites
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new SiteResponse(s.Id, s.Name, s.CreatedUtc))
            .ToListAsync(ct);
    }

    public async Task<SiteResponse> GetAsync(int siteId, CancellationToken ct = default)
    {
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId, ct)
                   ?? throw new NotFoundException("Site", siteId);
        return ToResponse(site);
    }

    private static SiteResponse ToResponse(Site s) => new(s.Id, s.Name, s.CreatedUtc);
}
