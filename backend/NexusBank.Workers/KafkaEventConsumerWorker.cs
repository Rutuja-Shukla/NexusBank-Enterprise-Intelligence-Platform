using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusBank.Domain.Enums;
using NexusBank.Infrastructure.Health;
using NexusBank.Infrastructure.Messaging;
using NexusBank.Infrastructure.Persistence;

namespace NexusBank.Workers;

/// <summary>
/// Consumes platform topics and reconciles the persisted event store. When Kafka is not
/// enabled or not reachable the worker stays idle and reports that state on the health page
/// instead of pretending to consume.
/// </summary>
public sealed class KafkaEventConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly ILogger<KafkaEventConsumerWorker> _logger;
    private int _consecutiveFailures;

    public KafkaEventConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        WorkerHeartbeatRegistry heartbeats,
        ILogger<KafkaEventConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _heartbeats = heartbeats;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _heartbeats.Record("Kafka consumer", "Kafka disabled by configuration; consumer is idle");
            _logger.LogInformation("Kafka consumer disabled by configuration");
            return;
        }

        List<string> topics;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();
            topics = await db.EventTopics.Select(t => t.Name).ToListAsync(stoppingToken);
        }

        if (topics.Count == 0)
        {
            _heartbeats.Record("Kafka consumer", "No topics registered; consumer is idle");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
            ClientId = $"{_options.ClientId}-consumer"
        };

        try
        {
            using var consumer = new ConsumerBuilder<string, string>(config).Build();
            consumer.Subscribe(topics);
            _heartbeats.Record("Kafka consumer", $"subscribed to {topics.Count} topic(s)");

            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(750));
                }
                catch (ConsumeException ex)
                {
                    _consecutiveFailures++;
                    _heartbeats.Record("Kafka consumer", $"consume error: {ex.Error.Reason}", _consecutiveFailures);
                    continue;
                }

                if (result?.Message is null) continue;

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();
                var stored = await db.KafkaEvents
                    .Include(e => e.Topic)
                    .FirstOrDefaultAsync(e => e.Topic!.Name == result.Topic && e.Offset == result.Offset.Value, stoppingToken);

                if (stored is null)
                {
                    stored = new Domain.Entities.KafkaEvent
                    {
                        TopicId = await db.EventTopics.Where(t => t.Name == result.Topic).Select(t => t.Id).FirstAsync(stoppingToken),
                        EventKey = result.Message.Key ?? "unknown",
                        EventType = "consumed.external",
                        Payload = result.Message.Value ?? "{}",
                        Partition = result.Partition.Value,
                        Offset = result.Offset.Value,
                        ProducedAt = DateTimeOffset.UtcNow
                    };
                    db.KafkaEvents.Add(stored);
                }

                stored.ConsumedAt = DateTimeOffset.UtcNow;
                stored.Status = KafkaEventStatus.Consumed;
                stored.IsSimulated = false;
                await db.SaveChangesAsync(stoppingToken);

                _consecutiveFailures = 0;
                _heartbeats.Record("Kafka consumer", $"last consumed {result.Topic}@{result.Offset.Value}");
            }

            consumer.Close();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogWarning(ex, "Kafka consumer unavailable; event ingestion paused");
            _heartbeats.Record("Kafka consumer", $"consumer unavailable: {ex.Message}", _consecutiveFailures);
        }
    }
}
