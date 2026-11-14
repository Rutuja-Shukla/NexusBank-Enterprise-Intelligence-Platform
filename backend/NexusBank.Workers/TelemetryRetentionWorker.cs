using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusBank.Infrastructure.Health;
using NexusBank.Infrastructure.Persistence;

namespace NexusBank.Workers;

/// <summary>
/// Housekeeping: trims request telemetry, expired idempotency records, revoked sessions and
/// processed outbox rows so the demo database does not grow without bound.
/// </summary>
public sealed class TelemetryRetentionWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private int _consecutiveFailures;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly ILogger<TelemetryRetentionWorker> _logger;

    public TelemetryRetentionWorker(
        IServiceScopeFactory scopeFactory,
        WorkerHeartbeatRegistry heartbeats,
        ILogger<TelemetryRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _heartbeats = heartbeats;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();
                var now = DateTimeOffset.UtcNow;

                var logs = await db.ApiRequestLogs.Where(l => l.Timestamp < now.AddDays(-7)).ExecuteDeleteAsync(stoppingToken);
                var idempotency = await db.IdempotencyRecords.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(stoppingToken);
                var tokens = await db.RefreshTokens.Where(t => t.ExpiresAt < now.AddDays(-30) || (t.RevokedAt != null && t.RevokedAt < now.AddDays(-30))).ExecuteDeleteAsync(stoppingToken);
                var outbox = await db.OutboxMessages.Where(m => m.ProcessedAt != null && m.ProcessedAt < now.AddDays(-7)).ExecuteDeleteAsync(stoppingToken);

                _consecutiveFailures = 0;
                _heartbeats.Record("Telemetry retention",
                    $"purged {logs} request log(s), {idempotency} idempotency record(s), {tokens} session(s), {outbox} outbox row(s)");
                _logger.LogInformation("Telemetry retention removed {Logs} request logs and {Tokens} sessions", logs, tokens);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "Telemetry retention cycle failed");
                _heartbeats.Record("Telemetry retention", $"cycle failed: {ex.Message}", _consecutiveFailures);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
