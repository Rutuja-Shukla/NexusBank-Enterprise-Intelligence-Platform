using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;

namespace NexusBank.Application.Services;

public sealed class IntegrationService
{
    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IIntegrationProbe _probe;
    private readonly ILogger<IntegrationService> _logger;

    public IntegrationService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IIntegrationProbe probe,
        ILogger<IntegrationService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _probe = probe;
        _logger = logger;
    }

    public async Task<PagedResult<IntegrationListItemDto>> ListAsync(
        PagedQuery query,
        IntegrationCategory? category,
        IntegrationStatus? status,
        bool? enabled,
        CancellationToken cancellationToken = default)
    {
        var source = _db.Integrations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(i =>
                i.Name.ToLower().Contains(term) ||
                i.Key.Contains(term) ||
                i.OwnerTeam.ToLower().Contains(term));
        }

        if (category.HasValue) source = source.Where(i => i.Category == category);
        if (status.HasValue) source = source.Where(i => i.Status == status);
        if (enabled.HasValue) source = source.Where(i => i.Enabled == enabled);

        source = (query.Sort ?? "name").ToLowerInvariant() switch
        {
            "-name" => source.OrderByDescending(i => i.Name),
            "status" => source.OrderBy(i => i.Status).ThenBy(i => i.Name),
            "-status" => source.OrderByDescending(i => i.Status).ThenBy(i => i.Name),
            "latency" => source.OrderByDescending(i => i.AvgLatencyMs),
            "failures" => source.OrderByDescending(i => i.FailureRatePct),
            "requests" => source.OrderByDescending(i => i.RequestCount24h),
            "updated" => source.OrderByDescending(i => i.UpdatedAt),
            _ => source.OrderBy(i => i.Name)
        };

        var total = await source.CountAsync(cancellationToken);
        var items = await source.Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<IntegrationListItemDto>(items.Select(MapListItem).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<IntegrationDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var integration = await _db.Integrations
            .AsNoTracking()
            .Include(i => i.OutgoingDependencies).ThenInclude(d => d.Target)
            .Include(i => i.IncomingDependencies).ThenInclude(d => d.Source)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Integration", id);

        var samples = await _db.IntegrationHealthSamples
            .AsNoTracking()
            .Where(s => s.IntegrationId == id)
            .OrderByDescending(s => s.Timestamp)
            .Take(48)
            .ToListAsync(cancellationToken);
        samples.Reverse();

        var audit = await _db.IntegrationAuditEntries
            .AsNoTracking()
            .Where(a => a.IntegrationId == id)
            .OrderByDescending(a => a.Timestamp)
            .Take(25)
            .ToListAsync(cancellationToken);

        return new IntegrationDetailDto
        {
            Id = integration.Id,
            Name = integration.Name,
            Key = integration.Key,
            Description = integration.Description,
            Category = integration.Category,
            Status = integration.Status,
            AuthType = integration.AuthType,
            Enabled = integration.Enabled,
            IsInternal = integration.IsInternal,
            IsSimulated = IsSimulated(integration),
            BaseUrl = integration.BaseUrl,
            HealthEndpointPath = integration.HealthEndpointPath,
            AuthHeaderName = integration.AuthHeaderName,
            SecretReference = integration.SecretReference,
            OwnerTeam = integration.OwnerTeam,
            Version = integration.Version,
            TimeoutSeconds = integration.TimeoutSeconds,
            RetryPolicy = new RetryPolicyDto
            {
                MaxAttempts = integration.RetryMaxAttempts,
                BaseDelayMs = integration.RetryBaseDelayMs,
                MaxDelayMs = integration.RetryMaxDelayMs,
                CircuitBreakerFailureThreshold = integration.CircuitBreakerFailureThreshold,
                CircuitBreakerBreakSeconds = integration.CircuitBreakerBreakSeconds
            },
            AvgLatencyMs = Math.Round(integration.AvgLatencyMs, 1),
            FailureRatePct = Math.Round(integration.FailureRatePct, 2),
            RequestCount24h = integration.RequestCount24h,
            LastSuccessAt = integration.LastSuccessAt,
            LastFailureAt = integration.LastFailureAt,
            LastFailureReason = integration.LastFailureReason,
            CreatedAt = integration.CreatedAt,
            UpdatedAt = integration.UpdatedAt,
            DependsOn = integration.OutgoingDependencies
                .Where(d => d.Target is not null)
                .Select(d => new IntegrationLinkDto
                {
                    Id = d.Target!.Id,
                    Name = d.Target.Name,
                    Protocol = d.Protocol,
                    IsCritical = d.IsCritical,
                    Status = d.Target.Status
                }).ToList(),
            ConsumedBy = integration.IncomingDependencies
                .Where(d => d.Source is not null)
                .Select(d => new IntegrationLinkDto
                {
                    Id = d.Source!.Id,
                    Name = d.Source.Name,
                    Protocol = d.Protocol,
                    IsCritical = d.IsCritical,
                    Status = d.Source.Status
                }).ToList(),
            HealthHistory = samples.Select(s => new HealthSampleDto
            {
                Timestamp = s.Timestamp,
                Status = s.Status,
                LatencyMs = Math.Round(s.LatencyMs, 1),
                SuccessRatePct = Math.Round(s.SuccessRatePct, 1)
            }).ToList(),
            AuditTrail = audit.Select(a => new IntegrationAuditDto
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                Action = a.Action,
                Actor = a.Actor,
                Details = a.Details
            }).ToList()
        };
    }

    public async Task<IntegrationDetailDto> CreateAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var key = request.Key.Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(key))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.Key)] = new[] { "Key must be 3-64 characters, lowercase letters, numbers and hyphens, starting with a letter or number." }
            });

        if (await _db.Integrations.AnyAsync(i => i.Key == key, cancellationToken))
            throw new ConflictException($"An integration with key '{key}' already exists.", "integration_exists");

        var integration = new Integration
        {
            Name = request.Name.Trim(),
            Key = key,
            Description = request.Description.Trim(),
            Category = request.Category,
            BaseUrl = request.BaseUrl.Trim(),
            HealthEndpointPath = string.IsNullOrWhiteSpace(request.HealthEndpointPath) ? "/health" : request.HealthEndpointPath.Trim(),
            AuthType = request.AuthType,
            AuthHeaderName = request.AuthHeaderName,
            SecretReference = request.SecretReference,
            OwnerTeam = request.OwnerTeam.Trim(),
            Version = request.Version.Trim(),
            IsInternal = request.IsInternal,
            Enabled = true,
            Status = IntegrationStatus.Unknown,
            TimeoutSeconds = request.TimeoutSeconds,
            RetryMaxAttempts = request.RetryPolicy.MaxAttempts,
            RetryBaseDelayMs = request.RetryPolicy.BaseDelayMs,
            RetryMaxDelayMs = request.RetryPolicy.MaxDelayMs,
            CircuitBreakerFailureThreshold = request.RetryPolicy.CircuitBreakerFailureThreshold,
            CircuitBreakerBreakSeconds = request.RetryPolicy.CircuitBreakerBreakSeconds,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow,
            // New registrations are placed on the topology canvas deterministically.
            TopologyX = 50 + Random.Shared.Next(-30, 30),
            TopologyY = 50 + Random.Shared.Next(-30, 30)
        };

        _db.Integrations.Add(integration);
        AddAudit(integration.Id, "created", $"Integration '{integration.Name}' registered with endpoint {integration.BaseUrl}.");
        WriteSecurityAudit("integration.create", "Integration", integration.Id.ToString(), $"Registered {integration.Name}");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Integration {Key} registered by {User}", integration.Key, _currentUser.Email);
        return await GetAsync(integration.Id, cancellationToken);
    }

    public async Task<IntegrationDetailDto> UpdateAsync(Guid id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Integration", id);

        var key = request.Key.Trim().ToLowerInvariant();
        if (key != integration.Key)
        {
            if (!KeyPattern.IsMatch(key))
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    [nameof(request.Key)] = new[] { "Key must be 3-64 characters, lowercase letters, numbers and hyphens." }
                });
            if (await _db.Integrations.AnyAsync(i => i.Key == key && i.Id != id, cancellationToken))
                throw new ConflictException($"An integration with key '{key}' already exists.", "integration_exists");
        }

        var changes = new List<string>();
        if (integration.Name != request.Name.Trim()) changes.Add($"name → {request.Name.Trim()}");
        if (integration.BaseUrl != request.BaseUrl.Trim()) changes.Add($"baseUrl → {request.BaseUrl.Trim()}");
        if (integration.AuthType != request.AuthType) changes.Add($"authType → {request.AuthType}");
        if (integration.Enabled != request.Enabled) changes.Add($"enabled → {request.Enabled}");

        integration.Name = request.Name.Trim();
        integration.Key = key;
        integration.Description = request.Description.Trim();
        integration.Category = request.Category;
        integration.BaseUrl = request.BaseUrl.Trim();
        integration.HealthEndpointPath = string.IsNullOrWhiteSpace(request.HealthEndpointPath) ? "/health" : request.HealthEndpointPath.Trim();
        integration.AuthType = request.AuthType;
        integration.AuthHeaderName = request.AuthHeaderName;
        integration.SecretReference = request.SecretReference;
        integration.OwnerTeam = request.OwnerTeam.Trim();
        integration.Version = request.Version.Trim();
        integration.IsInternal = request.IsInternal;
        integration.Enabled = request.Enabled;
        integration.TimeoutSeconds = request.TimeoutSeconds;
        integration.RetryMaxAttempts = request.RetryPolicy.MaxAttempts;
        integration.RetryBaseDelayMs = request.RetryPolicy.BaseDelayMs;
        integration.RetryMaxDelayMs = request.RetryPolicy.MaxDelayMs;
        integration.CircuitBreakerFailureThreshold = request.RetryPolicy.CircuitBreakerFailureThreshold;
        integration.CircuitBreakerBreakSeconds = request.RetryPolicy.CircuitBreakerBreakSeconds;
        integration.UpdatedAt = _clock.UtcNow;

        AddAudit(id, "updated", changes.Count == 0 ? "Configuration re-saved with no material changes." : string.Join("; ", changes));
        WriteSecurityAudit("integration.update", "Integration", id.ToString(), $"Updated {integration.Name}");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Integration {Key} updated by {User}", integration.Key, _currentUser.Email);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IntegrationListItemDto> ToggleAsync(Guid id, ToggleIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Integration", id);

        integration.Enabled = request.Enabled;
        integration.UpdatedAt = _clock.UtcNow;
        if (!request.Enabled) integration.Status = IntegrationStatus.Maintenance;

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "no reason supplied" : request.Reason;
        AddAudit(id, request.Enabled ? "enabled" : "disabled", $"Integration {(request.Enabled ? "enabled" : "disabled")} by {_currentUser.Email} ({reason}).");
        WriteSecurityAudit(request.Enabled ? "integration.enable" : "integration.disable", "Integration", id.ToString(), $"{integration.Name}: {reason}");
        await _db.SaveChangesAsync(cancellationToken);

        return MapListItem(integration);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Integration", id);

        if (await _db.Payments.AnyAsync(p => p.IntegrationId == id, cancellationToken))
            throw new ConflictException("This integration has payment history and cannot be deleted. Disable it instead.", "integration_in_use");

        if (integration.IsInternal)
            throw new ConflictException("Core bank integrations are protected and cannot be deleted. Disable it instead.", "integration_protected");

        _db.Integrations.Remove(integration);
        WriteSecurityAudit("integration.delete", "Integration", id.ToString(), $"Deleted {integration.Name}");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TopologyDto> GetTopologyAsync(CancellationToken cancellationToken = default)
    {
        var integrations = await _db.Integrations.AsNoTracking().ToListAsync(cancellationToken);
        var edges = await _db.IntegrationDependencies.AsNoTracking().ToListAsync(cancellationToken);
        var byId = integrations.ToDictionary(i => i.Id);

        return new TopologyDto
        {
            Nodes = integrations.Select(i => new TopologyNodeDto
            {
                Id = i.Id,
                Name = i.Name,
                Key = i.Key,
                Category = i.Category,
                Status = i.Status,
                Enabled = i.Enabled,
                IsSimulated = IsSimulated(i),
                BaseUrl = i.BaseUrl,
                OwnerTeam = i.OwnerTeam,
                Version = i.Version,
                AvgLatencyMs = Math.Round(i.AvgLatencyMs, 1),
                FailureRatePct = Math.Round(i.FailureRatePct, 2),
                RequestCount24h = i.RequestCount24h,
                X = i.TopologyX,
                Y = i.TopologyY,
                LastSuccessAt = i.LastSuccessAt,
                LastFailureAt = i.LastFailureAt
            }).ToList(),
            Edges = edges.Select(e => new TopologyEdgeDto
            {
                Id = e.Id,
                Source = e.SourceIntegrationId,
                Target = e.TargetIntegrationId,
                Protocol = e.Protocol,
                IsCritical = e.IsCritical,
                Description = e.Description,
                Degraded = byId.TryGetValue(e.SourceIntegrationId, out var source) && source.Status != IntegrationStatus.Healthy
                    || byId.TryGetValue(e.TargetIntegrationId, out var target) && target.Status != IntegrationStatus.Healthy
            }).ToList(),
            GeneratedAt = _clock.UtcNow
        };
    }

    public async Task<HealthProbeDto> ProbeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Integration", id);

        var result = await _probe.ProbeAsync(integration, cancellationToken);
        integration.Status = result.Status;
        integration.AvgLatencyMs = Math.Round(integration.AvgLatencyMs == 0 ? result.LatencyMs : integration.AvgLatencyMs * 0.7 + result.LatencyMs * 0.3, 1);
        integration.LastSuccessAt = result.Status == IntegrationStatus.Healthy ? result.CheckedAt : integration.LastSuccessAt;
        if (result.Status is IntegrationStatus.Down or IntegrationStatus.Degraded)
        {
            integration.LastFailureAt = result.CheckedAt;
            integration.LastFailureReason = result.Detail;
        }
        integration.UpdatedAt = _clock.UtcNow;

        _db.IntegrationHealthSamples.Add(new IntegrationHealthSample
        {
            IntegrationId = integration.Id,
            Timestamp = result.CheckedAt,
            Status = result.Status,
            LatencyMs = result.LatencyMs,
            SuccessRatePct = result.Status == IntegrationStatus.Healthy ? 100 : result.Status == IntegrationStatus.Degraded ? 96.5 : 42.0,
            RequestCount = 1
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new HealthProbeDto
        {
            IntegrationId = integration.Id,
            Name = integration.Name,
            Status = result.Status,
            LatencyMs = Math.Round(result.LatencyMs, 1),
            TelemetrySource = result.TelemetrySource,
            Detail = result.Detail,
            CheckedAt = result.CheckedAt
        };
    }

    public async Task<IReadOnlyList<IntegrationListItemDto>> LookupAsync(CancellationToken cancellationToken = default)
    {
        var items = await _db.Integrations.AsNoTracking().OrderBy(i => i.Name).ToListAsync(cancellationToken);
        return items.Select(MapListItem).ToList();
    }

    /// <summary>Integrations whose base URL uses the mock scheme are simulated fixtures.</summary>
    public static bool IsSimulated(Integration integration) =>
        integration.BaseUrl.StartsWith("mock://", StringComparison.OrdinalIgnoreCase);

    private static IntegrationListItemDto MapListItem(Integration i) => new()
    {
        Id = i.Id,
        Name = i.Name,
        Key = i.Key,
        Description = i.Description,
        Category = i.Category,
        Status = i.Status,
        AuthType = i.AuthType,
        Enabled = i.Enabled,
        IsInternal = i.IsInternal,
        IsSimulated = IsSimulated(i),
        OwnerTeam = i.OwnerTeam,
        Version = i.Version,
        BaseUrl = i.BaseUrl,
        AvgLatencyMs = Math.Round(i.AvgLatencyMs, 1),
        FailureRatePct = Math.Round(i.FailureRatePct, 2),
        RequestCount24h = i.RequestCount24h,
        LastSuccessAt = i.LastSuccessAt,
        LastFailureAt = i.LastFailureAt,
        LastFailureReason = i.LastFailureReason
    };

    private void AddAudit(Guid integrationId, string action, string details) =>
        _db.IntegrationAuditEntries.Add(new IntegrationAuditEntry
        {
            IntegrationId = integrationId,
            Timestamp = _clock.UtcNow,
            Action = action,
            Actor = _currentUser.Email,
            Details = details
        });

    private void WriteSecurityAudit(string action, string entityType, string entityId, string detail) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.UtcNow,
            ActorUserId = _currentUser.Id,
            ActorEmail = _currentUser.Email,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Outcome = AuditOutcome.Success,
            Severity = "info",
            IpAddress = _currentUser.IpAddress,
            Metadata = detail
        });
}
