using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/integrations")]
[Authorize]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IntegrationService _integrations;

    public IntegrationsController(IntegrationService integrations) => _integrations = integrations;

    [HttpGet]
    [Authorize(Policy = NexusPermissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResult<IntegrationListItemDto>>> List(
        [FromQuery] PagedQuery query,
        [FromQuery] IntegrationCategory? category,
        [FromQuery] IntegrationStatus? status,
        [FromQuery] bool? enabled,
        CancellationToken cancellationToken) =>
        Ok(await _integrations.ListAsync(query, category, status, enabled, cancellationToken));

    [HttpGet("lookup")]
    [Authorize(Policy = NexusPermissions.IntegrationsRead)]
    public async Task<ActionResult<IReadOnlyList<IntegrationListItemDto>>> Lookup(CancellationToken cancellationToken) =>
        Ok(await _integrations.LookupAsync(cancellationToken));

    [HttpGet("topology")]
    [Authorize(Policy = NexusPermissions.IntegrationsRead)]
    public async Task<ActionResult<TopologyDto>> Topology(CancellationToken cancellationToken) =>
        Ok(await _integrations.GetTopologyAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NexusPermissions.IntegrationsRead)]
    public async Task<ActionResult<IntegrationDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _integrations.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = NexusPermissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationDetailDto>> Create([FromBody] CreateIntegrationRequest request, CancellationToken cancellationToken)
    {
        var created = await _integrations.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NexusPermissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationDetailDto>> Update(Guid id, [FromBody] UpdateIntegrationRequest request, CancellationToken cancellationToken) =>
        Ok(await _integrations.UpdateAsync(id, request, cancellationToken));

    [HttpPatch("{id:guid}/enabled")]
    [Authorize(Policy = NexusPermissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationListItemDto>> Toggle(Guid id, [FromBody] ToggleIntegrationRequest request, CancellationToken cancellationToken) =>
        Ok(await _integrations.ToggleAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/probe")]
    [Authorize(Policy = NexusPermissions.IntegrationsWrite)]
    public async Task<ActionResult<HealthProbeDto>> Probe(Guid id, CancellationToken cancellationToken) =>
        Ok(await _integrations.ProbeAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = NexusPermissions.IntegrationsWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _integrations.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
