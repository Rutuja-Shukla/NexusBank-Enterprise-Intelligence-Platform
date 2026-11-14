using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexusBank.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Migrations target SQL Server (the production provider);
/// SQLite is used for local development and tests via EnsureCreated.
/// </summary>
public sealed class NexusBankDbContextFactory : IDesignTimeDbContextFactory<NexusBankDbContext>
{
    public NexusBankDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NexusBankDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=NexusBank;User Id=sa;Password=Local_Dev_Only_123;TrustServerCertificate=True;Encrypt=False",
                sql => sql.MigrationsAssembly(typeof(NexusBankDbContext).Assembly.FullName))
            .Options;

        return new NexusBankDbContext(options);
    }
}
