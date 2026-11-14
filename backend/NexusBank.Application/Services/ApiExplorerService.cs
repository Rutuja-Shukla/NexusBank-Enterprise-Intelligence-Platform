using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Shared.Security;

namespace NexusBank.Application.Services;

/// <summary>
/// Executes API catalogues requests against the platform's own HTTP surface so the
/// explorer observes real status codes, latency and payloads.
/// </summary>
public sealed class ApiExplorerService
{
    private static readonly EndpointDescriptorDto[] Catalogue =
    {
        new() { Method = "GET", Path = "/api/v1/dashboard", Summary = "Operational command centre aggregate", Category = "Platform", Permission = NexusPermissions.DashboardRead },
        new() { Method = "GET", Path = "/api/v1/integrations", Summary = "List integrations (paged, filterable)", Category = "Integrations", Permission = NexusPermissions.IntegrationsRead },
        new() { Method = "GET", Path = "/api/v1/integrations/{id}", Summary = "Integration detail with dependencies and health history", Category = "Integrations", Permission = NexusPermissions.IntegrationsRead, PathParameters = "id" },
        new() { Method = "GET", Path = "/api/v1/integrations/topology", Summary = "Integration topology nodes and edges", Category = "Integrations", Permission = NexusPermissions.IntegrationsRead },
        new() { Method = "POST", Path = "/api/v1/integrations", Summary = "Register a new integration", Category = "Integrations", Permission = NexusPermissions.IntegrationsWrite, SampleBody = "{\n  \"name\": \"Open Banking Consent Service\",\n  \"key\": \"open-banking-consent\",\n  \"description\": \"Consent management for third-party data sharing\",\n  \"category\": \"External\",\n  \"baseUrl\": \"https://consent.example.test\",\n  \"authType\": \"OAuth2ClientCredentials\",\n  \"ownerTeam\": \"Open Banking\",\n  \"version\": \"1.2.0\",\n  \"timeoutSeconds\": 10,\n  \"retryPolicy\": { \"maxAttempts\": 3, \"baseDelayMs\": 250, \"maxDelayMs\": 5000, \"circuitBreakerFailureThreshold\": 5, \"circuitBreakerBreakSeconds\": 30 }\n}" },
        new() { Method = "GET", Path = "/api/v1/customers", Summary = "List synthetic customers", Category = "Core banking", Permission = NexusPermissions.PaymentsRead },
        new() { Method = "GET", Path = "/api/v1/accounts", Summary = "List synthetic accounts", Category = "Core banking", Permission = NexusPermissions.PaymentsRead },
        new() { Method = "GET", Path = "/api/v1/payments", Summary = "List payments with filters and paging", Category = "Payments", Permission = NexusPermissions.PaymentsRead },
        new() { Method = "GET", Path = "/api/v1/payments/{id}", Summary = "Payment detail, journey and ledger postings", Category = "Payments", Permission = NexusPermissions.PaymentsRead, PathParameters = "id" },
        new() { Method = "POST", Path = "/api/v1/payments", Summary = "Create a payment (supports Idempotency-Key header)", Category = "Payments", Permission = NexusPermissions.PaymentsWrite, SampleBody = "{\n  \"creditorName\": \"Harbour Consulting Ltd\",\n  \"creditorAccountNumber\": \"12-3141-0098765-00\",\n  \"amount\": 1250.00,\n  \"currency\": \"NZD\",\n  \"type\": \"Transfer\",\n  \"channel\": \"API\",\n  \"description\": \"Invoice INV-2026-0912\"\n}" },
        new() { Method = "GET", Path = "/api/v1/payments/stats", Summary = "Payment volume and success metrics", Category = "Payments", Permission = NexusPermissions.PaymentsRead },
        new() { Method = "GET", Path = "/api/v1/events/topics", Summary = "Kafka topics with lag and volume", Category = "Events", Permission = NexusPermissions.EventsRead },
        new() { Method = "GET", Path = "/api/v1/events", Summary = "Event stream records", Category = "Events", Permission = NexusPermissions.EventsRead },
        new() { Method = "GET", Path = "/api/v1/events/status", Summary = "Broker connectivity and fallback mode", Category = "Events", Permission = NexusPermissions.EventsRead },
        new() { Method = "POST", Path = "/api/v1/events/publish", Summary = "Publish a synthetic event to a topic", Category = "Events", Permission = NexusPermissions.EventsWrite, SampleBody = "{\n  \"topic\": \"payments.events\",\n  \"eventType\": \"payment.reconciliation.requested\",\n  \"payload\": \"{ \\\"window\\\": \\\"T+1\\\" }\"\n}" },
        new() { Method = "GET", Path = "/api/v1/observability/overview", Summary = "Traffic, latency, availability and dependencies", Category = "Observability", Permission = NexusPermissions.ObservabilityRead },
        new() { Method = "GET", Path = "/api/v1/observability/traces", Summary = "Recent request traces", Category = "Observability", Permission = NexusPermissions.ObservabilityRead },
        new() { Method = "GET", Path = "/api/v1/observability/alerts", Summary = "Derived operational alerts", Category = "Observability", Permission = NexusPermissions.ObservabilityRead },
        new() { Method = "GET", Path = "/api/v1/incidents", Summary = "Incident register", Category = "Operations", Permission = NexusPermissions.IncidentsRead },
        new() { Method = "GET", Path = "/api/v1/incidents/stats", Summary = "Incident KPIs and MTTR", Category = "Operations", Permission = NexusPermissions.IncidentsRead },
        new() { Method = "GET", Path = "/api/v1/security/audit-logs", Summary = "Security audit trail", Category = "Security", Permission = NexusPermissions.AuditRead },
        new() { Method = "GET", Path = "/api/v1/security/roles", Summary = "Roles and their permissions", Category = "Security", Permission = NexusPermissions.SecurityRead },
        new() { Method = "GET", Path = "/api/v1/system/health", Summary = "Component-level system health", Category = "Platform", Permission = NexusPermissions.HealthRead },
        new() { Method = "GET", Path = "/api/v1/system/metrics", Summary = "Prometheus metrics for the platform", Category = "Platform", Permission = NexusPermissions.HealthRead }
    };

    private readonly IApiInvoker _invoker;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ApiExplorerService> _logger;

    public ApiExplorerService(IApiInvoker invoker, ICurrentUser currentUser, IDateTimeProvider clock, ILogger<ApiExplorerService> logger)
    {
        _invoker = invoker;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<EndpointDescriptorDto> GetCatalogue() =>
        Catalogue.OrderBy(c => c.Category).ThenBy(c => c.Path).ToList();

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken = default)
    {
        var method = (request.Method ?? "GET").Trim().ToUpperInvariant();
        var path = (request.Path ?? string.Empty).Trim();

        if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["path"] = new[] { "Requests must target a path under /api/v1/." } });

        if (!Catalogue.Any(c => c.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && Matches(c.Path, path)))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["path"] = new[] { $"'{method} {path}' is not part of the curated API catalogue." } });

        if (_currentUser.Id is null)
            throw new ForbiddenException("An authenticated session is required to execute API requests.");

        var stopwatch = Stopwatch.StartNew();
        var result = await _invoker.InvokeAsync(method, path, request.Body, _currentUser.Id.Value, _currentUser.Permissions, request.IdempotencyKey, cancellationToken);
        stopwatch.Stop();

        _logger.LogInformation("API explorer executed {Method} {Path} → {Status} in {Duration}ms", method, path, result.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

        var trimmed = result.Body.Length > 200_000 ? result.Body[..200_000] : result.Body;
        return new ExecuteResponse
        {
            StatusCode = result.StatusCode,
            ReasonPhrase = result.ReasonPhrase,
            DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
            Body = trimmed,
            BodyIsJson = LooksLikeJson(trimmed),
            SizeBytes = result.SizeBytes,
            Headers = result.ResponseHeaders,
            Error = result.Error,
            ExecutedAt = _clock.UtcNow
        };
    }

    private static bool Matches(string template, string path)
    {
        var cleanPath = path.Split('?')[0].TrimEnd('/');
        var segments = template.TrimEnd('/').Split('/');
        var actual = cleanPath.Split('/');
        if (segments.Length != actual.Length) return false;

        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith('{') && segments[i].EndsWith('}')) continue;
            if (!segments[i].Equals(actual[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static bool LooksLikeJson(string body)
    {
        var trimmed = body.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }
}
