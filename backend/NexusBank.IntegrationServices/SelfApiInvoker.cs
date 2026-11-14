using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;
using NexusBank.Shared.Formatting;

namespace NexusBank.IntegrationServices;

/// <summary>
/// Executes catalogue requests against the running API. The call is made server-side with a
/// short-lived token minted for the calling operator, so the explorer observes the real
/// pipeline (authentication, authorisation, validation, telemetry) rather than a shortcut.
/// </summary>
public sealed class SelfApiInvoker : IApiInvoker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IApplicationDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SelfApiInvoker> _logger;

    public SelfApiInvoker(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IApplicationDbContext db,
        ITokenService tokens,
        IConfiguration configuration,
        ILogger<SelfApiInvoker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _tokens = tokens;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ApiInvocationResult> InvokeAsync(
        string method,
        string path,
        string? jsonBody,
        Guid actorUserId,
        IEnumerable<string> permissions,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == actorUserId, cancellationToken)
            ?? throw new ForbiddenException("The calling user no longer exists.");

        var token = _tokens.CreateAccessToken(user, permissions);
        var client = _httpClientFactory.CreateClient("self-api");
        client.BaseAddress ??= ResolveBaseAddress();

        using var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Add("X-Correlation-ID", References.Correlation());

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        if (!string.IsNullOrWhiteSpace(jsonBody) && method is not "GET" and not "DELETE")
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            stopwatch.Stop();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers) headers[header.Key] = string.Join(", ", header.Value);
            foreach (var header in response.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);

            return new ApiInvocationResult(
                (int)response.StatusCode,
                response.ReasonPhrase ?? string.Empty,
                stopwatch.Elapsed.TotalMilliseconds,
                body,
                headers,
                Encoding.UTF8.GetByteCount(body),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Explorer invocation of {Method} {Path} failed", method, path);
            return new ApiInvocationResult(0, "No response", stopwatch.Elapsed.TotalMilliseconds,
                JsonSerializer.Serialize(new { error = "The platform could not be reached from the explorer.", detail = ex.Message }),
                new Dictionary<string, string>(), 0, ex.Message);
        }
    }

    private Uri ResolveBaseAddress()
    {
        var configured = _configuration["Api:SelfBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured, UriKind.Absolute, out var explicitUri))
            return explicitUri;

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null)
            return new Uri($"{request.Scheme}://{request.Host}");

        return new Uri("http://localhost:8080");
    }
}
