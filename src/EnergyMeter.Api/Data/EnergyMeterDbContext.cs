using EnergyMeter.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EnergyMeter.Api.Data;

public class EnergyMeterDbContext : DbContext
{
    public EnergyMeterDbContext(DbContextOptions<EnergyMeterDbContext> options) : base(options)
    {
    }

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQL Server datetime2 has no time-zone information, so values come back as DateTimeKind.Unspecified.
        // Everything in this system is UTC, so mark values as UTC on the way out of the database.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        modelBuilder.Entity<Site>(b =>
        {
            b.ToTable("Sites");
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).IsRequired().HasMaxLength(200);
            b.Property(s => s.CreatedUtc).HasConversion(utcConverter);

            b.HasMany(s => s.Meters)
                .WithOne(m => m.Site)
                .HasForeignKey(m => m.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Meter>(b =>
        {
            b.ToTable("Meters");
            b.HasKey(m => m.Id);
            b.Property(m => m.SerialNumber).IsRequired().HasMaxLength(100);
            b.Property(m => m.CreatedUtc).HasConversion(utcConverter);

            b.HasMany(m => m.Readings)
                .WithOne(r => r.Meter)
                .HasForeignKey(r => r.MeterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Reading>(b =>
        {
            b.ToTable("Readings");
            b.HasKey(r => r.Id);
            b.Property(r => r.TimestampUtc).HasConversion(utcConverter);
            // Cumulative register values: 15 integer digits, 3 decimals (Wh resolution).
            b.Property(r => r.ValueKwh).HasPrecision(18, 3);

            // Uniqueness rule: for a given meter, each reading timestamp must be unique.
            // This index is the source of truth; it also serves the (MeterId, time range) queries.
            b.HasIndex(r => new { r.MeterId, r.TimestampUtc }).IsUnique();
        });
    }
}
