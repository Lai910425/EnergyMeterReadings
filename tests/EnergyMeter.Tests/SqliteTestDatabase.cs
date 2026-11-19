using EnergyMeter.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EnergyMeter.Tests;

/// <summary>
/// A real relational database in memory (SQLite) so unique indexes and foreign keys are enforced.
/// The database lives as long as the open connection, i.e. for the lifetime of one test.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EnergyMeterDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<EnergyMeterDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>Each call returns a fresh context (no shared change tracker) over the same database.</summary>
    public EnergyMeterDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
