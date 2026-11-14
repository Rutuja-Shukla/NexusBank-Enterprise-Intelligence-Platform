using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/incidents")]
[Authorize]
public sealed class IncidentsController : ControllerBase
{
    private readonly IncidentService _incidents;

    public IncidentsController(IncidentService incidents) => _incidents = incidents;

    [HttpGet]
    [Authorize(Policy = NexusPermissions.IncidentsRead)]
    public async Task<ActionResult<PagedResult<IncidentListItemDto>>> List(
        [FromQuery] PagedQuery query,
        [FromQuery] IncidentStatus? status,
        [FromQuery] IncidentSeverity? severity,
        [FromQuery] IncidentCategory? category,
        [FromQuery] bool openOnly,
        CancellationToken cancellationToken) =>
        Ok(await _incidents.ListAsync(query, status, severity, category, openOnly, cancellationToken));

    [HttpGet("stats")]
    [Authorize(Policy = NexusPermissions.IncidentsRead)]
    public async Task<ActionResult<IncidentStatsDto>> Stats(CancellationToken cancellationToken) =>
        Ok(await _incidents.GetStatsAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NexusPermissions.IncidentsRead)]
    public async Task<ActionResult<IncidentDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _incidents.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = NexusPermissions.IncidentsWrite)]
    public async Task<ActionResult<IncidentDetailDto>> Create([FromBody] CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        var created = await _incidents.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = NexusPermissions.IncidentsWrite)]
    public async Task<ActionResult<IncidentDetailDto>> Update(Guid id, [FromBody] UpdateIncidentRequest request, CancellationToken cancellationToken) =>
        Ok(await _incidents.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = NexusPermissions.IncidentsWrite)]
    public async Task<ActionResult<IncidentDetailDto>> AddNote(Guid id, [FromBody] AddIncidentNoteRequest request, CancellationToken cancellationToken) =>
        Ok(await _incidents.AddNoteAsync(id, request.Detail, cancellationToken));
}
