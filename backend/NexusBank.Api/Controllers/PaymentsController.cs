using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentsController(PaymentService payments) => _payments = payments;

    [HttpGet]
    [Authorize(Policy = NexusPermissions.PaymentsRead)]
    public async Task<ActionResult<PagedResult<PaymentListItemDto>>> List(
        [FromQuery] PagedQuery query,
        [FromQuery] PaymentStatus? status,
        [FromQuery] PaymentType? type,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] decimal? minAmount,
        CancellationToken cancellationToken) =>
        Ok(await _payments.ListAsync(query, status, type, from, to, minAmount, cancellationToken));

    [HttpGet("stats")]
    [Authorize(Policy = NexusPermissions.PaymentsRead)]
    public async Task<ActionResult<PaymentStatsDto>> Stats([FromQuery] DateTimeOffset? from, CancellationToken cancellationToken) =>
        Ok(await _payments.GetStatsAsync(from, cancellationToken));

    [HttpGet("accounts")]
    [Authorize(Policy = NexusPermissions.PaymentsRead)]
    public async Task<ActionResult<IReadOnlyList<AccountOptionDto>>> Accounts([FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await _payments.GetAccountOptionsAsync(search, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NexusPermissions.PaymentsRead)]
    public async Task<ActionResult<PaymentDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _payments.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = NexusPermissions.PaymentsWrite)]
    public async Task<ActionResult<PaymentCreatedDto>> Create(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var created = await _payments.CreateAsync(request, idempotencyKey, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = NexusPermissions.PaymentsWrite)]
    public async Task<ActionResult<PaymentCreatedDto>> Refund(Guid id, [FromBody] RefundPaymentRequest request, CancellationToken cancellationToken) =>
        Ok(await _payments.RefundAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reverse")]
    [Authorize(Policy = NexusPermissions.PaymentsWrite)]
    public async Task<ActionResult<PaymentDetailDto>> Reverse(Guid id, [FromBody] ReversePaymentRequest request, CancellationToken cancellationToken) =>
        Ok(await _payments.ReverseAsync(id, request.Reason, cancellationToken));
}
