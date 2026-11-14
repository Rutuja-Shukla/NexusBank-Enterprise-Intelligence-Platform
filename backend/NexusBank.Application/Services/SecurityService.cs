using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Application.Services;

public sealed class SecurityService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SecurityService> _logger;

    public SecurityService(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<SecurityService> logger)
    {
        _db = db;
        _hasher = hasher;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PagedResult<UserListItemDto>> ListUsersAsync(PagedQuery query, string? roleSlug, CancellationToken cancellationToken = default)
    {
        var source = _db.Users.AsNoTracking().Include(u => u.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(roleSlug)) source = source.Where(u => u.Role!.Slug == roleSlug);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(u => u.Email.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderBy(u => u.DisplayName).Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        var ids = rows.Select(r => r.Id).ToList();
        var sessions = await _db.RefreshTokens.AsNoTracking()
            .Where(t => ids.Contains(t.UserId) && t.RevokedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        return new PagedResult<UserListItemDto>(rows.Select(u => new UserListItemDto
        {
            Id = u.Id,
            Email = u.Email,
            DisplayName = u.DisplayName,
            Role = u.Role?.Name ?? "Unknown",
            RoleSlug = u.Role?.Slug ?? string.Empty,
            IsActive = u.IsActive,
            IsLockedOut = u.LockoutEnd is not null && u.LockoutEnd > now,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            ActiveSessions = sessions.GetValueOrDefault(u.Id)
        }).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<UserListItemDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            throw new ConflictException("A user with that email address already exists.", "user_exists");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Slug == request.RoleSlug, cancellationToken)
            ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["roleSlug"] = new[] { "Select a valid role." } });

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _hasher.Hash(request.Password),
            RoleId = role.Id,
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        _db.Users.Add(user);
        Audit("security.user.create", user.Id.ToString(), $"Created {email} with role {role.Name}");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {Email} created by {Actor}", email, _currentUser.Email);
        return (await ListUsersAsync(new PagedQuery { Search = email }, null, cancellationToken)).Items.First();
    }

    public async Task<UserListItemDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw NotFoundException.For("User", id);

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Slug == request.RoleSlug, cancellationToken)
            ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["roleSlug"] = new[] { "Select a valid role." } });

        var losingAdmin = user.Role?.Slug == NexusRoles.Administrator &&
                          (role.Slug != NexusRoles.Administrator || !request.IsActive);

        if (losingAdmin)
        {
            var activeAdmins = await _db.Users.CountAsync(u => u.Role!.Slug == NexusRoles.Administrator && u.IsActive, cancellationToken);
            if (activeAdmins <= 1)
                throw new ConflictException("The platform must keep at least one active administrator.", "last_administrator");
        }

        user.DisplayName = request.DisplayName.Trim();
        user.RoleId = role.Id;
        user.IsActive = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
            user.PasswordHash = _hasher.Hash(request.NewPassword);

        if (!user.IsActive)
        {
            var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.RevokedAt = _clock.UtcNow;
                token.RevokedReason = "account_deactivated";
            }
        }

        Audit("security.user.update", user.Id.ToString(), $"Updated {user.Email} (role {role.Name}, active {user.IsActive})");
        await _db.SaveChangesAsync(cancellationToken);

        return (await ListUsersAsync(new PagedQuery { Search = user.Email }, null, cancellationToken)).Items.First();
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.Permissions).ThenInclude(p => p.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var counts = await _db.Users.AsNoTracking()
            .GroupBy(u => u.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        return roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Slug = r.Slug,
            Description = r.Description,
            IsSystem = r.IsSystem,
            UserCount = counts.GetValueOrDefault(r.Id),
            Permissions = r.Permissions.Select(p => p.Permission?.Code ?? string.Empty).Where(c => c.Length > 0).OrderBy(c => c).ToList()
        }).ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _db.Permissions.AsNoTracking().OrderBy(p => p.Category).ThenBy(p => p.Code).ToListAsync(cancellationToken);
        return permissions.Select(p => new PermissionDto { Code = p.Code, Description = p.Description, Category = p.Category }).ToList();
    }

    public async Task<PagedResult<AuditLogDto>> ListAuditAsync(PagedQuery query, AuditOutcome? outcome, string? action, CancellationToken cancellationToken = default)
    {
        var source = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (outcome.HasValue) source = source.Where(a => a.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(action)) source = source.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(a =>
                a.ActorEmail.ToLower().Contains(term) ||
                a.Action.ToLower().Contains(term) ||
                a.EntityType.ToLower().Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderByDescending(a => a.Timestamp).Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(rows.Select(a => new AuditLogDto
        {
            Id = a.Id,
            Timestamp = a.Timestamp,
            Actor = a.ActorEmail,
            Action = a.Action,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            Outcome = a.Outcome,
            Severity = a.Severity,
            IpAddress = a.IpAddress,
            CorrelationId = a.CorrelationId,
            Metadata = a.Metadata
        }).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetSecurityEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Outcome != AuditOutcome.Success || a.Severity != "info" || a.Action.StartsWith("security") || a.Action.StartsWith("auth"))
            .OrderByDescending(a => a.Timestamp)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        return rows.Select(a => new AuditLogDto
        {
            Id = a.Id,
            Timestamp = a.Timestamp,
            Actor = a.ActorEmail,
            Action = a.Action,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            Outcome = a.Outcome,
            Severity = a.Severity,
            IpAddress = a.IpAddress,
            CorrelationId = a.CorrelationId,
            Metadata = a.Metadata
        }).ToList();
    }

    public async Task<IReadOnlyList<AccessHistoryPointDto>> GetAccessHistoryAsync(int days, CancellationToken cancellationToken = default)
    {
        var from = _clock.UtcNow.AddDays(-days);
        var events = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Timestamp >= from && a.Action.StartsWith("auth.login"))
            .Select(a => new { a.Timestamp, a.Outcome })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, days).Select(offset =>
        {
            var day = _clock.UtcNow.Date.AddDays(-(days - 1 - offset));
            var next = day.AddDays(1);
            return new AccessHistoryPointDto
            {
                Label = day.ToString("dd MMM"),
                SignIns = events.Count(e => e.Timestamp.UtcDateTime >= day && e.Timestamp.UtcDateTime < next && e.Outcome == AuditOutcome.Success),
                Failures = events.Count(e => e.Timestamp.UtcDateTime >= day && e.Timestamp.UtcDateTime < next && e.Outcome != AuditOutcome.Success)
            };
        }).ToList();
    }

    private void Audit(string action, string entityId, string detail) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.UtcNow,
            ActorUserId = _currentUser.Id,
            ActorEmail = _currentUser.Email,
            Action = action,
            EntityType = "User",
            EntityId = entityId,
            Outcome = AuditOutcome.Success,
            Severity = "info",
            IpAddress = _currentUser.IpAddress,
            Metadata = detail
        });
}
