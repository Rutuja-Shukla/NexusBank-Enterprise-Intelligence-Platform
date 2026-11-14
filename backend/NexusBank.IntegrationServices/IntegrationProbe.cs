using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.IntegrationServices.Simulation;

namespace NexusBank.IntegrationServices;

/// <summary>
/// Probes a registered integration. Real HTTP endpoints go through a resilience pipeline
/// configured with retry, circuit breaking and a per-integration timeout. Fixture
/// integrations use the <c>mock://</c> scheme and are served by the deterministic
/// simulator, which is always labelled as a simulated telemetry source.
/// </summary>
public sealed class IntegrationProbe : IIntegrationProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<IntegrationProbe> _logger;

    public IntegrationProbe(IHttpClientFactory httpClientFactory, IDateTimeProvider clock, ILogger<IntegrationProbe> logger)
    {
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IntegrationProbeResult> ProbeAsync(Integration integration, CancellationToken cancellationToken = default)
    {
        if (integration.BaseUrl.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            return IntegrationSimulator.Probe(integration, _clock.UtcNow);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("integration-probe");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(integration.TimeoutSeconds, 1, 120)));

            using var response = await client.GetAsync(
                BuildHealthUri(integration.BaseUrl, integration.HealthEndpointPath),
                timeout.Token);

            stopwatch.Stop();
            var healthy = response.IsSuccessStatusCode;

            return new IntegrationProbeResult(
                healthy ? IntegrationStatus.Healthy : IntegrationStatus.Degraded,
                stopwatch.Elapsed.TotalMilliseconds,
                "live probe",
                healthy
                    ? $"Health endpoint returned {(int)response.StatusCode} in {stopwatch.ElapsedMilliseconds} ms."
                    : $"Health endpoint returned {(int)response.StatusCode} for {integration.Name}.",
                _clock.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("Probe timed out after {Timeout}s for {Integration}", integration.TimeoutSeconds, integration.Key);
            return new IntegrationProbeResult(
                IntegrationStatus.Down,
                stopwatch.Elapsed.TotalMilliseconds,
                "live probe",
                $"Probe exceeded the {integration.TimeoutSeconds}s timeout after {integration.RetryMaxAttempts} retries.",
                _clock.UtcNow);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Probe failed for {Integration}", integration.Key);
            return new IntegrationProbeResult(
                IntegrationStatus.Down,
                stopwatch.Elapsed.TotalMilliseconds,
                "live probe",
                $"Probe failed: {ex.Message}",
                _clock.UtcNow);
        }
    }

    private static string BuildHealthUri(string baseUrl, string path)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = string.IsNullOrWhiteSpace(path) ? "/health" : path.Trim();
        if (!trimmedPath.StartsWith('/')) trimmedPath = "/" + trimmedPath;
        return trimmedBase + trimmedPath;
    }
}
