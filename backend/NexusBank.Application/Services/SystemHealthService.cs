using Microsoft.EntityFrameworkCore;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Enums;

namespace NexusBank.Application.Services;

/// <summary>
/// Composes the system health view from live probes. Component status is never
/// hardcoded: each dependency reports its own result.
/// </summary>
public sealed class SystemHealthService
{
    private readonly IApplicationDbContext _db;
    private readonly IComponentHealthProbe _probe;
    private readonly IApplicationInfo _appInfo;
    private readonly IDateTimeProvider _clock;

    public SystemHealthService(
        IApplicationDbContext db,
        IComponentHealthProbe probe,
        IApplicationInfo appInfo,
        IDateTimeProvider clock)
    {
        _db = db;
        _probe = probe;
        _appInfo = appInfo;
        _clock = clock;
    }

    public async Task<SystemHealthDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var components = await _probe.CheckComponentsAsync(cancellationToken);
        var workers = await _probe.CheckWorkersAsync(cancellationToken);
        var now = _clock.UtcNow;
        var dayAgo = now.AddHours(-24);

        var logs = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= dayAgo)
            .Select(l => new { l.StatusCode, l.DurationMs })
            .ToListAsync(cancellationToken);

        var serverErrors = logs.Count(l => l.StatusCode >= 500);
        var availability = logs.Count == 0 ? 100 : Math.Round(100 - serverErrors * 100.0 / logs.Count, 3);

        var failures = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= dayAgo && l.StatusCode >= 500)
            .OrderByDescending(l => l.Timestamp)
            .Take(6)
            .ToListAsync(cancellationToken);

        var auditFailures = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Timestamp >= dayAgo && a.Outcome != AuditOutcome.Success)
            .OrderByDescending(a => a.Timestamp)
            .Take(4)
            .ToListAsync(cancellationToken);

        var recentFailures = failures.Select(f => new ActivityItemDto
        {
            Id = f.Id.ToString(),
            Kind = "api",
            Title = $"{f.Method} {f.Path} → {f.StatusCode}",
            Detail = $"{f.DurationMs:0.0} ms · correlation {(f.CorrelationId ?? "n/a")}",
            Tone = "danger",
            Timestamp = f.Timestamp,
            LinkTo = "/observability"
        }).Concat(auditFailures.Select(a => new ActivityItemDto
        {
            Id = a.Id.ToString(),
            Kind = "security",
            Title = a.Action,
            Detail = a.Metadata ?? a.EntityType,
            Tone = a.Outcome == AuditOutcome.Denied ? "warning" : "danger",
            Timestamp = a.Timestamp,
            LinkTo = "/security"
        })).OrderByDescending(f => f.Timestamp).Take(8).ToList();

        // Only the database and the API itself can make the platform unhealthy. A degraded
        // cache, broker or downstream integration degrades throughput but the platform still serves.
        var criticalDown = components.Any(c => c.Type is "Database" or "Application" && c.Status == "down");
        var anyDegraded = components.Any(c => c.Status is "down" or "degraded")
                          || workers.Any(w => w.Status != "healthy");
        var overall = criticalDown ? "unhealthy" : anyDegraded ? "degraded" : "healthy";

        return new SystemHealthDto
        {
            OverallStatus = overall,
            CheckedAt = now,
            UptimeSeconds = Math.Max(0, (long)(now - _appInfo.StartedAt).TotalSeconds),
            Version = _appInfo.Version,
            Environment = _appInfo.EnvironmentName,
            DatabaseConnected = components.Any(c => c.Type == "Database" && c.Status == "healthy"),
            CacheMode = components.FirstOrDefault(c => c.Type == "Cache")?.Mode ?? "unknown",
            EventBusMode = components.FirstOrDefault(c => c.Type == "Message broker")?.Mode ?? "unknown",
            Components = components,
            Workers = workers,
            RecentFailures = recentFailures,
            AvailabilityPct = availability,
            DeadLetterCount = await _db.KafkaEvents.CountAsync(e => e.Status == KafkaEventStatus.DeadLettered, cancellationToken)
        };
    }

    /// <summary>Prometheus scrape endpoint content, derived from live measurements.</summary>
    public async Task<string> GetPrometheusMetricsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var hourAgo = now.AddHours(-1);
        var logs = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= hourAgo)
            .Select(l => new { l.StatusCode, l.DurationMs, l.Method })
            .ToListAsync(cancellationToken);

        var duration = (now - _appInfo.StartedAt).TotalSeconds;
        var components = await _probe.CheckComponentsAsync(cancellationToken);

        var lines = new List<string>
        {
            "# HELP nexusbank_api_requests_last_hour API requests observed in the last hour.",
            "# TYPE nexusbank_api_requests_last_hour gauge",
            $"nexusbank_api_requests_last_hour {logs.Count}",
            "# HELP nexusbank_api_errors_last_hour 5xx responses observed in the last hour.",
            "# TYPE nexusbank_api_errors_last_hour gauge",
            $"nexusbank_api_errors_last_hour {logs.Count(l => l.StatusCode >= 500)}",
            "# HELP nexusbank_api_latency_avg_ms Mean request duration in the last hour.",
            "# TYPE nexusbank_api_latency_avg_ms gauge",
            $"nexusbank_api_latency_avg_ms {(logs.Count == 0 ? 0 : Math.Round(logs.Average(l => l.DurationMs), 2))}",
            "# HELP nexusbank_uptime_seconds Process uptime in seconds.",
            "# TYPE nexusbank_uptime_seconds counter",
            $"nexusbank_uptime_seconds {duration:0}",
            "# HELP nexusbank_component_up Component health (1 healthy, 0.5 degraded, 0 down).",
            "# TYPE nexusbank_component_up gauge"
        };

        foreach (var component in components)
        {
            var value = component.Status == "healthy" ? 1 : component.Status == "degraded" ? 0.5 : 0;
            var name = component.Name.ToLowerInvariant().Replace(' ', '_').Replace("-", "_");
            lines.Add($"nexusbank_component_up{{component=\"{name}\",type=\"{component.Type}\"}} {value}");
        }

        return string.Join('\n', lines) + "\n";
    }
}
