namespace NexusBank.Application.Common.Interfaces;

public interface IApplicationInfo
{
    DateTimeOffset StartedAt { get; }
    string Version { get; }
    string EnvironmentName { get; }
}
