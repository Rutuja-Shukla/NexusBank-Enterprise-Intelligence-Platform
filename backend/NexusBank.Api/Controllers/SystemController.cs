using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
[Authorize]
public sealed class SystemController : ControllerBase
{
    private readonly SystemHealthService _health;

    public SystemController(SystemHealthService health) => _health = health;

    [HttpGet("health")]
    [Authorize(Policy = NexusPermissions.HealthRead)]
    [ProducesResponseType(typeof(SystemHealthDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemHealthDto>> Health(CancellationToken cancellationToken) =>
        Ok(await _health.GetAsync(cancellationToken));

    [HttpGet("metrics")]
    [Authorize(Policy = NexusPermissions.HealthRead)]
    public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
    {
        var content = await _health.GetPrometheusMetricsAsync(cancellationToken);
        return Content(content, "text/plain; version=0.0.4");
    }
}
