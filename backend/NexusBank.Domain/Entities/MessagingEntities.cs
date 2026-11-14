using NexusBank.Domain.Enums;

namespace NexusBank.Domain.Entities;

public class EventTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Partitions { get; set; } = 3;
    public int ReplicationFactor { get; set; } = 3;
    public int RetentionHours { get; set; } = 168;
    public string OwnerTeam { get; set; } = "Platform Engineering";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<KafkaEvent> Events { get; set; } = new List<KafkaEvent>();
    public ICollection<ConsumerGroup> ConsumerGroups { get; set; } = new List<ConsumerGroup>();
}

public class KafkaEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TopicId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string? Headers { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
    public DateTimeOffset ProducedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? ConsumerGroupId { get; set; }
    public KafkaEventStatus Status { get; set; } = KafkaEventStatus.Produced;
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// True when the event was handled by the in-process development bus because a
    /// Kafka broker was not reachable. Surfaced in the UI so nothing is misrepresented.
    /// </summary>
    public bool IsSimulated { get; set; }
    public string? CorrelationId { get; set; }
    public EventTopic? Topic { get; set; }
    public ConsumerGroup? ConsumerGroup { get; set; }
}

public class ConsumerGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GroupId { get; set; } = string.Empty;
    public Guid TopicId { get; set; }
    public ConsumerGroupState State { get; set; } = ConsumerGroupState.Stable;
    public int Members { get; set; } = 1;
    public long Lag { get; set; }
    public long CommittedOffset { get; set; }
    public string OwnerTeam { get; set; } = "Platform Engineering";
    public DateTimeOffset LastHeartbeatAt { get; set; } = DateTimeOffset.UtcNow;

    public EventTopic? Topic { get; set; }
}
