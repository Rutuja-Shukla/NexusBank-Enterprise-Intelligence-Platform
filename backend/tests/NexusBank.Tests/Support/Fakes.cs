using System.Security.Cryptography;
using System.Text;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;

namespace NexusBank.Tests.Support;

public sealed class FixedClock : IDateTimeProvider
{
    public FixedClock(DateTimeOffset? now = null) => UtcNow = now ?? new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? Id { get; init; } = Guid.NewGuid();
    public string Email { get; init; } = "tester@nexusbank.dev";
    public string DisplayName { get; init; } = "Test Operator";
    public string Role { get; init; } = "Administrator";
    public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>();
    public string? IpAddress { get; init; } = "127.0.0.1";
    public bool IsAuthenticated => true;
    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

public sealed class StubTokenService : ITokenService
{
    public AccessToken CreateAccessToken(User user, IEnumerable<string> permissions) =>
        new($"header.{Convert.ToBase64String(Encoding.UTF8.GetBytes(user.Email))}.signature", DateTimeOffset.UtcNow.AddMinutes(30));

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
