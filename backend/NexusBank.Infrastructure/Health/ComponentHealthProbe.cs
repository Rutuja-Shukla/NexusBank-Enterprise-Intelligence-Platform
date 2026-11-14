using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Enums;
using NexusBank.Infrastructure.Persistence;

namespace NexusBank.Infrastructure.Health;

/// <summary>
/// Every dependency is probed on demand. A dependency that cannot be reached is reported
/// as down/degraded — never assumed healthy.
/// </summary>
public sealed class ComponentHealthProbe : IComponentHealthProbe
{
    private readonly NexusBankDbContext _db;
    private readonly ICacheService _cache;
    private readonly IEventBus _bus;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ComponentHealthProbe> _logger;

    public ComponentHealthProbe(
        NexusBankDbContext db,
        ICacheService cache,
        IEventBus bus,
        WorkerHeartbeatRegistry heartbeats,
        IDateTimeProvider clock,
        ILogger<ComponentHealthProbe> logger)
    {
        _db = db;
        _cache = cache;
        _bus = bus;
        _heartbeats = heartbeats;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DependencyDto>> CheckComponentsAsync(CancellationToken cancellationToken = default)
    {
        var components = new List<DependencyDto>();

        var dbWatch = Stopwatch.StartNew();
        var dbHealthy = false;
        string dbDetail;
        try
        {
            dbHealthy = await _db.Database.CanConnectAsync(cancellationToken);
            dbDetail = dbHealthy
                ? $"Connected to {_db.Database.ProviderName?.Split('.').LastOrDefault()} in {dbWatch.ElapsedMilliseconds} ms"
                : "Database connection could not be established.";
        }
        catch (Exception ex)
        {
            dbDetail = $"Database probe failed: {ex.Message}";
            _logger.LogWarning(ex, "Database health probe failed");
        }
        dbWatch.Stop();

        components.Add(new DependencyDto
        {
            Name = "SQL Server",
            Type = "Database",
            Status = dbHealthy ? "healthy" : "down",
            Detail = dbDetail,
            LatencyMs = dbWatch.Elapsed.TotalMilliseconds,
            Mode = _db.Database.ProviderName
        });

        var cacheWatch = Stopwatch.StartNew();
        var cacheDistributed = _cache.IsDistributed;
        try
        {
            await _cache.SetAsync("health:probe", _clock.UtcNow.ToString("O"), TimeSpan.FromSeconds(30), cancellationToken);
            var read = await _cache.GetAsync<string>("health:probe", cancellationToken);
            cacheWatch.Stop();

            components.Add(new DependencyDto
            {
                Name = "Redis",
                Type = "Cache",
                Status = cacheDistributed ? "healthy" : "degraded",
                Detail = cacheDistributed
                    ? "Redis responded to a read/write probe."
                    : "Redis unreachable — the in-process memory cache is serving reads. Cache state is not shared across instances.",
                LatencyMs = cacheWatch.Elapsed.TotalMilliseconds,
                Mode = _cache.Mode
            });
            _ = read;
        }
        catch (Exception ex)
        {
            cacheWatch.Stop();
            components.Add(new DependencyDto
            {
                Name = "Redis",
                Type = "Cache",
                Status = "degraded",
                Detail = $"Cache probe failed: {ex.Message}",
                LatencyMs = cacheWatch.Elapsed.TotalMilliseconds,
                Mode = _cache.Mode
            });
        }

        components.Add(new DependencyDto
        {
            Name = "Apache Kafka",
            Type = "Message broker",
            Status = _bus.BrokerConnected ? "healthy" : "degraded",
            Detail = _bus.Mode,
            LatencyMs = 0,
            Mode = _bus.Mode
        });

        var hourAgo = _clock.UtcNow.AddHours(-1);
        var recent = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= hourAgo)
            .Select(l => new { l.StatusCode, l.DurationMs })
            .ToListAsync(cancellationToken);

        var errorRate = recent.Count == 0 ? 0 : recent.Count(r => r.StatusCode >= 500) * 100.0 / recent.Count;
        components.Add(new DependencyDto
        {
            Name = "NexusBank API",
            Type = "Application",
            Status = errorRate > 5 ? "down" : errorRate > 2 ? "degraded" : "healthy",
            Detail = recent.Count == 0
                ? "No requests observed in the last hour."
                : $"{recent.Count} requests in the last hour · {errorRate:0.00}% server errors · {recent.Average(r => r.DurationMs):0} ms mean",
            LatencyMs = recent.Count == 0 ? 0 : Math.Round(recent.Average(r => r.DurationMs), 1),
            Mode = "in-process"
        });

        var integrations = await _db.Integrations.AsNoTracking()
            .Select(i => new { i.Status, i.Enabled, i.LastSuccessAt })
            .ToListAsync(cancellationToken);
        var down = integrations.Count(i => i.Enabled && i.Status == IntegrationStatus.Down);
        var degraded = integrations.Count(i => i.Enabled && i.Status == IntegrationStatus.Degraded);

        components.Add(new DependencyDto
        {
            Name = "Integration mesh",
            Type = "Integrations",
            Status = down > 0 ? "down" : degraded > 0 ? "degraded" : "healthy",
            Detail = $"{integrations.Count(i => i.Enabled)} enabled · {degraded} degraded · {down} down",
            LatencyMs = 0,
            Mode = "probe"
        });

        return components;
    }

    public async Task<IReadOnlyList<DependencyDto>> CheckWorkersAsync(CancellationToken cancellationToken = default)
    {
        var beats = _heartbeats.Snapshot();
        if (beats.Count == 0)
        {
            return new[]
            {
                new DependencyDto
                {
                    Name = "Background workers",
                    Type = "Worker",
                    Status = "degraded",
                    Detail = "No worker heartbeat has been recorded yet. Workers report after their first cycle.",
                    LatencyMs = 0
                }
            };
        }

        var pendingOutbox = await _db.OutboxMessages.CountAsync(m => m.ProcessedAt == null, cancellationToken);
        var now = _clock.UtcNow;

        return beats.Select(b =>
        {
            var age = (now - b.LastRunAt).TotalSeconds;
            var status = b.ConsecutiveFailures >= 3 ? "down" : age > 180 ? "degraded" : "healthy";
            return new DependencyDto
            {
                Name = b.Name,
                Type = "Worker",
                Status = status,
                Detail = $"{b.Detail} · last cycle {age:0}s ago · {pendingOutbox} outbox message(s) pending",
                LatencyMs = 0,
                Mode = "hosted service"
            };
        }).ToList();
    }
}
