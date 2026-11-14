using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record TopicDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Partitions { get; init; }
    public int ReplicationFactor { get; init; }
    public int RetentionHours { get; init; }
    public string OwnerTeam { get; init; } = string.Empty;
    public int EventCount24h { get; init; }
    public int FailedCount24h { get; init; }
    public long TotalLag { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public int ConsumerGroups { get; init; }
}

public sealed record KafkaEventDto
{
    public Guid Id { get; init; }
    public Guid TopicId { get; init; }
    public string Topic { get; init; } = string.Empty;
    public string EventKey { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public KafkaEventStatus Status { get; init; }
    public int Partition { get; init; }
    public long Offset { get; init; }
    public DateTimeOffset ProducedAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; init; }
    public int RetryCount { get; init; }
    public bool IsSimulated { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ConsumerGroup { get; init; }
    public string Payload { get; init; } = "{}";
    public string? Headers { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record ConsumerGroupDto
{
    public Guid Id { get; init; }
    public string GroupId { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public ConsumerGroupState State { get; init; }
    public int Members { get; init; }
    public long Lag { get; init; }
    public long CommittedOffset { get; init; }
    public string OwnerTeam { get; init; } = string.Empty;
    public DateTimeOffset LastHeartbeatAt { get; init; }
}

public sealed record StreamStatusDto
{
    public string Mode { get; init; } = string.Empty;
    public bool BrokerConnected { get; init; }
    public bool IsFallback { get; init; }
    public string Detail { get; init; } = string.Empty;
    public int TopicCount { get; init; }
    public int EventsLastHour { get; init; }
    public int DeadLetterCount { get; init; }
    public int TotalLag { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
}

public sealed record PublishEventRequest
{
    public string Topic { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string? EventKey { get; init; }
    public string Payload { get; init; } = "{}";
}
