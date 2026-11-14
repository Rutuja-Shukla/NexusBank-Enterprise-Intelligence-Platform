using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using NexusBank.Application.Common.Interfaces;

namespace NexusBank.Infrastructure.Health;

public sealed class ApplicationInfo : IApplicationInfo
{
    public ApplicationInfo(IHostEnvironment environment)
    {
        StartedAt = new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);
        EnvironmentName = environment.EnvironmentName;
        Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    public DateTimeOffset StartedAt { get; }
    public string Version { get; }
    public string EnvironmentName { get; }
}
