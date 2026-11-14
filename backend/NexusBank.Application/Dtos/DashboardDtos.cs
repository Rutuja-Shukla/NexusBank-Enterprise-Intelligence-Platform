namespace NexusBank.Application.Dtos;

public sealed record MetricDeltaDto
{
    public double Current { get; init; }
    public double Previous { get; init; }
    public double ChangePct => Previous == 0 ? (Current == 0 ? 0 : 100) : Math.Round((Current - Previous) / Previous * 100, 1);
}

public sealed record DashboardSummaryDto
{
    public int IntegrationsTotal { get; init; }
    public int IntegrationsActive { get; init; }
    public int IntegrationsDegraded { get; init; }
    public int IntegrationsDown { get; init; }
    public int ApiRequests24h { get; init; }
    public double ApiRequestsChangePct { get; init; }
    public int Transactions24h { get; init; }
    public decimal TransactionVolume24h { get; init; }
    public double TransactionChangePct { get; init; }
    public double TransactionSuccessRatePct { get; init; }
    public double AvgLatencyMs { get; init; }
    public double LatencyChangePct { get; init; }
    public int ActiveIncidents { get; init; }
    public int Sev1Incidents { get; init; }
    public double AvailabilityPct { get; init; }
    public int EventsLastHour { get; init; }
    public int DeadLetterCount { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record VolumePointDto
{
    public string Bucket { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public decimal Volume { get; init; }
    public int Count { get; init; }
}

public sealed record ActivityItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Tone { get; init; } = "neutral";
    public DateTimeOffset Timestamp { get; init; }
    public string? LinkTo { get; init; }
}

public sealed record DashboardDto
{
    public DashboardSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<TrafficPointDto> Traffic { get; init; } = Array.Empty<TrafficPointDto>();
    public IReadOnlyList<VolumePointDto> Volume { get; init; } = Array.Empty<VolumePointDto>();
    public IReadOnlyList<PaymentListItemDto> RecentPayments { get; init; } = Array.Empty<PaymentListItemDto>();
    public IReadOnlyList<ActivityItemDto> Activity { get; init; } = Array.Empty<ActivityItemDto>();
    public IReadOnlyList<IncidentListItemDto> ActiveIncidents { get; init; } = Array.Empty<IncidentListItemDto>();
    public IReadOnlyList<DependencyDto> Services { get; init; } = Array.Empty<DependencyDto>();
    public IReadOnlyList<TopologyNodeDto> TopologyNodes { get; init; } = Array.Empty<TopologyNodeDto>();
    public IReadOnlyList<TopologyEdgeDto> TopologyEdges { get; init; } = Array.Empty<TopologyEdgeDto>();
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record SystemHealthDto
{
    public string OverallStatus { get; init; } = "unknown";
    public DateTimeOffset CheckedAt { get; init; }
    public long UptimeSeconds { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public bool DatabaseConnected { get; init; }
    public string CacheMode { get; init; } = string.Empty;
    public string EventBusMode { get; init; } = string.Empty;
    public IReadOnlyList<DependencyDto> Components { get; init; } = Array.Empty<DependencyDto>();
    public IReadOnlyList<DependencyDto> Workers { get; init; } = Array.Empty<DependencyDto>();
    public IReadOnlyList<ActivityItemDto> RecentFailures { get; init; } = Array.Empty<ActivityItemDto>();
    public double AvailabilityPct { get; init; }
    public int DeadLetterCount { get; init; }
}
