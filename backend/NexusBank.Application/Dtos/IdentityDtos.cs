using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed record UserProfileDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string RoleSlug { get; init; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public DateTimeOffset? LastLoginAt { get; init; }
}

public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public string RefreshToken { get; init; } = string.Empty;
    public UserProfileDto User { get; init; } = new();
}

public sealed record RefreshRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record SessionDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool IsCurrent { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

public sealed record UserListItemDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string RoleSlug { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsLockedOut { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public int ActiveSessions { get; init; }
}

public sealed record CreateUserRequest
{
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string RoleSlug { get; init; } = string.Empty;
}

public sealed record UpdateUserRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string RoleSlug { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public string? NewPassword { get; init; }
}

public sealed record RoleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public int UserCount { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

public sealed record PermissionDto
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

public sealed record AuditLogDto
{
    public Guid Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string? EntityId { get; init; }
    public AuditOutcome Outcome { get; init; }
    public string Severity { get; init; } = "info";
    public string? IpAddress { get; init; }
    public string? CorrelationId { get; init; }
    public string? Metadata { get; init; }
}

public sealed record AccessHistoryPointDto
{
    public string Label { get; init; } = string.Empty;
    public int SignIns { get; init; }
    public int Failures { get; init; }
}
