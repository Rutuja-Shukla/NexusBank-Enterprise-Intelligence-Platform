using Microsoft.EntityFrameworkCore;
using NexusBank.Domain.Entities;

namespace NexusBank.Application.Common.Interfaces;

/// <summary>
/// Persistence boundary for the application layer. Kept as a single DbContext
/// abstraction instead of a repository per aggregate: the queries here are
/// read-model shaped and a generic repository would add indirection without value.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserPermission> UserPermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Integration> Integrations { get; }
    DbSet<IntegrationDependency> IntegrationDependencies { get; }
    DbSet<IntegrationHealthSample> IntegrationHealthSamples { get; }
    DbSet<IntegrationAuditEntry> IntegrationAuditEntries { get; }

    DbSet<Customer> Customers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentEvent> PaymentEvents { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }

    DbSet<EventTopic> EventTopics { get; }
    DbSet<KafkaEvent> KafkaEvents { get; }
    DbSet<ConsumerGroup> ConsumerGroups { get; }

    DbSet<Incident> Incidents { get; }
    DbSet<IncidentActivity> IncidentActivities { get; }
    DbSet<ApiRequestLog> ApiRequestLogs { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Ambient identity of the caller, resolved from the validated JWT.</summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    string Email { get; }
    string DisplayName { get; }
    string Role { get; }
    IReadOnlySet<string> Permissions { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Cache abstraction over Redis with an in-memory fallback. <see cref="IsDistributed"/>
/// lets the health module report which implementation is actually in use.
/// </summary>
public interface ICacheService
{
    bool IsDistributed { get; }
    string Mode { get; }
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default);
}

public sealed record EventPublishResult(bool Delivered, bool Simulated, int? Partition, long? Offset, string? Error);

/// <summary>
/// Broker abstraction. When a Kafka broker is not reachable the implementation
/// delivers through an in-process bus and flags the result as simulated.
/// </summary>
public interface IEventBus
{
    string Mode { get; }
    bool BrokerConnected { get; }
    Task<EventPublishResult> PublishAsync(string topic, string key, string eventType, string payload, CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IEnumerable<string> permissions);
    string CreateRefreshToken();
    string HashRefreshToken(string rawToken);
}

public interface IApiInvoker
{
    Task<ApiInvocationResult> InvokeAsync(string method, string path, string? jsonBody, Guid actorUserId, IEnumerable<string> permissions, string? idempotencyKey = null, CancellationToken cancellationToken = default);
}

public sealed record ApiInvocationResult(
    int StatusCode,
    string ReasonPhrase,
    double DurationMs,
    string Body,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    long SizeBytes,
    string? Error);
