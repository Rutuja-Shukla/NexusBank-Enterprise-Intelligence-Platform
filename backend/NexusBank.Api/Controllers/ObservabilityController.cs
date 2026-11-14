using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/observability")]
[Authorize(Policy = NexusPermissions.ObservabilityRead)]
public sealed class ObservabilityController : ControllerBase
{
    private readonly ObservabilityService _observability;

    public ObservabilityController(ObservabilityService observability) => _observability = observability;

    [HttpGet("overview")]
    public async Task<ActionResult<ObservabilityOverviewDto>> Overview([FromQuery] int hours, CancellationToken cancellationToken) =>
        Ok(await _observability.GetOverviewAsync(hours == 0 ? 24 : hours, cancellationToken));

    [HttpGet("traffic")]
    public async Task<ActionResult<IReadOnlyList<TrafficPointDto>>> Traffic([FromQuery] int hours, CancellationToken cancellationToken) =>
        Ok(await _observability.GetTrafficAsync(hours == 0 ? 24 : hours, cancellationToken));

    [HttpGet("latency")]
    public async Task<ActionResult<IReadOnlyList<LatencyDistributionDto>>> Latency([FromQuery] int hours, CancellationToken cancellationToken) =>
        Ok(await _observability.GetLatencyDistributionAsync(hours == 0 ? 24 : hours, cancellationToken));

    [HttpGet("errors")]
    public async Task<ActionResult<IReadOnlyList<ErrorGroupDto>>> Errors([FromQuery] int hours, [FromQuery] int limit, CancellationToken cancellationToken) =>
        Ok(await _observability.GetErrorsAsync(hours == 0 ? 24 : hours, limit == 0 ? 20 : limit, cancellationToken));

    [HttpGet("traces")]
    public async Task<ActionResult<IReadOnlyList<TraceDto>>> Traces([FromQuery] int limit, [FromQuery] bool errorsOnly, CancellationToken cancellationToken) =>
        Ok(await _observability.GetTracesAsync(limit == 0 ? 40 : limit, errorsOnly, cancellationToken));

    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<LogEntryDto>>> Logs([FromQuery] int limit, [FromQuery] string? level, [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await _observability.GetLogsAsync(limit == 0 ? 80 : limit, level, search, cancellationToken));

    [HttpGet("alerts")]
    public async Task<ActionResult<AlertDto>> Alerts(CancellationToken cancellationToken) =>
        Ok(new AlertDto
        {
            Alerts = await _observability.GetAlertsAsync(cancellationToken),
            GeneratedAt = DateTimeOffset.UtcNow
        });

    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<ServiceNodeDto>>> Services(CancellationToken cancellationToken) =>
        Ok(await _observability.GetServicesAsync(cancellationToken));
}
