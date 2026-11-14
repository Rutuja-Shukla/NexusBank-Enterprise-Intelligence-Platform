using Microsoft.EntityFrameworkCore;
using NexusBank.Application.Common;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Formatting;

namespace NexusBank.Application.Services;

public sealed class DashboardService
{
    private sealed record PaymentRow(DateTimeOffset CreatedAt, decimal Amount, PaymentStatus Status, PaymentType Type);
    private sealed record RequestRow(DateTimeOffset Timestamp, int StatusCode, double DurationMs);

    private readonly IApplicationDbContext _db;
    private readonly IntegrationService _integrations;
    private readonly IComponentHealthProbe _probe;
    private readonly IDateTimeProvider _clock;

    public DashboardService(
        IApplicationDbContext db,
        IntegrationService integrations,
        IComponentHealthProbe probe,
        IDateTimeProvider clock)
    {
        _db = db;
        _integrations = integrations;
        _probe = probe;
        _clock = clock;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var dayAgo = now.AddHours(-24);
        var twoDaysAgo = now.AddHours(-48);

        var integrations = await _db.Integrations.AsNoTracking().ToListAsync(cancellationToken);

        var logs = await _db.ApiRequestLogs.AsNoTracking()
            .Where(l => l.Timestamp >= twoDaysAgo)
            .Select(l => new RequestRow(l.Timestamp, l.StatusCode, l.DurationMs))
            .ToListAsync(cancellationToken);

        var currentLogs = logs.Where(l => l.Timestamp >= dayAgo).ToList();
        var previousLogs = logs.Where(l => l.Timestamp < dayAgo).ToList();

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.CreatedAt >= now.AddDays(-14))
            .Select(p => new PaymentRow(p.CreatedAt, p.Amount, p.Status, p.Type))
            .ToListAsync(cancellationToken);

        var currentPayments = payments.Where(p => p.CreatedAt >= dayAgo).ToList();
        var previousPayments = payments.Where(p => p.CreatedAt < dayAgo && p.CreatedAt >= twoDaysAgo).ToList();

        var completed = currentPayments.Count(p => p.Status == PaymentStatus.Completed);
        var failed = currentPayments.Count(p => p.Status == PaymentStatus.Failed);
        var decided = completed + failed;

        var incidents = await _db.Incidents.AsNoTracking()
            .Where(i => i.Status != IncidentStatus.Resolved)
            .OrderBy(i => i.Severity).ThenByDescending(i => i.DetectedAt)
            .Take(6)
            .ToListAsync(cancellationToken);

        var incidentIds = incidents.Select(i => i.Id).ToList();
        var activityCounts = await _db.IncidentActivities.AsNoTracking()
            .Where(a => incidentIds.Contains(a.IncidentId))
            .GroupBy(a => a.IncidentId)
            .Select(g => new { IncidentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.IncidentId, x => x.Count, cancellationToken);

        var topology = await _integrations.GetTopologyAsync(cancellationToken);
        var components = await _probe.CheckComponentsAsync(cancellationToken);

        var recentPaymentRows = await _db.Payments.AsNoTracking()
            .Include(p => p.Integration)
            .OrderByDescending(p => p.CreatedAt)
            .Take(7)
            .ToListAsync(cancellationToken);

        var serverErrors = currentLogs.Count(l => l.StatusCode >= 500);
        var availability = currentLogs.Count == 0 ? 100 : Math.Round(100 - serverErrors * 100.0 / currentLogs.Count, 3);

        var summary = new DashboardSummaryDto
        {
            IntegrationsTotal = integrations.Count,
            IntegrationsActive = integrations.Count(i => i.Enabled),
            IntegrationsDegraded = integrations.Count(i => i.Status == IntegrationStatus.Degraded),
            IntegrationsDown = integrations.Count(i => i.Status == IntegrationStatus.Down),
            ApiRequests24h = currentLogs.Count,
            ApiRequestsChangePct = PctChange(previousLogs.Count, currentLogs.Count),
            Transactions24h = currentPayments.Count,
            TransactionVolume24h = currentPayments
                .Where(p => p.Status is PaymentStatus.Completed or PaymentStatus.Processing)
                .Sum(p => p.Amount),
            TransactionChangePct = PctChange(previousPayments.Count, currentPayments.Count),
            TransactionSuccessRatePct = decided == 0 ? 100 : Math.Round(completed * 100.0 / decided, 2),
            AvgLatencyMs = currentLogs.Count == 0 ? 0 : Math.Round(currentLogs.Average(l => l.DurationMs), 1),
            LatencyChangePct = previousLogs.Count == 0 || currentLogs.Count == 0
                ? 0
                : Math.Round((currentLogs.Average(l => l.DurationMs) - previousLogs.Average(l => l.DurationMs))
                             / previousLogs.Average(l => l.DurationMs) * 100, 1),
            ActiveIncidents = incidents.Count,
            Sev1Incidents = incidents.Count(i => i.Severity == IncidentSeverity.Sev1),
            AvailabilityPct = availability,
            EventsLastHour = await _db.KafkaEvents.CountAsync(e => e.ProducedAt >= now.AddHours(-1), cancellationToken),
            DeadLetterCount = await _db.KafkaEvents.CountAsync(e => e.Status == KafkaEventStatus.DeadLettered, cancellationToken),
            GeneratedAt = now
        };

        return new DashboardDto
        {
            Summary = summary,
            Traffic = BuildTraffic(logs, now),
            Volume = BuildVolume(payments, now),
            RecentPayments = recentPaymentRows.Select(p => new PaymentListItemDto
            {
                Id = p.Id,
                Reference = p.Reference,
                Status = p.Status,
                Type = p.Type,
                Amount = p.Amount,
                Currency = p.Currency,
                CreditorName = p.CreditorName,
                DebtorAccountNumber = Masking.AccountNumber(p.DebtorAccountNumber),
                Channel = p.Channel,
                IntegrationName = p.Integration?.Name,
                RiskScore = p.RiskScore,
                ProcessingDurationMs = p.ProcessingDurationMs,
                CreatedAt = p.CreatedAt,
                CompletedAt = p.CompletedAt
            }).ToList(),
            Activity = await BuildActivityAsync(cancellationToken),
            ActiveIncidents = incidents.Select(i => new IncidentListItemDto
            {
                Id = i.Id,
                Reference = i.Reference,
                Title = i.Title,
                Severity = i.Severity,
                Status = i.Status,
                Category = i.Category,
                Team = i.Team,
                AffectedService = i.AffectedService,
                Assignee = i.Assignee,
                DetectedAt = i.DetectedAt,
                AcknowledgedAt = i.AcknowledgedAt,
                ResolvedAt = i.ResolvedAt,
                ActivityCount = activityCounts.GetValueOrDefault(i.Id),
                MinutesOpen = Math.Round((now - i.DetectedAt).TotalMinutes, 0)
            }).ToList(),
            Services = components,
            TopologyNodes = topology.Nodes,
            TopologyEdges = topology.Edges,
            GeneratedAt = now
        };
    }

    private static IReadOnlyList<TrafficPointDto> BuildTraffic(IReadOnlyList<RequestRow> rows, DateTimeOffset now) =>
        TimeBuckets.Hourly(now, 24).Select(bucket =>
        {
            var inBucket = rows.Where(r => r.Timestamp >= bucket.Start && r.Timestamp < bucket.End).ToList();
            var durations = inBucket.Select(r => r.DurationMs).ToList();
            return new TrafficPointDto
            {
                Bucket = bucket.Label,
                Timestamp = bucket.Start,
                Requests = inBucket.Count,
                Errors = inBucket.Count(r => r.StatusCode >= 500),
                AvgLatencyMs = durations.Count == 0 ? 0 : Math.Round(durations.Average(), 1),
                P95LatencyMs = Math.Round(TimeBuckets.Percentile(durations, 95), 1)
            };
        }).ToList();

    private static IReadOnlyList<VolumePointDto> BuildVolume(IReadOnlyList<PaymentRow> payments, DateTimeOffset now) =>
        TimeBuckets.Daily(now, 14).Select(bucket =>
        {
            var inBucket = payments.Where(p => p.CreatedAt >= bucket.Start && p.CreatedAt < bucket.End).ToList();
            return new VolumePointDto
            {
                Bucket = bucket.Label,
                Timestamp = bucket.Start,
                Volume = inBucket.Where(p => p.Status is PaymentStatus.Completed or PaymentStatus.Processing).Sum(p => p.Amount),
                Count = inBucket.Count
            };
        }).ToList();

    private async Task<IReadOnlyList<ActivityItemDto>> BuildActivityAsync(CancellationToken cancellationToken)
    {
        var audits = await _db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp).Take(8).ToListAsync(cancellationToken);
        var incidents = await _db.Incidents.AsNoTracking().OrderByDescending(i => i.UpdatedAt).Take(4).ToListAsync(cancellationToken);
        var events = await _db.KafkaEvents.AsNoTracking().Include(e => e.Topic).OrderByDescending(e => e.ProducedAt).Take(4).ToListAsync(cancellationToken);

        var items = new List<ActivityItemDto>();

        items.AddRange(audits.Select(a => new ActivityItemDto
        {
            Id = a.Id.ToString(),
            Kind = a.Action.StartsWith("payment") ? "payment"
                : a.Action.StartsWith("incident") ? "incident"
                : a.Action.StartsWith("integration") ? "integration"
                : "audit",
            Title = a.Action,
            Detail = a.Metadata ?? $"{a.EntityType} {ShortId(a.EntityId)}",
            Tone = a.Outcome == AuditOutcome.Success ? "neutral" : a.Outcome == AuditOutcome.Denied ? "warning" : "danger",
            Timestamp = a.Timestamp,
            LinkTo = a.EntityType switch
            {
                "Integration" when a.EntityId is not null => $"/integrations/{a.EntityId}",
                "Incident" when a.EntityId is not null => $"/incidents/{a.EntityId}",
                "Payment" when a.EntityId is not null => $"/payments/{a.EntityId}",
                _ => "/observability"
            }
        }));

        items.AddRange(incidents.Select(i => new ActivityItemDto
        {
            Id = i.Id.ToString(),
            Kind = "incident",
            Title = $"{i.Reference} · {i.Status}",
            Detail = i.Title,
            Tone = i.Severity is IncidentSeverity.Sev1 or IncidentSeverity.Sev2 ? "danger" : "warning",
            Timestamp = i.UpdatedAt,
            LinkTo = $"/incidents/{i.Id}"
        }));

        items.AddRange(events.Select(e => new ActivityItemDto
        {
            Id = e.Id.ToString(),
            Kind = "event",
            Title = e.EventType,
            Detail = $"{e.Topic?.Name ?? "topic"} · partition {e.Partition} · offset {e.Offset}",
            Tone = e.Status is KafkaEventStatus.Failed or KafkaEventStatus.DeadLettered ? "danger" : "neutral",
            Timestamp = e.ProducedAt,
            LinkTo = "/events"
        }));

        return items.OrderByDescending(i => i.Timestamp).Take(12).ToList();
    }

    private static string ShortId(string? id) =>
        string.IsNullOrEmpty(id) ? string.Empty : id[..Math.Min(8, id.Length)];

    private static double PctChange(double previous, double current)
    {
        if (previous == 0) return current == 0 ? 0 : 100;
        return Math.Round((current - previous) / previous * 100, 1);
    }
}
