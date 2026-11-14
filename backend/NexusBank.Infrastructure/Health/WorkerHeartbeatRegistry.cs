using System.Collections.Concurrent;

namespace NexusBank.Infrastructure.Health;

public sealed record WorkerHeartbeat(string Name, DateTimeOffset LastRunAt, string Detail, int ConsecutiveFailures);

/// <summary>
/// Background workers record a heartbeat after every cycle. The health module reads this
/// registry to report whether each worker is actually running.
/// </summary>
public sealed class WorkerHeartbeatRegistry
{
    private readonly ConcurrentDictionary<string, WorkerHeartbeat> _beats = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string name, string detail, int consecutiveFailures = 0) =>
        _beats[name] = new WorkerHeartbeat(name, DateTimeOffset.UtcNow, detail, consecutiveFailures);

    public IReadOnlyList<WorkerHeartbeat> Snapshot() => _beats.Values.OrderBy(b => b.Name).ToList();
}
