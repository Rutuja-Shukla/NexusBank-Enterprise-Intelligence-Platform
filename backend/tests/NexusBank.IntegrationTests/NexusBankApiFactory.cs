using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NexusBank.IntegrationTests;

/// <summary>
/// Boots the production host (real middleware, auth, rate limiting and seeding) against a
/// throwaway SQLite file. Redis and Kafka are disabled so the test run needs no containers;
/// the health endpoint is asserted to report that degradation honestly.
/// </summary>
public sealed class NexusBankApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"nexusbank-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Host settings are visible before Program reads Jwt:SigningKey, so the token issuer
        // and the JWT bearer validation use the same symmetric key.
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-enough-entropy-2026");
        builder.UseSetting("Jwt:Issuer", "nexusbank-api");
        builder.UseSetting("Jwt:Audience", "nexusbank-console");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = $"Data Source={_databasePath}",
                ["Database:AutoMigrate"] = "true",
                ["Database:AutoSeed"] = "true",
                ["Redis:Enabled"] = "false",
                ["Kafka:Enabled"] = "false",
                ["RateLimiting:PermitLimit"] = "20000",
                ["RateLimiting:AuthPermitLimit"] = "2000"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A lingering SQLite handle is harmless for a throwaway test database.
        }
    }
}
