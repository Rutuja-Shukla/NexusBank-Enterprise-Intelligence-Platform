using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/explorer")]
[Authorize]
[EnableRateLimiting("auth")]
public sealed class ExplorerController : ControllerBase
{
    private readonly ApiExplorerService _explorer;

    public ExplorerController(ApiExplorerService explorer) => _explorer = explorer;

    [HttpGet("catalogue")]
    [Authorize(Policy = NexusPermissions.ExplorerExecute)]
    public ActionResult<IReadOnlyList<EndpointDescriptorDto>> Catalogue() => Ok(_explorer.GetCatalogue());

    [HttpPost("execute")]
    [Authorize(Policy = NexusPermissions.ExplorerExecute)]
    public async Task<ActionResult<ExecuteResponse>> Execute([FromBody] ExecuteRequest request, CancellationToken cancellationToken) =>
        Ok(await _explorer.ExecuteAsync(request, cancellationToken));
}
