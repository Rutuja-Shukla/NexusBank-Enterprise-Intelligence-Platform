using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;

namespace NexusBank.Application.Services;

public sealed class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        IDateTimeProvider clock,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            // Same message for unknown account and wrong password: no account enumeration.
            await WriteAuditAsync(null, email, "auth.login", "User", null, AuditOutcome.Failure, "warning", ipAddress, "Unknown account", cancellationToken);
            throw new AuthenticationException();
        }

        if (!user.IsActive)
        {
            await WriteAuditAsync(user.Id, email, "auth.login", "User", user.Id.ToString(), AuditOutcome.Denied, "warning", ipAddress, "Account disabled", cancellationToken);
            throw new AuthenticationException("This account has been disabled. Contact a platform administrator.", "account_disabled");
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > _clock.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((lockoutEnd - _clock.UtcNow).TotalMinutes));
            await WriteAuditAsync(user.Id, email, "auth.login", "User", user.Id.ToString(), AuditOutcome.Denied, "warning", ipAddress, "Account locked", cancellationToken);
            throw new AuthenticationException($"Account temporarily locked. Try again in {minutes} minute(s).", "account_locked");
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = _clock.UtcNow.Add(LockoutWindow);
                user.FailedLoginAttempts = 0;
            }
            await WriteAuditAsync(user.Id, email, "auth.login", "User", user.Id.ToString(), AuditOutcome.Failure, "warning", ipAddress, "Invalid password", cancellationToken);
            throw new AuthenticationException();
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = _clock.UtcNow;

        var permissions = await EffectivePermissionsAsync(user, cancellationToken);
        var response = await IssueTokensAsync(user, permissions, ipAddress, userAgent, cancellationToken);
        await WriteAuditAsync(user.Id, user.Email, "auth.login", "User", user.Id.ToString(), AuditOutcome.Success, "info", ipAddress, "Signed in", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {Email} signed in from {Ip}", user.Email, ipAddress ?? "unknown");
        return response;
    }

    public async Task<LoginResponse> RefreshAsync(string rawRefreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) throw new AuthenticationException("Refresh token is required.", "refresh_required");

        var hash = _tokens.HashRefreshToken(rawRefreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User!).ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token?.User is null || !token.IsActive || !token.User.IsActive)
            throw new AuthenticationException("Your session has expired. Please sign in again.", "session_expired");

        // Rotation: the presented token is single-use.
        token.RevokedAt = _clock.UtcNow;
        token.RevokedReason = "rotated";
        token.LastUsedAt = _clock.UtcNow;

        var permissions = await EffectivePermissionsAsync(token.User, cancellationToken);
        var response = await IssueTokensAsync(token.User, permissions, ipAddress, userAgent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task LogoutAsync(string? rawRefreshToken, Guid? userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var hash = _tokens.HashRefreshToken(rawRefreshToken);
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
            if (token is not null && token.RevokedAt is null)
            {
                token.RevokedAt = _clock.UtcNow;
                token.RevokedReason = "signed_out";
                await WriteAuditAsync(token.UserId, "session", "auth.logout", "RefreshToken", token.Id.ToString(), AuditOutcome.Success, "info", ipAddress, "Signed out", cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation("Logout processed for user {UserId}", userId);
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw NotFoundException.For("User", userId);

        var permissions = await EffectivePermissionsAsync(user, cancellationToken);
        return MapProfile(user, permissions);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, string? currentRefreshToken, CancellationToken cancellationToken = default)
    {
        var currentHash = string.IsNullOrWhiteSpace(currentRefreshToken) ? null : _tokens.HashRefreshToken(currentRefreshToken);
        var sessions = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > _clock.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return sessions.Select(t => new SessionDto
        {
            Id = t.Id,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            LastUsedAt = t.LastUsedAt,
            IsCurrent = currentHash is not null && t.TokenHash == currentHash,
            IpAddress = t.IpAddress,
            UserAgent = t.UserAgent
        }).ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var session = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId, cancellationToken)
            ?? throw NotFoundException.For("Session", sessionId);

        if (session.RevokedAt is null)
        {
            session.RevokedAt = _clock.UtcNow;
            session.RevokedReason = "revoked_by_user";
            await WriteAuditAsync(userId, "session", "auth.session.revoke", "RefreshToken", sessionId.ToString(), AuditOutcome.Success, "info", ipAddress, "Session revoked", cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AccessHistoryPointDto>> GetAccessHistoryAsync(Guid userId, int days, CancellationToken cancellationToken = default)
    {
        var from = _clock.UtcNow.AddDays(-days);
        var events = await _db.AuditLogs
            .Where(a => a.ActorUserId == userId && a.Timestamp >= from && a.Action.StartsWith("auth.login"))
            .Select(a => new { a.Timestamp, a.Outcome })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, days)
            .Select(offset =>
            {
                var day = _clock.UtcNow.Date.AddDays(-(days - 1 - offset));
                var next = day.AddDays(1);
                return new AccessHistoryPointDto
                {
                    Label = day.ToString("dd MMM"),
                    SignIns = events.Count(e => e.Timestamp.UtcDateTime >= day && e.Timestamp.UtcDateTime < next && e.Outcome == AuditOutcome.Success),
                    Failures = events.Count(e => e.Timestamp.UtcDateTime >= day && e.Timestamp.UtcDateTime < next && e.Outcome != AuditOutcome.Success)
                };
            })
            .ToList();
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, IReadOnlyList<string> permissions, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var access = _tokens.CreateAccessToken(user, permissions);
        var rawRefresh = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokens.HashRefreshToken(rawRefresh),
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.Add(RefreshLifetime),
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, 256)
        });

        await Task.CompletedTask;
        return new LoginResponse
        {
            AccessToken = access.Token,
            ExpiresAt = access.ExpiresAt,
            RefreshToken = rawRefresh,
            User = MapProfile(user, permissions)
        };
    }

    private async Task<IReadOnlyList<string>> EffectivePermissionsAsync(User user, CancellationToken cancellationToken)
    {
        var rolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == user.RoleId)
            .Select(rp => rp.Permission!.Code)
            .ToListAsync(cancellationToken);

        var directPermissions = await _db.UserPermissions
            .Where(up => up.UserId == user.Id)
            .Select(up => up.Permission!.Code)
            .ToListAsync(cancellationToken);

        return rolePermissions.Concat(directPermissions).Distinct().OrderBy(p => p).ToList();
    }

    private static UserProfileDto MapProfile(User user, IReadOnlyList<string> permissions) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role?.Name ?? "Unknown",
        RoleSlug = user.Role?.Slug ?? string.Empty,
        Permissions = permissions,
        LastLoginAt = user.LastLoginAt
    };

    private async Task WriteAuditAsync(Guid? userId, string actor, string action, string entityType, string? entityId,
        AuditOutcome outcome, string severity, string? ipAddress, string detail, CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            ActorEmail = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Outcome = outcome,
            Severity = severity,
            IpAddress = ipAddress,
            Metadata = detail,
            Timestamp = _clock.UtcNow
        });

        // Persist immediately: failed and denied sign-ins throw before the caller saves,
        // and security evidence must survive the rejection.
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
