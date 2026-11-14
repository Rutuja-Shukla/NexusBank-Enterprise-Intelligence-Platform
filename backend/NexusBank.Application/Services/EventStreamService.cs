using System.Text.Json;
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

public sealed class EventStreamService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _bus;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<EventStreamService> _logger;

    public EventStreamService(
        IApplicationDbContext db,
        IEventBus bus,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<EventStreamService> logger)
    {
        _db = db;
        _bus = bus;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TopicDto>> GetTopicsAsync(CancellationToken cancellationToken = default)
    {
        var since = _clock.UtcNow.AddHours(-24);
        var topics = await _db.EventTopics.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var eventRows = await _db.KafkaEvents.AsNoTracking()
            .Where(e => e.ProducedAt >= since)
            .Select(e => new { e.TopicId, e.Status, e.ProducedAt })
            .ToListAsync(cancellationToken);
        var groups = await _db.ConsumerGroups.AsNoTracking().ToListAsync(cancellationToken);

        return topics.Select(t =>
        {
            var topicEvents = eventRows.Where(e => e.TopicId == t.Id).ToList();
            var topicGroups = groups.Where(g => g.TopicId == t.Id).ToList();
            return new TopicDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Partitions = t.Partitions,
                ReplicationFactor = t.ReplicationFactor,
                RetentionHours = t.RetentionHours,
                OwnerTeam = t.OwnerTeam,
                EventCount24h = topicEvents.Count,
                FailedCount24h = topicEvents.Count(e => e.Status is KafkaEventStatus.Failed or KafkaEventStatus.DeadLettered),
                TotalLag = topicGroups.Sum(g => g.Lag),
                ConsumerGroups = topicGroups.Count,
                LastEventAt = topicEvents.Count == 0 ? null : topicEvents.Max(e => e.ProducedAt)
            };
        }).ToList();
    }

    public async Task<PagedResult<KafkaEventDto>> GetEventsAsync(
        PagedQuery query,
        Guid? topicId,
        KafkaEventStatus? status,
        bool deadLettersOnly,
        CancellationToken cancellationToken = default)
    {
        var source = _db.KafkaEvents.AsNoTracking()
            .Include(e => e.Topic)
            .Include(e => e.ConsumerGroup)
            .AsQueryable();

        if (topicId.HasValue) source = source.Where(e => e.TopicId == topicId);
        if (status.HasValue) source = source.Where(e => e.Status == status);
        if (deadLettersOnly) source = source.Where(e => e.Status == KafkaEventStatus.DeadLettered || e.Status == KafkaEventStatus.Failed);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(e =>
                e.EventType.ToLower().Contains(term) ||
                e.EventKey.ToLower().Contains(term) ||
                (e.ErrorMessage != null && e.ErrorMessage.ToLower().Contains(term)));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderByDescending(e => e.ProducedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<KafkaEventDto>(rows.Select(Map).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<KafkaEventDto> GetEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var evt = await _db.KafkaEvents.AsNoTracking()
            .Include(e => e.Topic)
            .Include(e => e.ConsumerGroup)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Event", id);
        return Map(evt);
    }

    public async Task<IReadOnlyList<ConsumerGroupDto>> GetConsumerGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _db.ConsumerGroups.AsNoTracking()
            .Include(g => g.Topic)
            .OrderByDescending(g => g.Lag)
            .ToListAsync(cancellationToken);

        return groups.Select(g => new ConsumerGroupDto
        {
            Id = g.Id,
            GroupId = g.GroupId,
            Topic = g.Topic?.Name ?? string.Empty,
            State = g.State,
            Members = g.Members,
            Lag = g.Lag,
            CommittedOffset = g.CommittedOffset,
            OwnerTeam = g.OwnerTeam,
            LastHeartbeatAt = g.LastHeartbeatAt
        }).ToList();
    }

    public async Task<StreamStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var hourAgo = _clock.UtcNow.AddHours(-1);
        return new StreamStatusDto
        {
            Mode = _bus.Mode,
            BrokerConnected = _bus.BrokerConnected,
            IsFallback = !_bus.BrokerConnected,
            Detail = _bus.BrokerConnected
                ? "Connected to the configured Kafka broker. Events are produced and consumed over the broker."
                : "Kafka is not reachable. Events are recorded and delivered through the labelled in-process development bus.",
            TopicCount = await _db.EventTopics.CountAsync(cancellationToken),
            EventsLastHour = await _db.KafkaEvents.CountAsync(e => e.ProducedAt >= hourAgo, cancellationToken),
            DeadLetterCount = await _db.KafkaEvents.CountAsync(e => e.Status == KafkaEventStatus.DeadLettered, cancellationToken),
            TotalLag = (int)(await _db.ConsumerGroups.Select(g => g.Lag).ToListAsync(cancellationToken)).Sum(),
            CheckedAt = _clock.UtcNow
        };
    }

    public async Task<KafkaEventDto> PublishAsync(PublishEventRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Topic))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["topic"] = new[] { "A topic name is required." } });

        try
        {
            using var _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.Payload) ? "{}" : request.Payload);
        }
        catch (JsonException)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["payload"] = new[] { "Payload must be valid JSON." } });
        }

        var topic = await _db.EventTopics.FirstOrDefaultAsync(t => t.Name == request.Topic, cancellationToken)
            ?? throw NotFoundException.For("Topic", request.Topic);

        var eventType = string.IsNullOrWhiteSpace(request.EventType) ? "platform.test.published" : request.EventType.Trim();
        var key = string.IsNullOrWhiteSpace(request.EventKey) ? Guid.NewGuid().ToString("N") : request.EventKey.Trim();
        var payload = string.IsNullOrWhiteSpace(request.Payload) ? "{}" : request.Payload;

        var result = await _bus.PublishAsync(topic.Name, key, eventType, payload, cancellationToken);
        var group = await _db.ConsumerGroups.FirstOrDefaultAsync(g => g.TopicId == topic.Id, cancellationToken);

        var evt = new KafkaEvent
        {
            TopicId = topic.Id,
            EventKey = key,
            EventType = eventType,
            Payload = payload,
            Headers = JsonSerializer.Serialize(new { producer = "nexusbank.api", user = _currentUser.Email }),
            Partition = result.Partition ?? Random.Shared.Next(0, topic.Partitions),
            Offset = result.Offset ?? await NextOffsetAsync(topic.Id, cancellationToken),
            ProducedAt = _clock.UtcNow,
            Status = result.Delivered ? KafkaEventStatus.Produced : KafkaEventStatus.DeadLettered,
            IsSimulated = result.Simulated,
            ErrorMessage = result.Error,
            ConsumerGroupId = result.Delivered ? group?.Id : null,
            CorrelationId = References.Correlation()
        };

        _db.KafkaEvents.Add(evt);
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.UtcNow,
            ActorUserId = _currentUser.Id,
            ActorEmail = _currentUser.Email,
            Action = "event.publish",
            EntityType = "KafkaEvent",
            EntityId = evt.Id.ToString(),
            Outcome = result.Delivered ? AuditOutcome.Success : AuditOutcome.Failure,
            Severity = result.Delivered ? "info" : "warning",
            IpAddress = _currentUser.IpAddress,
            Metadata = $"{eventType} → {topic.Name} ({(result.Simulated ? "in-process fallback" : "kafka")})"
        });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Published {EventType} to {Topic} (simulated={Simulated})", eventType, topic.Name, evt.IsSimulated);
        return (await GetEventAsync(evt.Id, cancellationToken));
    }

    public async Task<KafkaEventDto> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var evt = await _db.KafkaEvents.Include(e => e.Topic)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Event", id);

        if (evt.Status is not (KafkaEventStatus.DeadLettered or KafkaEventStatus.Failed))
            throw new ConflictException($"Only failed or dead-lettered events can be retried. This event is {evt.Status}.", "invalid_state");

        var result = await _bus.PublishAsync(evt.Topic?.Name ?? "unknown", evt.EventKey, evt.EventType, evt.Payload, cancellationToken);
        evt.RetryCount++;
        evt.IsSimulated = result.Simulated;

        if (result.Delivered)
        {
            evt.Status = KafkaEventStatus.Retried;
            evt.ConsumedAt = _clock.UtcNow;
            evt.ErrorMessage = null;
            var group = await _db.ConsumerGroups.FirstOrDefaultAsync(g => g.TopicId == evt.TopicId, cancellationToken);
            if (group is not null && group.Lag > 0) group.Lag--;
        }
        else
        {
            evt.Status = KafkaEventStatus.DeadLettered;
            evt.ErrorMessage = result.Error ?? "Replay failed: broker did not acknowledge the message.";
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.UtcNow,
            ActorUserId = _currentUser.Id,
            ActorEmail = _currentUser.Email,
            Action = "event.retry",
            EntityType = "KafkaEvent",
            EntityId = evt.Id.ToString(),
            Outcome = result.Delivered ? AuditOutcome.Success : AuditOutcome.Failure,
            Severity = "info",
            IpAddress = _currentUser.IpAddress,
            Metadata = $"Retry attempt {evt.RetryCount} for {evt.EventType}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        return await GetEventAsync(evt.Id, cancellationToken);
    }

    public async Task<KafkaEventDto> DeadLetterAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var evt = await _db.KafkaEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Event", id);

        if (evt.Status == KafkaEventStatus.DeadLettered)
            throw new ConflictException("This event is already in the dead-letter queue.", "already_dead_lettered");

        evt.Status = KafkaEventStatus.DeadLettered;
        evt.ErrorMessage = string.IsNullOrWhiteSpace(reason) ? $"Parked by {_currentUser.Email}." : reason.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        return await GetEventAsync(evt.Id, cancellationToken);
    }

    private async Task<long> NextOffsetAsync(Guid topicId, CancellationToken cancellationToken)
    {
        var max = await _db.KafkaEvents.Where(e => e.TopicId == topicId).MaxAsync(e => (long?)e.Offset, cancellationToken);
        return (max ?? 0) + 1;
    }

    private static KafkaEventDto Map(KafkaEvent e) => new()
    {
        Id = e.Id,
        TopicId = e.TopicId,
        Topic = e.Topic?.Name ?? string.Empty,
        EventKey = e.EventKey,
        EventType = e.EventType,
        Status = e.Status,
        Partition = e.Partition,
        Offset = e.Offset,
        ProducedAt = e.ProducedAt,
        ConsumedAt = e.ConsumedAt,
        RetryCount = e.RetryCount,
        IsSimulated = e.IsSimulated,
        ErrorMessage = e.ErrorMessage,
        ConsumerGroup = e.ConsumerGroup?.GroupId,
        Payload = e.Payload,
        Headers = e.Headers,
        CorrelationId = e.CorrelationId
    };
}
