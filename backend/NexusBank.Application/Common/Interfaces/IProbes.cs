using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;

namespace NexusBank.Application.Common.Interfaces;

public sealed record IntegrationProbeResult(
    IntegrationStatus Status,
    double LatencyMs,
    string TelemetrySource,
    string Detail,
    DateTimeOffset CheckedAt);

/// <summary>
/// Executes a health probe against an integration endpoint using the configured
/// timeout, retry and circuit-breaker policy.
/// </summary>
public interface IIntegrationProbe
{
    Task<IntegrationProbeResult> ProbeAsync(Integration integration, CancellationToken cancellationToken = default);
}

/// <summary>Reports the real state of infrastructure dependencies and background workers.</summary>
public interface IComponentHealthProbe
{
    Task<IReadOnlyList<DependencyDto>> CheckComponentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DependencyDto>> CheckWorkersAsync(CancellationToken cancellationToken = default);
}
