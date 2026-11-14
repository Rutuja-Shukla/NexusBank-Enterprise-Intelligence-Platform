using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize]
public sealed class EventsController : ControllerBase
{
    private readonly EventStreamService _events;

    public EventsController(EventStreamService events) => _events = events;

    [HttpGet]
    [Authorize(Policy = NexusPermissions.EventsRead)]
    public async Task<ActionResult<PagedResult<KafkaEventDto>>> List(
        [FromQuery] PagedQuery query,
        [FromQuery] Guid? topicId,
        [FromQuery] KafkaEventStatus? status,
        [FromQuery] bool deadLettersOnly,
        CancellationToken cancellationToken) =>
        Ok(await _events.GetEventsAsync(query, topicId, status, deadLettersOnly, cancellationToken));

    [HttpGet("topics")]
    [Authorize(Policy = NexusPermissions.EventsRead)]
    public async Task<ActionResult<IReadOnlyList<TopicDto>>> Topics(CancellationToken cancellationToken) =>
        Ok(await _events.GetTopicsAsync(cancellationToken));

    [HttpGet("consumer-groups")]
    [Authorize(Policy = NexusPermissions.EventsRead)]
    public async Task<ActionResult<IReadOnlyList<ConsumerGroupDto>>> ConsumerGroups(CancellationToken cancellationToken) =>
        Ok(await _events.GetConsumerGroupsAsync(cancellationToken));

    [HttpGet("status")]
    [Authorize(Policy = NexusPermissions.EventsRead)]
    public async Task<ActionResult<StreamStatusDto>> Status(CancellationToken cancellationToken) =>
        Ok(await _events.GetStatusAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NexusPermissions.EventsRead)]
    public async Task<ActionResult<KafkaEventDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _events.GetEventAsync(id, cancellationToken));

    [HttpPost("publish")]
    [Authorize(Policy = NexusPermissions.EventsWrite)]
    public async Task<ActionResult<KafkaEventDto>> Publish([FromBody] PublishEventRequest request, CancellationToken cancellationToken) =>
        Ok(await _events.PublishAsync(request, cancellationToken));

    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = NexusPermissions.EventsWrite)]
    public async Task<ActionResult<KafkaEventDto>> Retry(Guid id, CancellationToken cancellationToken) =>
        Ok(await _events.RetryAsync(id, cancellationToken));

    [HttpPost("{id:guid}/dead-letter")]
    [Authorize(Policy = NexusPermissions.EventsWrite)]
    public async Task<ActionResult<KafkaEventDto>> DeadLetter(Guid id, [FromBody] DeadLetterRequest request, CancellationToken cancellationToken) =>
        Ok(await _events.DeadLetterAsync(id, request.Reason, cancellationToken));
}

public sealed record DeadLetterRequest
{
    public string Reason { get; init; } = string.Empty;
}
