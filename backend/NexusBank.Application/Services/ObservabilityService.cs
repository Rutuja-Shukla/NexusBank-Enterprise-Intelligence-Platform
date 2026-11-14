using Microsoft.EntityFrameworkCore;
using NexusBank.Application.Common;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;

namespace NexusBank.Application.Services;

/// <summary>
/// All observability numbers are computed from <see cref="ApiRequestLog"/> rows captured
/// by the API middleware, plus live dependency probes. Nothing here is hardcoded.
/// </summary>
public sealed class ObservabilityService
{
    private readonly IApplicationDbContext _db;
    private readonly IComponentHealthProbe _probe;
    private readonly IEventBus _bus;
    private readonly ICacheService _cache;
    private readonly IApplicationInfo _appInfo;
    private readonly IDateTimeProvider _clock;

    public ObservabilityService(
        IApplicationDbContext db,
        IComponentHealthProbe probe,
        IEventBus bus,
        ICacheService cache,
        IApplicationInfo appInfo,
        IDateTimeProvider clock)
    {
        _db = db;
        _probe = probe;
        _bus = bus;
        _cache = cache;
        _appInfo = appInfo;
        _clock = clock;
    }

    public async Task<ObservabilityOverviewDto> GetOverviewAsync(int hours, CancellationToken cancellationToken = default)
    {
        var since = _clock.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        var rows = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= since)
            .Select(l => new { l.StatusCode, l.DurationMs, l.IsError })
            .ToListAsync(cancellationToken);

        var components = await _probe.CheckComponentsAsync(cancellationToken);
        var incidents = await _db.Incidents.CountAsync(i => i.Status != IncidentStatus.Resolved, cancellationToken);
        var serverErrors = rows.Count(r => r.StatusCode >= 500);

        return new ObservabilityOverviewDto
        {
            RequestVolume24h = rows.Count,
            AvgLatencyMs = rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.DurationMs), 1),
            P95LatencyMs = Math.Round(TimeBuckets.Percentile(rows.Select(r => r.DurationMs).ToList(), 95), 1),
            ErrorRatePct = rows.Count == 0 ? 0 : Math.Round(serverErrors * 100.0 / rows.Count, 2),
            AvailabilityPct = rows.Count == 0 ? 100 : Math.Round(100 - serverErrors * 100.0 / rows.Count, 3),
            ActiveIncidents = incidents,
            ServicesUp = components.Count(c => c.Status == "healthy"),
            ServicesDown = components.Count(c => c.Status is "down" or "unhealthy"),
            UptimeSeconds = Math.Max(0, (long)(_clock.UtcNow - _appInfo.StartedAt).TotalSeconds),
            GeneratedAt = _clock.UtcNow,
            Dependencies = components
        };
    }

    public async Task<IReadOnlyList<TrafficPointDto>> GetTrafficAsync(int hours, CancellationToken cancellationToken = default)
    {
        hours = Math.Clamp(hours, 6, 168);
        var buckets = TimeBuckets.Hourly(_clock.UtcNow, hours);
        var from = buckets[0].Start;

        var rows = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= from)
            .Select(l => new { l.Timestamp, l.StatusCode, l.DurationMs })
            .ToListAsync(cancellationToken);

        return buckets.Select(b =>
        {
            var inBucket = rows.Where(r => r.Timestamp >= b.Start && r.Timestamp < b.End).ToList();
            var durations = inBucket.Select(r => r.DurationMs).ToList();
            return new TrafficPointDto
            {
                Bucket = b.Label,
                Timestamp = b.Start,
                Requests = inBucket.Count,
                Errors = inBucket.Count(r => r.StatusCode >= 500),
                AvgLatencyMs = durations.Count == 0 ? 0 : Math.Round(durations.Average(), 1),
                P95LatencyMs = Math.Round(TimeBuckets.Percentile(durations, 95), 1)
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<LatencyDistributionDto>> GetLatencyDistributionAsync(int hours, CancellationToken cancellationToken = default)
    {
        var since = _clock.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        var durations = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= since)
            .Select(l => l.DurationMs)
            .ToListAsync(cancellationToken);

        var bands = new (string Label, double Min, double Max)[]
        {
            ("0–50 ms", 0, 50),
            ("50–100 ms", 50, 100),
            ("100–250 ms", 100, 250),
            ("250–500 ms", 250, 500),
            ("500 ms–1 s", 500, 1000),
            ("> 1 s", 1000, double.MaxValue)
        };

        return bands.Select(b => new LatencyDistributionDto
        {
            Bucket = b.Label,
            Count = durations.Count(d => d >= b.Min && d < b.Max)
        }).ToList();
    }

    public async Task<IReadOnlyList<ErrorGroupDto>> GetErrorsAsync(int hours, int limit, CancellationToken cancellationToken = default)
    {
        var since = _clock.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        var rows = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= since && l.StatusCode >= 400)
            .Select(l => new { l.Path, l.Method, l.StatusCode, l.DurationMs, l.Timestamp })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => new { r.Path, r.Method, r.StatusCode })
            .Select(g => new ErrorGroupDto
            {
                Path = g.Key.Path,
                Method = g.Key.Method,
                StatusCode = g.Key.StatusCode,
                Count = g.Count(),
                AvgLatencyMs = Math.Round(g.Average(r => r.DurationMs), 1),
                LastSeenAt = g.Max(r => r.Timestamp)
            })
            .OrderByDescending(g => g.Count)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    public async Task<IReadOnlyList<TraceDto>> GetTracesAsync(int limit, bool errorsOnly, CancellationToken cancellationToken = default)
    {
        var query = _db.ApiRequestLogs.AsNoTracking().AsQueryable();
        if (errorsOnly) query = query.Where(l => l.IsError || l.StatusCode >= 400);

        var rows = await query.OrderByDescending(l => l.Timestamp).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);
        return rows.Select(l => new TraceDto
        {
            CorrelationId = l.CorrelationId ?? $"req_{l.Id}",
            TraceId = l.TraceId,
            Method = l.Method,
            Path = l.Path,
            StatusCode = l.StatusCode,
            DurationMs = Math.Round(l.DurationMs, 1),
            Timestamp = l.Timestamp,
            IsError = l.IsError || l.StatusCode >= 400,
            ServiceName = l.ServiceName
        }).ToList();
    }

    public async Task<IReadOnlyList<LogEntryDto>> GetLogsAsync(int limit, string? level, string? search, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 300);
        var requestQuery = _db.ApiRequestLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            requestQuery = requestQuery.Where(l => l.Path.ToLower().Contains(term) || l.Method.ToLower().Contains(term));
        }

        var requests = await requestQuery.OrderByDescending(l => l.Timestamp).Take(limit).ToListAsync(cancellationToken);
        var auditRows = await _db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp).Take(limit).ToListAsync(cancellationToken);

        var entries = new List<LogEntryDto>();
        entries.AddRange(requests.Select(l => new LogEntryDto
        {
            Id = l.Id,
            Timestamp = l.Timestamp,
            Level = l.StatusCode >= 500 ? "error" : l.StatusCode >= 400 ? "warn" : "info",
            Source = "api",
            Message = $"{l.Method} {l.Path} responded {l.StatusCode} in {l.DurationMs:0.0} ms",
            CorrelationId = l.CorrelationId,
            TraceId = l.TraceId,
            Path = l.Path,
            StatusCode = l.StatusCode,
            DurationMs = Math.Round(l.DurationMs, 1)
        }));

        entries.AddRange(auditRows.Select(a => new LogEntryDto
        {
            Id = 0,
            Timestamp = a.Timestamp,
            Level = a.Outcome == AuditOutcome.Success ? (a.Severity == "info" ? "info" : "warn") : "error",
            Source = "audit",
            Message = $"{a.Action} · {a.Metadata ?? a.EntityType}",
            CorrelationId = a.CorrelationId,
            Path = a.EntityType
        }));

        if (!string.IsNullOrWhiteSpace(level) && level != "all")
            entries = entries.Where(e => e.Level == level).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            entries = entries.Where(e => e.Message.ToLower().Contains(term) || (e.Path?.ToLower().Contains(term) ?? false)).ToList();
        }

        return entries.OrderByDescending(e => e.Timestamp).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<OperationalAlertDto>> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var alerts = new List<OperationalAlertDto>();
        var integrations = await _db.Integrations.AsNoTracking().Where(i => i.Enabled).ToListAsync(cancellationToken);
        var incidents = await _db.Incidents.AsNoTracking()
            .Where(i => i.Status != IncidentStatus.Resolved)
            .OrderBy(i => i.Severity)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var integration in integrations.Where(i => i.Status is IntegrationStatus.Down or IntegrationStatus.Degraded))
        {
            alerts.Add(new OperationalAlertDto
            {
                Id = $"integration-{integration.Id}",
                Title = $"{integration.Name} is {integration.Status}",
                Detail = integration.LastFailureReason ?? $"Failure rate {integration.FailureRatePct:0.0}% across {integration.RequestCount24h} calls in the last 24h.",
                Severity = integration.Status == IntegrationStatus.Down ? "critical" : "warning",
                Source = "Integration monitor",
                DetectedAt = integration.LastFailureAt ?? now,
                LinkTo = $"/integrations/{integration.Id}"
            });
        }

        var since = now.AddHours(-1);
        var recent = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= since)
            .Select(l => new { l.StatusCode })
            .ToListAsync(cancellationToken);
        if (recent.Count > 20)
        {
            var errorRate = recent.Count(r => r.StatusCode >= 500) * 100.0 / recent.Count;
            if (errorRate > 2)
            {
                alerts.Add(new OperationalAlertDto
                {
                    Id = "api-error-rate",
                    Title = $"API error rate at {errorRate:0.0}%",
                    Detail = $"{recent.Count(r => r.StatusCode >= 500)} server errors in the last hour across {recent.Count} requests. SLO threshold is 2%.",
                    Severity = errorRate > 5 ? "critical" : "warning",
                    Source = "API gateway",
                    DetectedAt = now,
                    LinkTo = "/observability"
                });
            }
        }

        var deadLetters = await _db.KafkaEvents.CountAsync(e => e.Status == KafkaEventStatus.DeadLettered, cancellationToken);
        if (deadLetters > 0)
        {
            alerts.Add(new OperationalAlertDto
            {
                Id = "dlq-depth",
                Title = $"{deadLetters} message(s) in the dead-letter queue",
                Detail = "Poison messages require triage. Replay from the event stream workspace once the consumer defect is fixed.",
                Severity = deadLetters > 5 ? "critical" : "warning",
                Source = "Event streaming",
                DetectedAt = now,
                LinkTo = "/events?tab=dead-letters"
            });
        }

        foreach (var incident in incidents.Where(i => i.Severity is IncidentSeverity.Sev1 or IncidentSeverity.Sev2))
        {
            alerts.Add(new OperationalAlertDto
            {
                Id = $"incident-{incident.Id}",
                Title = $"{incident.Severity} · {incident.Title}",
                Detail = incident.ImpactSummary ?? incident.Description,
                Severity = incident.Severity == IncidentSeverity.Sev1 ? "critical" : "warning",
                Source = "Incident management",
                DetectedAt = incident.DetectedAt,
                LinkTo = $"/incidents/{incident.Id}"
            });
        }

        if (!_bus.BrokerConnected)
        {
            alerts.Add(new OperationalAlertDto
            {
                Id = "event-bus-fallback",
                Title = "Kafka broker unreachable",
                Detail = _bus.Mode,
                Severity = "info",
                Source = "Event streaming",
                DetectedAt = now,
                LinkTo = "/events"
            });
        }

        if (!_cache.IsDistributed)
        {
            alerts.Add(new OperationalAlertDto
            {
                Id = "cache-fallback",
                Title = "Redis cache unavailable",
                Detail = _cache.Mode,
                Severity = "info",
                Source = "Platform",
                DetectedAt = now,
                LinkTo = "/health"
            });
        }

        return alerts.OrderBy(a => a.Severity == "critical" ? 0 : a.Severity == "warning" ? 1 : 2)
            .ThenByDescending(a => a.DetectedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<ServiceNodeDto>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        var integrations = await _db.Integrations.AsNoTracking().ToListAsync(cancellationToken);
        var incidents = await _db.Incidents.AsNoTracking()
            .Where(i => i.Status != IncidentStatus.Resolved)
            .Select(i => i.AffectedService)
            .ToListAsync(cancellationToken);

        return integrations.Select(i => new ServiceNodeDto
        {
            Id = i.Id,
            Name = i.Name,
            Status = i.Status,
            LatencyMs = Math.Round(i.AvgLatencyMs, 1),
            ErrorRatePct = Math.Round(i.FailureRatePct, 2),
            RequestCount24h = i.RequestCount24h,
            Incidents = incidents.Count(s => string.Equals(s, i.Name, StringComparison.OrdinalIgnoreCase))
        }).OrderByDescending(s => s.RequestCount24h).ToList();
    }
}
