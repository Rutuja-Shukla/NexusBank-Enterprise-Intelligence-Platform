using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Infrastructure.Health;
using NexusBank.Infrastructure.Persistence;

namespace NexusBank.Workers;

/// <summary>
/// Transactional outbox relay. Business changes and outbox rows are committed together,
/// so an event is never published for a rolled-back payment. Failures retry with
/// exponential backoff and remain visible in the outbox table.
/// </summary>
public sealed class OutboxPublisherWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly ILogger<OutboxPublisherWorker> _logger;
    private int _consecutiveFailures;

    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        WorkerHeartbeatRegistry heartbeats,
        ILogger<OutboxPublisherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _heartbeats = heartbeats;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();
                var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
                var now = DateTimeOffset.UtcNow;

                var pending = await db.OutboxMessages
                    .Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
                    .OrderBy(m => m.OccurredAt)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                if (pending.Count > 0)
                {
                    var topicOffsets = new Dictionary<string, long>();

                    foreach (var message in pending)
                    {
                        var result = await bus.PublishAsync(message.Topic, message.Id.ToString(), message.Type, message.Payload, stoppingToken);

                        if (result.Delivered)
                        {
                            message.ProcessedAt = DateTimeOffset.UtcNow;
                            message.Error = result.Simulated ? "Delivered through the in-process fallback (broker unreachable)." : null;
                            processed++;

                            var topic = await db.EventTopics.FirstOrDefaultAsync(t => t.Name == message.Topic, stoppingToken);
                            if (topic is not null)
                            {
                                if (!topicOffsets.TryGetValue(topic.Name, out var nextOffset))
                                {
                                    var max = await db.KafkaEvents.Where(e => e.TopicId == topic.Id)
                                        .MaxAsync(e => (long?)e.Offset, stoppingToken);
                                    nextOffset = (max ?? 0) + 1;
                                }

                                db.KafkaEvents.Add(new KafkaEvent
                                {
                                    TopicId = topic.Id,
                                    EventKey = message.Id.ToString(),
                                    EventType = message.Type,
                                    Payload = message.Payload,
                                    Partition = (int)(nextOffset % topic.Partitions),
                                    Offset = nextOffset,
                                    ProducedAt = DateTimeOffset.UtcNow,
                                    Status = KafkaEventStatus.Produced,
                                    IsSimulated = result.Simulated,
                                    ErrorMessage = result.Error,
                                    Headers = "{\"producer\":\"nexusbank.outbox-relay\"}",
                                    CorrelationId = $"cor_{message.Id:N}"[..20]
                                });
                                topicOffsets[topic.Name] = nextOffset + 1;
                            }
                        }
                        else
                        {
                            message.Attempts++;
                            message.Error = result.Error;
                            message.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, message.Attempts)));
                            _logger.LogWarning("Outbox relay failed for {Id}: {Error}", message.Id, result.Error);
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }

                _consecutiveFailures = 0;
                _heartbeats.Record("Outbox publisher", $"{processed} message(s) relayed in the last cycle, {pending.Count} examined");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "Outbox publisher cycle failed");
                _heartbeats.Record("Outbox publisher", $"cycle failed: {ex.Message}", _consecutiveFailures);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
