using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusBank.Infrastructure.Persistence;

namespace NexusBank.Tests.Support;

/// <summary>
/// An isolated SQLite database per test. The production provider is SQL Server; the model,
/// conventions and queries are identical, so service behaviour is exercised for real.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<NexusBankDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;

        Db = new NexusBankDbContext(options);
        Db.Database.EnsureCreated();
    }

    public NexusBankDbContext Db { get; }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
