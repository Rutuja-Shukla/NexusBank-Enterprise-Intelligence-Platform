using System.Diagnostics;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;
using NexusBank.Infrastructure.Persistence;

namespace NexusBank.Api.Middleware;

/// <summary>
/// Persists one record per API request. Dashboard traffic, latency, availability, traces and
/// the log explorer are all derived from these rows — there are no seeded dashboard numbers.
/// Telemetry write failures never affect the client response.
/// </summary>
public sealed class RequestTelemetryMiddleware
{
    private static readonly string[] SkipPrefixes = { "/health", "/metrics", "/swagger", "/favicon" };

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTelemetryMiddleware> _logger;

    public RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, NexusBankDbContext db, ICurrentUser currentUser)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId)) correlationId = $"cor_{Guid.NewGuid():N}"[..20];

        context.Items["correlationId"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var path = context.Request.Path.Value ?? string.Empty;

            if (!ShouldSkip(path, context.Request.Method))
            {
                try
                {
                    db.ApiRequestLogs.Add(new ApiRequestLog
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Method = context.Request.Method,
                        Path = path,
                        StatusCode = context.Response.StatusCode,
                        DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                        UserId = currentUser.Id,
                        ServiceName = ResolveService(path),
                        CorrelationId = correlationId,
                        TraceId = Activity.Current?.TraceId.ToString(),
                        ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                        IsError = context.Response.StatusCode >= 400
                    });
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Request telemetry could not be persisted for {Method} {Path}", context.Request.Method, path);
                }
            }
        }
    }

    private static bool ShouldSkip(string path, string method) =>
        HttpMethods.IsOptions(method) ||
        SkipPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string ResolveService(string path) => path switch
    {
        var p when p.Contains("/payments") => "Payments Platform",
        var p when p.Contains("/integrations") => "Integration Hub",
        var p when p.Contains("/events") => "Event Streaming",
        var p when p.Contains("/incidents") => "Operations",
        var p when p.Contains("/security") || p.Contains("/auth") => "Identity & Access",
        var p when p.Contains("/observability") || p.Contains("/system") => "Observability",
        var p when p.Contains("/explorer") => "Developer Platform",
        _ => "NexusBank API"
    };
}
