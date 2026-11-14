using Microsoft.EntityFrameworkCore;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Shared.Formatting;
using NexusBank.Shared.Paging;

namespace NexusBank.Application.Services;

public sealed class CoreBankingService
{
    private readonly IApplicationDbContext _db;

    public CoreBankingService(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<CustomerDto>> GetCustomersAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var source = _db.Customers.AsNoTracking().Include(c => c.Accounts).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(c =>
                c.CustomerNumber.ToLower().Contains(term) ||
                c.FirstName.ToLower().Contains(term) ||
                c.LastName.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<CustomerDto>(rows.Select(c => new CustomerDto
        {
            Id = c.Id,
            CustomerNumber = c.CustomerNumber,
            Name = $"{c.FirstName} {c.LastName}",
            Email = Masking.Email(c.Email),
            Segment = c.Segment,
            KycStatus = c.KycStatus,
            IsActive = c.IsActive,
            AccountCount = c.Accounts.Count,
            CreatedAt = c.CreatedAt
        }).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<PagedResult<AccountDto>> GetAccountsAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var source = _db.Accounts.AsNoTracking().Include(a => a.Customer).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(a =>
                a.AccountNumber.Contains(term) ||
                a.ProductType.ToLower().Contains(term) ||
                a.Customer!.LastName.ToLower().Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderBy(a => a.AccountNumber).Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<AccountDto>(rows.Select(a => new AccountDto
        {
            Id = a.Id,
            AccountNumber = Masking.AccountNumber(a.AccountNumber),
            CustomerName = $"{a.Customer?.FirstName} {a.Customer?.LastName}",
            ProductType = a.ProductType,
            Currency = a.Currency,
            Balance = a.Balance,
            AvailableBalance = a.AvailableBalance,
            Status = a.Status,
            OpenedAt = a.OpenedAt
        }).ToList(), query.Page, query.PageSize, total);
    }
}
