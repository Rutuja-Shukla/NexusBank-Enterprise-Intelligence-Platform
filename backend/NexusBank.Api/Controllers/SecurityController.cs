using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/security")]
[Authorize]
public sealed class SecurityController : ControllerBase
{
    private readonly SecurityService _security;

    public SecurityController(SecurityService security) => _security = security;

    [HttpGet("users")]
    [Authorize(Policy = NexusPermissions.SecurityRead)]
    public async Task<ActionResult<PagedResult<UserListItemDto>>> Users([FromQuery] PagedQuery query, [FromQuery] string? role, CancellationToken cancellationToken) =>
        Ok(await _security.ListUsersAsync(query, role, cancellationToken));

    [HttpPost("users")]
    [Authorize(Policy = NexusPermissions.SecurityWrite)]
    public async Task<ActionResult<UserListItemDto>> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken) =>
        Ok(await _security.CreateUserAsync(request, cancellationToken));

    [HttpPut("users/{id:guid}")]
    [Authorize(Policy = NexusPermissions.SecurityWrite)]
    public async Task<ActionResult<UserListItemDto>> UpdateUser(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken) =>
        Ok(await _security.UpdateUserAsync(id, request, cancellationToken));

    [HttpGet("roles")]
    [Authorize(Policy = NexusPermissions.SecurityRead)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> Roles(CancellationToken cancellationToken) =>
        Ok(await _security.GetRolesAsync(cancellationToken));

    [HttpGet("permissions")]
    [Authorize(Policy = NexusPermissions.SecurityRead)]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> Permissions(CancellationToken cancellationToken) =>
        Ok(await _security.GetPermissionsAsync(cancellationToken));

    [HttpGet("audit-logs")]
    [Authorize(Policy = NexusPermissions.AuditRead)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> AuditLogs(
        [FromQuery] PagedQuery query,
        [FromQuery] AuditOutcome? outcome,
        [FromQuery] string? action,
        CancellationToken cancellationToken) =>
        Ok(await _security.ListAuditAsync(query, outcome, action, cancellationToken));

    [HttpGet("events")]
    [Authorize(Policy = NexusPermissions.SecurityRead)]
    public async Task<ActionResult<IReadOnlyList<AuditLogDto>>> Events([FromQuery] int limit, CancellationToken cancellationToken) =>
        Ok(await _security.GetSecurityEventsAsync(limit == 0 ? 25 : limit, cancellationToken));

    [HttpGet("access-history")]
    [Authorize(Policy = NexusPermissions.AuditRead)]
    public async Task<ActionResult<IReadOnlyList<AccessHistoryPointDto>>> AccessHistory([FromQuery] int days, CancellationToken cancellationToken) =>
        Ok(await _security.GetAccessHistoryAsync(days == 0 ? 14 : days, cancellationToken));
}
