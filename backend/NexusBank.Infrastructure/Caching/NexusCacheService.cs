using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusBank.Application.Common.Interfaces;
using StackExchange.Redis;

namespace NexusBank.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public bool Enabled { get; set; } = true;
    public string Configuration { get; set; } = "localhost:6379";
    public int DefaultTtlSeconds { get; set; } = 60;
    public bool AbortOnConnectFail { get; set; } = false;
}

/// <summary>
/// Uses Redis when it is reachable, otherwise a process-local memory cache. The active
/// mode is reported through <see cref="Mode"/> and surfaced on the health page.
/// </summary>
public sealed class NexusCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache? _distributed;
    private readonly IMemoryCache _memory;
    private readonly RedisOptions _options;
    private readonly ILogger<NexusCacheService> _logger;
    private bool? _redisHealthy;

    public NexusCacheService(
        IMemoryCache memory,
        IOptions<RedisOptions> options,
        ILogger<NexusCacheService> logger,
        IDistributedCache? distributed = null)
    {
        _memory = memory;
        _options = options.Value;
        _logger = logger;
        _distributed = _options.Enabled ? distributed : null;
    }

    public bool IsDistributed => _redisHealthy ??= ProbeRedis();

    public string Mode => IsDistributed
        ? $"Redis ({_options.Configuration})"
        : "In-process memory cache (Redis unreachable)";

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (IsDistributed && _distributed is not null)
        {
            try
            {
                var payload = await _distributed.GetStringAsync(key, cancellationToken);
                return payload is null ? default : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
            }
            catch (Exception ex)
            {
                MarkUnhealthy(ex);
            }
        }

        return _memory.TryGetValue(key, out var cached) && cached is T typed ? typed : default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (IsDistributed && _distributed is not null)
        {
            try
            {
                await _distributed.SetStringAsync(key, JsonSerializer.Serialize(value, SerializerOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                MarkUnhealthy(ex);
            }
        }

        _memory.Set(key, value, ttl);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (IsDistributed && _distributed is not null)
        {
            try
            {
                await _distributed.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                MarkUnhealthy(ex);
            }
        }
        _memory.Remove(key);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;

        var created = await factory(cancellationToken);
        await SetAsync(key, created, ttl, cancellationToken);
        return created;
    }

    private bool ProbeRedis()
    {
        if (!_options.Enabled) return false;

        try
        {
            var options = ConfigurationOptions.Parse(_options.Configuration);
            options.AbortOnConnectFail = _options.AbortOnConnectFail;
            options.ConnectTimeout = 1500;
            options.SyncTimeout = 1500;

            using var connection = ConnectionMultiplexer.Connect(options);
            var latency = connection.GetDatabase().Ping();
            _logger.LogInformation("Redis reachable at {Configuration} (ping {Latency} ms)", _options.Configuration, latency.TotalMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Redis unavailable at {Configuration}: {Message}. Falling back to memory cache.", _options.Configuration, ex.Message);
            return false;
        }
    }

    private void MarkUnhealthy(Exception ex)
    {
        _redisHealthy = false;
        _logger.LogWarning(ex, "Redis operation failed; subsequent reads use the in-process cache");
    }
}
