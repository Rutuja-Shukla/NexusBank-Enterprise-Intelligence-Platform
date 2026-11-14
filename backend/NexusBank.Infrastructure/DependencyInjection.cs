using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Infrastructure.Caching;
using NexusBank.Infrastructure.Health;
using NexusBank.Infrastructure.Messaging;
using NexusBank.Infrastructure.Persistence;
using NexusBank.Infrastructure.Persistence.Seed;
using NexusBank.Infrastructure.Security;

namespace NexusBank.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddNexusInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));

        var provider = configuration["Database:Provider"] ?? "SqlServer";
        var sqliteConnection = configuration.GetConnectionString("Sqlite") ?? "Data Source=nexusbank.db";
        var sqlServerConnection = configuration.GetConnectionString("SqlServer")
            ?? "Server=localhost,1433;Database=NexusBank;User Id=sa;Password=Your_password123;TrustServerCertificate=True";

        services.AddDbContext<NexusBankDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(sqliteConnection);
            }
            else
            {
                options.UseSqlServer(sqlServerConnection, sql =>
                {
                    sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    sql.MigrationsAssembly(typeof(NexusBankDbContext).Assembly.FullName);
                });
            }
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<NexusBankDbContext>());

        services.AddMemoryCache();
        if (configuration.GetValue("Redis:Enabled", true))
        {
            var redisConfiguration = configuration["Redis:Configuration"] ?? "localhost:6379";
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConfiguration;
                options.InstanceName = "nexusbank:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, NexusCacheService>();
        services.AddSingleton<IEventBus, KafkaEventBus>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IApplicationInfo, ApplicationInfo>();
        services.AddSingleton<WorkerHeartbeatRegistry>();
        services.AddScoped<IComponentHealthProbe, ComponentHealthProbe>();

        services.AddScoped<IDatabaseSeeder, NexusBankSeeder>();

        return services;
    }
}
