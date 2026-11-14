using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Dtos;
using NexusBank.Application.Services;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Tests.Support;

namespace NexusBank.Tests;

public sealed class PaymentServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FixedClock _clock = new();
    private readonly TestCurrentUser _currentUser = new();
    private Account _debtor = null!;

    private PaymentService CreateService() =>
        new(_database.Db, _currentUser, _clock, NullLogger<PaymentService>.Instance);

    private async Task<Account> SeedDebtorAsync(decimal balance = 25_000m)
    {
        var customer = new Customer
        {
            CustomerNumber = "C-000123",
            FirstName = "Aroha",
            LastName = "Ngata",
            Email = "aro-ha@example.test",
            Segment = "Personal",
            KycStatus = KycStatus.Verified
        };

        _debtor = new Account
        {
            CustomerId = customer.Id,
            Customer = customer,
            AccountNumber = "12-3456-0123456-00",
            Iban = "NZ00NEXU000000012345600",
            ProductType = "Everyday",
            Currency = "NZD",
            Balance = balance,
            AvailableBalance = balance,
            Status = AccountStatus.Active
        };

        _database.Db.Customers.Add(customer);
        _database.Db.Accounts.Add(_debtor);
        await _database.Db.SaveChangesAsync();
        return _debtor;
    }

    private static CreatePaymentRequest Request(decimal amount = 250.75m, bool simulateFailure = false, string? bic = null) => new()
    {
        CreditorName = "Harbour Consulting Ltd",
        CreditorAccountNumber = "99-8888-0777777-01",
        CreditorBic = bic,
        Amount = amount,
        Currency = "NZD",
        Type = PaymentType.Transfer,
        Description = "Invoice 2026-114",
        SimulateFailure = simulateFailure
    };

    [Fact]
    public async Task Create_CompletesPayment_WithBalancedDoubleEntryLedger()
    {
        var account = await SeedDebtorAsync();
        var service = CreateService();

        var created = await service.CreateAsync(Request(), null);

        Assert.Equal(PaymentStatus.Completed, created.Status);
        Assert.False(created.Replayed);
        Assert.False(string.IsNullOrWhiteSpace(created.Reference));

        var payment = await _database.Db.Payments
            .Include(p => p.Events)
            .Include(p => p.LedgerEntries)
            .SingleAsync(p => p.Id == created.Id);

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.NotNull(payment.CompletedAt);
        Assert.True(payment.Events.Count >= 5);
        Assert.Equal(new[] { PaymentStep.Initiated, PaymentStep.Validation }, payment.Events.OrderBy(e => e.Sequence).Take(2).Select(e => e.Step));

        var debits = payment.LedgerEntries.Where(e => e.EntryType == LedgerEntryType.Debit).Sum(e => e.Amount);
        var credits = payment.LedgerEntries.Where(e => e.EntryType == LedgerEntryType.Credit).Sum(e => e.Amount);
        Assert.Equal(debits, credits);
        Assert.Equal(250.75m, debits);

        var reloaded = await _database.Db.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.Equal(25_000m - 250.75m, reloaded.Balance);
        var outbox = Assert.Single(_database.Db.OutboxMessages);
        Assert.Equal("payment.completed", outbox.Type);
        Assert.Contains(payment.Reference, outbox.Payload);
    }

    [Fact]
    public async Task Create_WithExistingIdempotencyKey_ReplaysWithoutDuplicating()
    {
        await SeedDebtorAsync();
        var service = CreateService();

        var first = await service.CreateAsync(Request(), "idem-key-001");
        var second = await service.CreateAsync(Request(), "idem-key-001");

        Assert.Equal(first.Id, second.Id);
        Assert.True(second.Replayed);
        Assert.Equal(1, await _database.Db.Payments.CountAsync());
        Assert.Equal(1, await _database.Db.IdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task Create_WithReusedKeyAndDifferentPayload_IsRejected()
    {
        await SeedDebtorAsync();
        var service = CreateService();

        await service.CreateAsync(Request(), "idem-key-002");

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(amount: 999m), "idem-key-002"));
        Assert.Equal(1, await _database.Db.Payments.CountAsync());
    }

    [Fact]
    public async Task Create_WhenProcessorRejects_RecordsFailureWithoutLedgerPostings()
    {
        var account = await SeedDebtorAsync();
        var service = CreateService();

        var created = await service.CreateAsync(Request(simulateFailure: true), null);

        Assert.Equal(PaymentStatus.Failed, created.Status);

        var payment = await _database.Db.Payments.Include(p => p.Events).Include(p => p.LedgerEntries).SingleAsync(p => p.Id == created.Id);
        Assert.Equal("PROCESSOR_REJECTED", payment.FailureCode);
        Assert.NotNull(payment.FailureReason);
        Assert.Empty(payment.LedgerEntries);
        Assert.Contains(payment.Events, e => e.Status == PaymentStepStatus.Failed);

        var unchanged = await _database.Db.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.Equal(25_000m, unchanged.Balance);
    }

    [Fact]
    public async Task Create_WithBic_RoutesThroughExternalGateway()
    {
        await SeedDebtorAsync();
        var service = CreateService();

        var created = await service.CreateAsync(Request(bic: "NEXUNZ2A"), null);

        var payment = await _database.Db.Payments.Include(p => p.Events).SingleAsync(p => p.Id == created.Id);
        Assert.Contains(payment.Events, e => e.Step == PaymentStep.ExternalGateway && e.Status == PaymentStepStatus.Succeeded);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
    }

    [Fact]
    public async Task Create_WithInsufficientFunds_IsRejected()
    {
        await SeedDebtorAsync(balance: 10m);
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationFailedException>(() => service.CreateAsync(Request(amount: 500m), null));
        Assert.Equal(0, await _database.Db.Payments.CountAsync());
    }

    public void Dispose() => _database.Dispose();
}
