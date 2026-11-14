using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshHeader = "X-Refresh-Token";
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await _auth.LoginAsync(request, IpAddress, Request.Headers.UserAgent.ToString(), cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var token = string.IsNullOrWhiteSpace(request.RefreshToken) ? RefreshTokenFromHeader : request.RefreshToken;
        return Ok(await _auth.RefreshAsync(token, IpAddress, Request.Headers.UserAgent.ToString(), cancellationToken));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request, CancellationToken cancellationToken)
    {
        var token = request?.RefreshToken ?? RefreshTokenFromHeader;
        await _auth.LogoutAsync(token, null, IpAddress, cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> Me(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return Ok(await _auth.GetProfileAsync(userId, cancellationToken));
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken cancellationToken)
    {
        return Ok(await _auth.GetSessionsAsync(CurrentUserId(), RefreshTokenFromHeader, cancellationToken));
    }

    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken cancellationToken)
    {
        await _auth.RevokeSessionAsync(CurrentUserId(), id, IpAddress, cancellationToken);
        return NoContent();
    }

    [HttpGet("access-history")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<AccessHistoryPointDto>>> AccessHistory([FromQuery] int days, CancellationToken cancellationToken)
    {
        return Ok(await _auth.GetAccessHistoryAsync(CurrentUserId(), Math.Clamp(days == 0 ? 14 : days, 1, 90), cancellationToken));
    }

    private string? RefreshTokenFromHeader => Request.Headers[RefreshHeader].FirstOrDefault();

    private string? IpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid CurrentUserId()
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new AuthenticationException("Your session is no longer valid.", "session_invalid");
    }
}
