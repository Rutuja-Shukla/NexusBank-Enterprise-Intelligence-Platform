using NexusBank.Domain.Enums;

namespace NexusBank.Domain.Entities;

/// <summary>A registered banking system that NexusBank integrates with.</summary>
public class Integration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IntegrationCategory Category { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string HealthEndpointPath { get; set; } = "/health";
    public IntegrationAuthType AuthType { get; set; } = IntegrationAuthType.ApiKey;
    public string? AuthHeaderName { get; set; }
    /// <summary>Name of the configuration/secret reference — never the secret itself.</summary>
    public string? SecretReference { get; set; }
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Unknown;
    public bool Enabled { get; set; } = true;
    public bool IsInternal { get; set; }
    public string OwnerTeam { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";

    public int TimeoutSeconds { get; set; } = 10;
    public int RetryMaxAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 250;
    public int RetryMaxDelayMs { get; set; } = 5000;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;

    public double AvgLatencyMs { get; set; }
    public double FailureRatePct { get; set; }
    public long RequestCount24h { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public string? LastFailureReason { get; set; }

    /// <summary>Stable normalised position used by the integration topology canvas.</summary>
    public double TopologyX { get; set; }
    public double TopologyY { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<IntegrationDependency> OutgoingDependencies { get; set; } = new List<IntegrationDependency>();
    public ICollection<IntegrationDependency> IncomingDependencies { get; set; } = new List<IntegrationDependency>();
    public ICollection<IntegrationHealthSample> HealthSamples { get; set; } = new List<IntegrationHealthSample>();
}

public class IntegrationDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceIntegrationId { get; set; }
    public Guid TargetIntegrationId { get; set; }
    public string Protocol { get; set; } = "REST";
    public string Description { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public Integration? Source { get; set; }
    public Integration? Target { get; set; }
}

public class IntegrationHealthSample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntegrationId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public IntegrationStatus Status { get; set; }
    public double LatencyMs { get; set; }
    public double SuccessRatePct { get; set; }
    public int RequestCount { get; set; }
    public Integration? Integration { get; set; }
}

public class IntegrationAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntegrationId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = "system";
    public string Details { get; set; } = string.Empty;
    public Integration? Integration { get; set; }
}
