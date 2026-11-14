using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Formatting;
using NexusBank.Shared.Paging;

namespace NexusBank.Application.Services;

public sealed class IncidentService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<IncidentService> _logger;

    public IncidentService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<IncidentService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PagedResult<IncidentListItemDto>> ListAsync(
        PagedQuery query,
        IncidentStatus? status,
        IncidentSeverity? severity,
        IncidentCategory? category,
        bool openOnly,
        CancellationToken cancellationToken = default)
    {
        var source = _db.Incidents.AsNoTracking().AsQueryable();

        if (status.HasValue) source = source.Where(i => i.Status == status);
        if (severity.HasValue) source = source.Where(i => i.Severity == severity);
        if (category.HasValue) source = source.Where(i => i.Category == category);
        if (openOnly) source = source.Where(i => i.Status != IncidentStatus.Resolved);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(i =>
                i.Reference.ToLower().Contains(term) ||
                i.Title.ToLower().Contains(term) ||
                i.Team.ToLower().Contains(term) ||
                (i.AffectedService != null && i.AffectedService.ToLower().Contains(term)));
        }

        source = (query.Sort ?? "-detected").ToLowerInvariant() switch
        {
            "severity" => source.OrderBy(i => i.Severity).ThenByDescending(i => i.DetectedAt),
            "detected" => source.OrderBy(i => i.DetectedAt),
            "status" => source.OrderBy(i => i.Status).ThenByDescending(i => i.DetectedAt),
            _ => source.OrderByDescending(i => i.DetectedAt)
        };

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var activityCounts = await _db.IncidentActivities.AsNoTracking()
            .Where(a => ids.Contains(a.IncidentId))
            .GroupBy(a => a.IncidentId)
            .Select(g => new { IncidentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.IncidentId, x => x.Count, cancellationToken);

        var now = _clock.UtcNow;
        return new PagedResult<IncidentListItemDto>(rows.Select(i => new IncidentListItemDto
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
            MinutesOpen = Math.Round(((i.ResolvedAt ?? now) - i.DetectedAt).TotalMinutes, 0)
        }).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<IncidentDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var incident = await _db.Incidents.AsNoTracking()
            .Include(i => i.Integration)
            .Include(i => i.Activities)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Incident", id);
        return MapDetail(incident);
    }

    public async Task<IncidentDetailDto> CreateAsync(CreateIncidentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.IntegrationId.HasValue &&
            !await _db.Integrations.AnyAsync(i => i.Id == request.IntegrationId, cancellationToken))
            throw NotFoundException.For("Integration", request.IntegrationId);

        var yearStart = new DateTimeOffset(_clock.UtcNow.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sequence = await _db.Incidents.CountAsync(i => i.DetectedAt >= yearStart, cancellationToken) + 1;

        var incident = new Incident
        {
            Reference = References.Incident(sequence),
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Severity = request.Severity,
            Status = IncidentStatus.Open,
            Category = request.Category,
            Team = string.IsNullOrWhiteSpace(request.Team) ? "Platform Engineering" : request.Team.Trim(),
            IntegrationId = request.IntegrationId,
            AffectedService = request.AffectedService?.Trim(),
            ImpactSummary = request.ImpactSummary?.Trim(),
            Assignee = request.Assignee?.Trim(),
            CreatedBy = _currentUser.Email,
            DetectedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        };

        incident.Activities.Add(new IncidentActivity
        {
            IncidentId = incident.Id,
            Type = IncidentActivityType.Created,
            Author = _currentUser.Email,
            Timestamp = _clock.UtcNow,
            Detail = $"Incident raised as {incident.Severity} for {incident.Category}."
        });

        _db.Incidents.Add(incident);
        Audit("incident.create", incident.Id.ToString(), $"Raised {incident.Reference}: {incident.Title}");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Incident {Reference} created with severity {Severity}", incident.Reference, incident.Severity);
        return await GetAsync(incident.Id, cancellationToken);
    }

    public async Task<IncidentDetailDto> UpdateAsync(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken = default)
    {
        var incident = await _db.Incidents.Include(i => i.Activities)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Incident", id);

        var now = _clock.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim() != incident.Title)
        {
            incident.Title = request.Title.Trim();
            AddActivity(incident, IncidentActivityType.Note, $"Title updated to '{incident.Title}'.", now);
        }

        if (request.Severity.HasValue && request.Severity != incident.Severity)
        {
            var previous = incident.Severity;
            incident.Severity = request.Severity.Value;
            AddActivity(incident, IncidentActivityType.StatusChanged, $"Severity changed from {previous} to {incident.Severity}.", now);
        }

        if (request.Status.HasValue && request.Status != incident.Status)
        {
            var previous = incident.Status;
            if (previous == IncidentStatus.Resolved)
                throw new ConflictException("Resolved incidents are closed and cannot be reopened.", "incident_closed");

            incident.Status = request.Status.Value;
            if (incident.Status == IncidentStatus.Acknowledged && incident.AcknowledgedAt is null)
                incident.AcknowledgedAt = now;
            if (incident.Status == IncidentStatus.Resolved)
                incident.ResolvedAt = now;

            AddActivity(incident, incident.Status == IncidentStatus.Resolved ? IncidentActivityType.Resolution : IncidentActivityType.StatusChanged,
                $"Status moved from {previous} to {incident.Status}.", now);
        }

        if (!string.IsNullOrWhiteSpace(request.Team) && request.Team.Trim() != incident.Team)
        {
            incident.Team = request.Team.Trim();
            AddActivity(incident, IncidentActivityType.Assignment, $"Owning team set to {incident.Team}.", now);
        }

        if (request.Assignee is not null && request.Assignee.Trim() != incident.Assignee)
        {
            incident.Assignee = string.IsNullOrWhiteSpace(request.Assignee) ? null : request.Assignee.Trim();
            AddActivity(incident, IncidentActivityType.Assignment, $"Assigned to {incident.Assignee ?? "nobody"}.", now);
        }

        if (!string.IsNullOrWhiteSpace(request.ImpactSummary))
            incident.ImpactSummary = request.ImpactSummary.Trim();

        if (!string.IsNullOrWhiteSpace(request.RootCause))
        {
            incident.RootCause = request.RootCause.Trim();
            AddActivity(incident, IncidentActivityType.Note, $"Root cause recorded: {incident.RootCause}", now);
        }

        if (!string.IsNullOrWhiteSpace(request.CorrectiveActions))
        {
            incident.CorrectiveActions = request.CorrectiveActions.Trim();
            AddActivity(incident, IncidentActivityType.Mitigation, $"Corrective actions recorded: {incident.CorrectiveActions}", now);
        }

        if (!string.IsNullOrWhiteSpace(request.Note))
            AddActivity(incident, IncidentActivityType.Note, request.Note.Trim(), now);

        incident.UpdatedAt = now;
        Audit("incident.update", incident.Id.ToString(), $"Updated {incident.Reference} → {incident.Status} ({incident.Severity})");
        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<IncidentDetailDto> AddNoteAsync(Guid id, string detail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(detail))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["detail"] = new[] { "A note cannot be empty." } });

        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Incident", id);

        _db.IncidentActivities.Add(new IncidentActivity
        {
            IncidentId = incident.Id,
            Timestamp = _clock.UtcNow,
            Type = IncidentActivityType.Note,
            Author = _currentUser.Email,
            Detail = detail.Trim()
        });
        incident.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<IncidentStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var incidents = await _db.Incidents.AsNoTracking()
            .Select(i => new { i.Status, i.Severity, i.DetectedAt, i.ResolvedAt })
            .ToListAsync(cancellationToken);

        var resolved = incidents.Where(i => i.ResolvedAt.HasValue).ToList();
        return new IncidentStatsDto
        {
            Open = incidents.Count(i => i.Status == IncidentStatus.Open),
            Acknowledged = incidents.Count(i => i.Status == IncidentStatus.Acknowledged),
            Investigating = incidents.Count(i => i.Status == IncidentStatus.Investigating),
            Mitigated = incidents.Count(i => i.Status == IncidentStatus.Mitigated),
            Resolved = incidents.Count(i => i.Status == IncidentStatus.Resolved),
            Sev1 = incidents.Count(i => i.Severity == IncidentSeverity.Sev1 && i.Status != IncidentStatus.Resolved),
            Sev2 = incidents.Count(i => i.Severity == IncidentSeverity.Sev2 && i.Status != IncidentStatus.Resolved),
            MeanTimeToResolveMinutes = resolved.Count == 0
                ? 0
                : Math.Round(resolved.Average(i => (i.ResolvedAt!.Value - i.DetectedAt).TotalMinutes), 1)
        };
    }

    private void AddActivity(Incident incident, IncidentActivityType type, string detail, DateTimeOffset at) =>
        _db.IncidentActivities.Add(new IncidentActivity
        {
            IncidentId = incident.Id,
            Timestamp = at,
            Type = type,
            Author = _currentUser.Email,
            Detail = detail
        });

    private void Audit(string action, string entityId, string detail) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.UtcNow,
            ActorUserId = _currentUser.Id,
            ActorEmail = _currentUser.Email,
            Action = action,
            EntityType = "Incident",
            EntityId = entityId,
            Outcome = AuditOutcome.Success,
            Severity = "info",
            IpAddress = _currentUser.IpAddress,
            Metadata = detail
        });

    private static IncidentDetailDto MapDetail(Incident i) => new()
    {
        Id = i.Id,
        Reference = i.Reference,
        Title = i.Title,
        Description = i.Description,
        Severity = i.Severity,
        Status = i.Status,
        Category = i.Category,
        Team = i.Team,
        IntegrationId = i.IntegrationId,
        IntegrationName = i.Integration?.Name,
        AffectedService = i.AffectedService,
        ImpactSummary = i.ImpactSummary,
        RootCause = i.RootCause,
        CorrectiveActions = i.CorrectiveActions,
        CreatedBy = i.CreatedBy,
        Assignee = i.Assignee,
        DetectedAt = i.DetectedAt,
        AcknowledgedAt = i.AcknowledgedAt,
        ResolvedAt = i.ResolvedAt,
        UpdatedAt = i.UpdatedAt,
        Activities = i.Activities.OrderBy(a => a.Timestamp).Select(a => new IncidentActivityDto
        {
            Id = a.Id,
            Timestamp = a.Timestamp,
            Type = a.Type,
            Author = a.Author,
            Detail = a.Detail
        }).ToList()
    };
}
