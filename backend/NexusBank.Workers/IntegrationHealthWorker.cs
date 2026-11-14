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
/// Probes every enabled integration and records the measured result. Integration status on
/// the dashboard is therefore derived from probes, not from a stored constant.
/// </summary>
public sealed class IntegrationHealthWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly ILogger<IntegrationHealthWorker> _logger;
    private int _consecutiveFailures;

    public IntegrationHealthWorker(
        IServiceScopeFactory scopeFactory,
        WorkerHeartbeatRegistry heartbeats,
        ILogger<IntegrationHealthWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _heartbeats = heartbeats;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow the API to finish starting before the first probe cycle.
        try { await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();
                var probe = scope.ServiceProvider.GetRequiredService<IIntegrationProbe>();
                var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

                var integrations = await db.Integrations.Where(i => i.Enabled).ToListAsync(stoppingToken);
                var now = clock.UtcNow;

                foreach (var integration in integrations)
                {
                    var result = await probe.ProbeAsync(integration, stoppingToken);
                    integration.Status = result.Status;
                    integration.UpdatedAt = now;
                    integration.AvgLatencyMs = Math.Round(integration.AvgLatencyMs == 0
                        ? result.LatencyMs
                        : integration.AvgLatencyMs * 0.75 + result.LatencyMs * 0.25, 1);

                    if (result.Status == IntegrationStatus.Healthy)
                    {
                        integration.LastSuccessAt = result.CheckedAt;
                    }
                    else if (result.Status is IntegrationStatus.Down or IntegrationStatus.Degraded)
                    {
                        integration.LastFailureAt = result.CheckedAt;
                        integration.LastFailureReason = result.Detail;
                    }

                    db.IntegrationHealthSamples.Add(new IntegrationHealthSample
                    {
                        IntegrationId = integration.Id,
                        Timestamp = result.CheckedAt,
                        Status = result.Status,
                        LatencyMs = result.LatencyMs,
                        SuccessRatePct = result.Status switch
                        {
                            IntegrationStatus.Healthy => 100,
                            IntegrationStatus.Degraded => 96.5,
                            IntegrationStatus.Maintenance => 100,
                            _ => 41.8
                        },
                        RequestCount = (int)Math.Max(1, integration.RequestCount24h / 1440)
                    });
                }

                await db.SaveChangesAsync(stoppingToken);
                _consecutiveFailures = 0;
                _heartbeats.Record("Integration health monitor", $"{integrations.Count} integration(s) probed");
                _logger.LogDebug("Integration health cycle probed {Count} integrations", integrations.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "Integration health cycle failed");
                _heartbeats.Record("Integration health monitor", $"cycle failed: {ex.Message}", _consecutiveFailures);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
