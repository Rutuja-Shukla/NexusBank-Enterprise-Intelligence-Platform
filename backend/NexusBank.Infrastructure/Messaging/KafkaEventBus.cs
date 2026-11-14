using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusBank.Application.Common.Interfaces;

namespace NexusBank.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";
    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ClientId { get; set; } = "nexusbank-api";
    public string ConsumerGroupId { get; set; } = "nexusbank-platform-consumers";
    public int ConnectionTimeoutMs { get; set; } = 3000;
}

/// <summary>
/// Produces to Kafka when a broker is reachable. If it is not, the message is accepted by
/// an in-process bus and flagged as simulated — the UI and health page report this
/// explicitly rather than pretending a broker exists.
/// </summary>
public sealed class KafkaEventBus : IEventBus, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventBus> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IProducer<string, string>? _producer;
    private bool? _connected;
    private DateTimeOffset _lastProbe = DateTimeOffset.MinValue;

    public KafkaEventBus(IOptions<KafkaOptions> options, ILogger<KafkaEventBus> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool BrokerConnected
    {
        get
        {
            if (!_options.Enabled) return false;
            if (_connected is null || DateTimeOffset.UtcNow - _lastProbe > TimeSpan.FromSeconds(30))
            {
                _ = Task.Run(EnsureConnectedAsync);
            }
            return _connected ?? false;
        }
    }

    public string Mode => !_options.Enabled
        ? "In-process development bus (Kafka disabled by configuration)"
        : BrokerConnected
            ? $"Apache Kafka ({_options.BootstrapServers})"
            : $"In-process development bus (Kafka unreachable at {_options.BootstrapServers})";

    public async Task<EventPublishResult> PublishAsync(string topic, string key, string eventType, string payload, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return new EventPublishResult(true, true, null, null, null);

        var connected = await EnsureConnectedAsync();
        if (!connected)
            return new EventPublishResult(true, true, null, null, "Kafka broker unreachable — accepted by the in-process fallback.");

        try
        {
            var message = new Message<string, string>
            {
                Key = key,
                Value = payload,
                Headers = new Headers { { "event-type", System.Text.Encoding.UTF8.GetBytes(eventType) } }
            };

            var delivery = await _producer!.ProduceAsync(topic, message, cancellationToken);
            return new EventPublishResult(true, false, delivery.Partition.Value, delivery.Offset.Value, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka publish to {Topic} failed; message accepted by fallback", topic);
            _connected = false;
            return new EventPublishResult(true, true, null, null, ex.Message);
        }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (!_options.Enabled) return false;
        if (_connected.HasValue && DateTimeOffset.UtcNow - _lastProbe < TimeSpan.FromSeconds(30)) return _connected.Value;

        await _gate.WaitAsync();
        try
        {
            if (_connected.HasValue && DateTimeOffset.UtcNow - _lastProbe < TimeSpan.FromSeconds(30)) return _connected.Value;

            _lastProbe = DateTimeOffset.UtcNow;
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _options.BootstrapServers,
                ClientId = _options.ClientId
            }).Build();

            admin.GetMetadata(TimeSpan.FromMilliseconds(_options.ConnectionTimeoutMs));

            _producer ??= new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                ClientId = _options.ClientId,
                Acks = Acks.All,
                MessageTimeoutMs = 5000,
                EnableIdempotence = true
            }).Build();

            _connected = true;
            _logger.LogInformation("Kafka broker reachable at {Servers}", _options.BootstrapServers);
            return true;
        }
        catch (Exception ex)
        {
            _connected = false;
            _logger.LogWarning("Kafka broker not reachable at {Servers}: {Message}. Using in-process fallback.", _options.BootstrapServers, ex.Message);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(2));
        _producer?.Dispose();
        _gate.Dispose();
    }
}
