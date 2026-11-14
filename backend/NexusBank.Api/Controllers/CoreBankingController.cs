using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Shared.Paging;
using NexusBank.Shared.Security;

namespace NexusBank.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = NexusPermissions.PaymentsRead)]
public sealed class CoreBankingController : ControllerBase
{
    private readonly CoreBankingService _coreBanking;

    public CoreBankingController(CoreBankingService coreBanking) => _coreBanking = coreBanking;

    [HttpGet("customers")]
    public async Task<ActionResult<PagedResult<CustomerDto>>> Customers([FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        Ok(await _coreBanking.GetCustomersAsync(query, cancellationToken));

    [HttpGet("accounts")]
    public async Task<ActionResult<PagedResult<AccountDto>>> Accounts([FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        Ok(await _coreBanking.GetAccountsAsync(query, cancellationToken));
}
