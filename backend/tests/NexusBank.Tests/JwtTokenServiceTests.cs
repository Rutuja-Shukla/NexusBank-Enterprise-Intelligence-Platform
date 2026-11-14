using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NexusBank.Domain.Entities;
using NexusBank.Infrastructure.Security;
using NexusBank.Tests.Support;

namespace NexusBank.Tests;

/// <summary>
/// Token issuance and validation, using the same parameters the API configures in Program.cs
/// (issuer, audience, symmetric key, lifetime validation, 30 second clock skew).
/// </summary>
public sealed class JwtTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-with-at-least-32-bytes-of-entropy";
    private const string Issuer = "nexusbank-api";
    private const string Audience = "nexusbank-console";

    private static JwtOptions Options(string issuer = Issuer, string audience = Audience) => new()
    {
        Issuer = issuer,
        Audience = audience,
        SigningKey = SigningKey,
        AccessTokenMinutes = 30
    };

    private static User User() => new()
    {
        Email = "ops@nexusbank.dev",
        DisplayName = "Operations",
        Role = new Role { Name = "Operations Engineer", Slug = "operations" }
    };

    private static TokenValidationParameters ValidationParameters(string key = SigningKey) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "email",
        RoleClaimType = "role"
    };

    private static JwtSecurityToken Read(string token) => Handler().ReadJwtToken(token);

    // Mirrors Program.cs: claims keep their JWT names (no legacy WS-Federation mapping).
    private static JwtSecurityTokenHandler Handler() => new() { MapInboundClaims = false };

    [Fact]
    public void NewToken_IsValidImmediately_AndHasNoFutureNotBefore()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options()), clock);

        var access = service.CreateAccessToken(User(), new[] { "payments:read" });
        var token = Read(access.Token);

        // No nbf (or one that is already in the past) — never a future activation time.
        Assert.False(token.Payload.ContainsKey(JwtRegisteredClaimNames.Nbf));
        Assert.True(token.ValidFrom <= DateTime.UtcNow.AddSeconds(1));

        var principal = Handler().ValidateToken(access.Token, ValidationParameters(), out var validated);
        Assert.NotNull(principal);
        Assert.Equal(User().Email, principal.FindFirst("email")?.Value);
        Assert.Contains(principal.FindAll("permission"), claim => claim.Value == "payments:read");
        var validatedToken = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Contains(Audience, validatedToken.Audiences);
        Assert.Equal(Issuer, validatedToken.Issuer);
    }

    [Fact]
    public void NewToken_RecordsIssuedAt_AndExpiryThirtyMinutesAhead()
    {
        var issuedAt = new DateTimeOffset(2026, 11, 14, 7, 5, 0, TimeSpan.Zero);
        var clock = new FixedClock(issuedAt);
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options()), clock);

        var access = service.CreateAccessToken(User(), Array.Empty<string>());
        var token = Read(access.Token);

        var iat = long.Parse(token.Payload[JwtRegisteredClaimNames.Iat].ToString()!);
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), iat);
        Assert.Equal(issuedAt.AddMinutes(30).ToUnixTimeSeconds(), Convert.ToInt64(token.Payload.Expiration));
        Assert.Equal(issuedAt.AddMinutes(30), access.ExpiresAt);
    }

    [Fact]
    public void TokenIssuedByHostWithFastClock_IsAcceptedByValidatorOnCorrectClock()
    {
        // Regression: a host whose clock ran ~38 minutes ahead minted tokens with nbf in the
        // future, and a correctly-clocked instance rejected them as "not yet valid".
        var skewedClock = new FixedClock(DateTimeOffset.UtcNow.AddMinutes(38));
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options()), skewedClock);

        var access = service.CreateAccessToken(User(), Array.Empty<string>());

        var principal = Handler().ValidateToken(access.Token, ValidationParameters(), out _);
        Assert.NotNull(principal);
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-31);
        var service = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(Options()),
            new FixedClock(issuedAt));

        var access = service.CreateAccessToken(User(), Array.Empty<string>());

        var exception = Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(access.Token, ValidationParameters(), out _));
        Assert.IsType<SecurityTokenExpiredException>(exception);
    }

    [Fact]
    public void TokenSignedWithDifferentKey_IsRejected()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options()), new FixedClock(DateTimeOffset.UtcNow));
        var access = service.CreateAccessToken(User(), Array.Empty<string>());

        var exception = Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(
                access.Token,
                ValidationParameters(key: "a-completely-different-signing-key-of-32-bytes-min"),
                out _));

        Assert.True(
            exception is SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException,
            $"Expected a signature failure, got {exception.GetType().Name}");
    }

    [Fact]
    public void TokenWithWrongIssuer_IsRejected()
    {
        var service = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(Options(issuer: "some-other-issuer")),
            new FixedClock(DateTimeOffset.UtcNow));

        var access = service.CreateAccessToken(User(), Array.Empty<string>());

        var exception = Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(access.Token, ValidationParameters(), out _));
        Assert.IsType<SecurityTokenInvalidIssuerException>(exception);
    }

    [Fact]
    public void TokenWithWrongAudience_IsRejected()
    {
        var service = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(Options(audience: "some-other-client")),
            new FixedClock(DateTimeOffset.UtcNow));

        var access = service.CreateAccessToken(User(), Array.Empty<string>());

        var exception = Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(access.Token, ValidationParameters(), out _));
        Assert.IsType<SecurityTokenInvalidAudienceException>(exception);
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options()), new FixedClock(DateTimeOffset.UtcNow));
        var access = service.CreateAccessToken(User(), Array.Empty<string>());

        var parts = access.Token.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("\"sub\":", "\"sub\":\"x\",\"ignored\":");
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(tampered, ValidationParameters(), out _));
    }
}
