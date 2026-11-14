using NexusBank.Domain.Enums;

namespace NexusBank.Domain.Entities;

/// <summary>Operational incident with an append-only activity timeline.</summary>
public class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Sev3;
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public IncidentCategory Category { get; set; } = IncidentCategory.Integration;
    public string Team { get; set; } = "Platform Engineering";
    public Guid? IntegrationId { get; set; }
    public string? AffectedService { get; set; }
    public string? ImpactSummary { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveActions { get; set; }
    public string CreatedBy { get; set; } = "system";
    public string? Assignee { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Integration? Integration { get; set; }
    public ICollection<IncidentActivity> Activities { get; set; } = new List<IncidentActivity>();
}

public class IncidentActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public IncidentActivityType Type { get; set; } = IncidentActivityType.Note;
    public string Author { get; set; } = "system";
    public string Detail { get; set; } = string.Empty;
    public Incident? Incident { get; set; }
}

/// <summary>
/// Captured by request-logging middleware for every API call. This is the source of
/// truth for dashboard traffic, latency and error-rate analytics.
/// </summary>
public class ApiRequestLog
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public double DurationMs { get; set; }
    public Guid? UserId { get; set; }
    public string? ServiceName { get; set; }
    public Guid? IntegrationId { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? ClientIp { get; set; }
    public bool IsError { get; set; }
}
