using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record RetryPolicyDto
{
    public int MaxAttempts { get; init; }
    public int BaseDelayMs { get; init; }
    public int MaxDelayMs { get; init; }
    public int CircuitBreakerFailureThreshold { get; init; }
    public int CircuitBreakerBreakSeconds { get; init; }
}

public sealed record IntegrationListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IntegrationCategory Category { get; init; }
    public IntegrationStatus Status { get; init; }
    public IntegrationAuthType AuthType { get; init; }
    public bool Enabled { get; init; }
    public bool IsInternal { get; init; }
    public bool IsSimulated { get; init; }
    public string OwnerTeam { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public double AvgLatencyMs { get; init; }
    public double FailureRatePct { get; init; }
    public long RequestCount24h { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public string? LastFailureReason { get; init; }
}

public sealed record HealthSampleDto
{
    public DateTimeOffset Timestamp { get; init; }
    public IntegrationStatus Status { get; init; }
    public double LatencyMs { get; init; }
    public double SuccessRatePct { get; init; }
}

public sealed record IntegrationAuditDto
{
    public Guid Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}

public sealed record IntegrationLinkDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public bool IsCritical { get; init; }
    public IntegrationStatus Status { get; init; }
}

public sealed record IntegrationDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IntegrationCategory Category { get; init; }
    public IntegrationStatus Status { get; init; }
    public IntegrationAuthType AuthType { get; init; }
    public bool Enabled { get; init; }
    public bool IsInternal { get; init; }
    public bool IsSimulated { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string HealthEndpointPath { get; init; } = "/health";
    public string? AuthHeaderName { get; init; }
    public string? SecretReference { get; init; }
    public string OwnerTeam { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public RetryPolicyDto RetryPolicy { get; init; } = new();
    public double AvgLatencyMs { get; init; }
    public double FailureRatePct { get; init; }
    public long RequestCount24h { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public string? LastFailureReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<IntegrationLinkDto> DependsOn { get; init; } = Array.Empty<IntegrationLinkDto>();
    public IReadOnlyList<IntegrationLinkDto> ConsumedBy { get; init; } = Array.Empty<IntegrationLinkDto>();
    public IReadOnlyList<HealthSampleDto> HealthHistory { get; init; } = Array.Empty<HealthSampleDto>();
    public IReadOnlyList<IntegrationAuditDto> AuditTrail { get; init; } = Array.Empty<IntegrationAuditDto>();
}

public record CreateIntegrationRequest
{
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IntegrationCategory Category { get; init; } = IntegrationCategory.Core;
    public string BaseUrl { get; init; } = string.Empty;
    public string HealthEndpointPath { get; init; } = "/health";
    public IntegrationAuthType AuthType { get; init; } = IntegrationAuthType.ApiKey;
    public string? AuthHeaderName { get; init; }
    public string? SecretReference { get; init; }
    public string OwnerTeam { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public bool IsInternal { get; init; }
    public int TimeoutSeconds { get; init; } = 10;
    public RetryPolicyDto RetryPolicy { get; init; } = new();
}

public sealed record UpdateIntegrationRequest : CreateIntegrationRequest
{
    public bool Enabled { get; init; } = true;
}

public sealed record ToggleIntegrationRequest
{
    public bool Enabled { get; init; }
    public string? Reason { get; init; }
}

public sealed record HealthProbeDto
{
    public Guid IntegrationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public IntegrationStatus Status { get; init; }
    public double LatencyMs { get; init; }
    public string TelemetrySource { get; init; } = "probe";
    public string Detail { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
}

public sealed record TopologyNodeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public IntegrationCategory Category { get; init; }
    public IntegrationStatus Status { get; init; }
    public bool Enabled { get; init; }
    public bool IsSimulated { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string OwnerTeam { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public double AvgLatencyMs { get; init; }
    public double FailureRatePct { get; init; }
    public long RequestCount24h { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
}

public sealed record TopologyEdgeDto
{
    public Guid Id { get; init; }
    public Guid Source { get; init; }
    public Guid Target { get; init; }
    public string Protocol { get; init; } = "REST";
    public bool IsCritical { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool Degraded { get; init; }
}

public sealed record TopologyDto
{
    public IReadOnlyList<TopologyNodeDto> Nodes { get; init; } = Array.Empty<TopologyNodeDto>();
    public IReadOnlyList<TopologyEdgeDto> Edges { get; init; } = Array.Empty<TopologyEdgeDto>();
    public DateTimeOffset GeneratedAt { get; init; }
}
