using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;

namespace NexusBank.IntegrationServices.Simulation;

/// <summary>
/// Fixture integrations (base URL <c>mock://…</c>) do not have a real upstream system in
/// this portfolio build. The simulator produces a deterministic result per five-minute
/// window biased by the integration's recorded state, and every result is returned with
/// the telemetry source "simulated fixture" so nothing is presented as a live dependency.
/// </summary>
public static class IntegrationSimulator
{
    public static IntegrationProbeResult Probe(Integration integration, DateTimeOffset now)
    {
        var window = now.ToUnixTimeSeconds() / 300;
        var seed = StableHash($"{integration.Key}:{window}");
        var jitter = (seed % 1000) / 1000.0;

        var status = integration.Status switch
        {
            IntegrationStatus.Down => jitter < 0.72 ? IntegrationStatus.Down : IntegrationStatus.Degraded,
            IntegrationStatus.Degraded => jitter < 0.55 ? IntegrationStatus.Degraded : IntegrationStatus.Healthy,
            IntegrationStatus.Maintenance => IntegrationStatus.Maintenance,
            IntegrationStatus.Unknown => IntegrationStatus.Healthy,
            _ => jitter < 0.02 ? IntegrationStatus.Degraded : IntegrationStatus.Healthy
        };

        var latency = status switch
        {
            IntegrationStatus.Down => 2_400 + jitter * 900,
            IntegrationStatus.Degraded => integration.AvgLatencyMs * (1.8 + jitter),
            _ => Math.Max(8, integration.AvgLatencyMs * (0.75 + jitter * 0.6))
        };

        var detail = status switch
        {
            IntegrationStatus.Down => "Simulated upstream unavailable: connection refused after retries.",
            IntegrationStatus.Degraded => $"Simulated degradation: p95 latency {latency:0} ms against a {integration.AvgLatencyMs * 1.5:0} ms threshold.",
            IntegrationStatus.Maintenance => "Integration is disabled for maintenance; probes are suppressed.",
            _ => $"Simulated health check succeeded in {latency:0} ms."
        };

        return new IntegrationProbeResult(status, latency, "simulated fixture", detail, now);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value) hash = hash * 31 + c;
            return Math.Abs(hash);
        }
    }
}
