using Microsoft.Extensions.Logging.Abstractions;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Infrastructure.Security;
using NexusBank.Tests.Support;

namespace NexusBank.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private const string Password = "NexusBank!2026";

    private readonly TestDatabase _database = new();
    private readonly FixedClock _clock = new();
    private readonly BcryptPasswordHasher _hasher = new();
    private readonly StubTokenService _tokens = new();

    private AuthService CreateService() =>
        new(_database.Db, _hasher, _tokens, _clock, NullLogger<AuthService>.Instance);

    private async Task<User> SeedUserAsync(bool isActive = true)
    {
        var role = new Role { Name = "Operations Engineer", Slug = "operations", IsSystem = true, Description = "Runs the platform" };
        role.Permissions.Add(new RolePermission
        {
            RoleId = role.Id,
            Permission = new Permission { Code = "payments:read", Category = "Payments", Description = "Read payments" }
        });

        var user = new User
        {
            Email = "ops@nexusbank.dev",
            DisplayName = "Operations",
            PasswordHash = _hasher.Hash(Password),
            Role = role,
            RoleId = role.Id,
            IsActive = isActive
        };

        _database.Db.Users.Add(user);
        await _database.Db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Throws_AndWritesFailedAudit()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.LoginAsync(new LoginRequest { Email = "nobody@nexusbank.dev", Password = Password }, "127.0.0.1", "tests"));

        var audit = Assert.Single(_database.Db.AuditLogs.Where(a => a.Action == "auth.login"));
        Assert.Equal(AuditOutcome.Failure, audit.Outcome);
        Assert.Equal("nobody@nexusbank.dev", audit.ActorEmail);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens_AndStoresHashedRefreshToken()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        var response = await service.LoginAsync(new LoginRequest { Email = "OPS@nexusbank.dev", Password = Password }, "127.0.0.1", "tests");

        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(response.RefreshToken));
        Assert.Equal(user.Id, response.User.Id);

        var stored = Assert.Single(_database.Db.RefreshTokens.Where(t => t.UserId == user.Id));
        Assert.NotEqual(response.RefreshToken, stored.TokenHash);
        Assert.Equal(_tokens.HashRefreshToken(response.RefreshToken), stored.TokenHash);
        Assert.Equal("127.0.0.1", stored.IpAddress);

        var refreshed = await _database.Db.Users.FindAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.NotNull(refreshed!.LastLoginAt);
        Assert.Equal(0, refreshed.FailedLoginAttempts);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IncrementsFailureCounter()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.LoginAsync(new LoginRequest { Email = user.Email, Password = "WrongPassword!1" }, null, null));

        var reloaded = await _database.Db.Users.FindAsync(user.Id);
        Assert.Equal(1, reloaded!.FailedLoginAttempts);
        Assert.Null(reloaded.LockoutEnd);
    }

    [Fact]
    public async Task Login_LocksAccount_AfterFiveFailedAttempts()
    {
        var user = await SeedUserAsync();
        var service = CreateService();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationException>(() =>
                service.LoginAsync(new LoginRequest { Email = user.Email, Password = "WrongPassword!1" }, null, null));
        }

        var locked = await _database.Db.Users.FindAsync(user.Id);
        Assert.NotNull(locked!.LockoutEnd);
        Assert.True(locked.LockoutEnd > _clock.UtcNow);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.LoginAsync(new LoginRequest { Email = user.Email, Password = Password }, null, null));
        Assert.Contains("locked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WhenAccountDisabled_IsDenied()
    {
        var user = await SeedUserAsync(isActive: false);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.LoginAsync(new LoginRequest { Email = user.Email, Password = Password }, null, null));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _database.Dispose();
}
