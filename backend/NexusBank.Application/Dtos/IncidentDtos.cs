using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record IncidentListItemDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public IncidentSeverity Severity { get; init; }
    public IncidentStatus Status { get; init; }
    public IncidentCategory Category { get; init; }
    public string Team { get; init; } = string.Empty;
    public string? AffectedService { get; init; }
    public string? Assignee { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public int ActivityCount { get; init; }
    public double MinutesOpen { get; init; }
}

public sealed record IncidentActivityDto
{
    public Guid Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IncidentActivityType Type { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record IncidentDetailDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IncidentSeverity Severity { get; init; }
    public IncidentStatus Status { get; init; }
    public IncidentCategory Category { get; init; }
    public string Team { get; init; } = string.Empty;
    public Guid? IntegrationId { get; init; }
    public string? IntegrationName { get; init; }
    public string? AffectedService { get; init; }
    public string? ImpactSummary { get; init; }
    public string? RootCause { get; init; }
    public string? CorrectiveActions { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public string? Assignee { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<IncidentActivityDto> Activities { get; init; } = Array.Empty<IncidentActivityDto>();
}

public sealed record CreateIncidentRequest
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IncidentSeverity Severity { get; init; } = IncidentSeverity.Sev3;
    public IncidentCategory Category { get; init; } = IncidentCategory.Integration;
    public string Team { get; init; } = "Platform Engineering";
    public Guid? IntegrationId { get; init; }
    public string? AffectedService { get; init; }
    public string? ImpactSummary { get; init; }
    public string? Assignee { get; init; }
}

public sealed record UpdateIncidentRequest
{
    public string? Title { get; init; }
    public IncidentStatus? Status { get; init; }
    public IncidentSeverity? Severity { get; init; }
    public string? Team { get; init; }
    public string? Assignee { get; init; }
    public string? ImpactSummary { get; init; }
    public string? RootCause { get; init; }
    public string? CorrectiveActions { get; init; }
    public string? Note { get; init; }
}

public sealed record AddIncidentNoteRequest
{
    public string Detail { get; init; } = string.Empty;
}

public sealed record IncidentStatsDto
{
    public int Open { get; init; }
    public int Acknowledged { get; init; }
    public int Investigating { get; init; }
    public int Mitigated { get; init; }
    public int Resolved { get; init; }
    public int Sev1 { get; init; }
    public int Sev2 { get; init; }
    public double MeanTimeToResolveMinutes { get; init; }
}
