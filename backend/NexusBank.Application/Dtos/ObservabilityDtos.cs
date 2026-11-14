using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record SeriesPointDto
{
    public string Bucket { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public double Value { get; init; }
}

public sealed record TrafficPointDto
{
    public string Bucket { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public int Requests { get; init; }
    public int Errors { get; init; }
    public double AvgLatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
}

public sealed record LatencyDistributionDto
{
    public string Bucket { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed record ErrorGroupDto
{
    public string Path { get; init; } = string.Empty;
    public string Method { get; init; } = "GET";
    public int StatusCode { get; init; }
    public int Count { get; init; }
    public double AvgLatencyMs { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
}

public sealed record TraceDto
{
    public string CorrelationId { get; init; } = string.Empty;
    public string? TraceId { get; init; }
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public double DurationMs { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool IsError { get; init; }
    public string? ServiceName { get; init; }
}

public sealed record LogEntryDto
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "info";
    public string Source { get; init; } = "api";
    public string Message { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public string? TraceId { get; init; }
    public string? Path { get; init; }
    public int? StatusCode { get; init; }
    public double? DurationMs { get; init; }
}

public sealed record DependencyDto
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = "healthy";
    public string Detail { get; init; } = string.Empty;
    public double LatencyMs { get; init; }
    public string? Mode { get; init; }
}

public sealed record ObservabilityOverviewDto
{
    public int RequestVolume24h { get; init; }
    public double AvgLatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double ErrorRatePct { get; init; }
    public double AvailabilityPct { get; init; }
    public int ActiveIncidents { get; init; }
    public int ServicesUp { get; init; }
    public int ServicesDown { get; init; }
    public long UptimeSeconds { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<DependencyDto> Dependencies { get; init; } = Array.Empty<DependencyDto>();
}

public sealed record OperationalAlertDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Severity { get; init; } = "warning";
    public string Source { get; init; } = string.Empty;
    public DateTimeOffset DetectedAt { get; init; }
    public string? LinkTo { get; init; }
    public string Status { get; init; } = "active";
}

public sealed record AlertDto
{
    public IReadOnlyList<OperationalAlertDto> Alerts { get; init; } = Array.Empty<OperationalAlertDto>();
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ServiceNodeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IntegrationStatus Status { get; init; }
    public double LatencyMs { get; init; }
    public double ErrorRatePct { get; init; }
    public long RequestCount24h { get; init; }
    public int Incidents { get; init; }
}
