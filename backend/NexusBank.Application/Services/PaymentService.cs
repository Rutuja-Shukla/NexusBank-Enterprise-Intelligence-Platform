using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Exceptions;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Dtos;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Formatting;
using NexusBank.Shared.Paging;

namespace NexusBank.Application.Services;

/// <summary>
/// Orchestrates synthetic payment instructions. Every successful or failed payment is
/// persisted together with its journey steps, ledger postings and an outbox message in a
/// single SaveChanges call so the write is atomic.
/// </summary>
public sealed class PaymentService
{
    private const string PaymentTopic = "payments.events";
    private const string SuspenseAccount = "NEXUSBANK-SUSPENSE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PagedResult<PaymentListItemDto>> ListAsync(
        PagedQuery query,
        PaymentStatus? status,
        PaymentType? type,
        DateTimeOffset? from,
        DateTimeOffset? to,
        decimal? minAmount,
        CancellationToken cancellationToken = default)
    {
        var source = _db.Payments.AsNoTracking().Include(p => p.Integration).AsQueryable();

        if (status.HasValue) source = source.Where(p => p.Status == status);
        if (type.HasValue) source = source.Where(p => p.Type == type);
        if (from.HasValue) source = source.Where(p => p.CreatedAt >= from);
        if (to.HasValue) source = source.Where(p => p.CreatedAt <= to);
        if (minAmount.HasValue)
        {
            var threshold = (double)minAmount.Value;
            source = source.Where(p => (double)p.Amount >= threshold);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(p =>
                p.Reference.ToLower().Contains(term) ||
                p.CreditorName.ToLower().Contains(term) ||
                p.CorrelationId.ToLower().Contains(term));
        }

        source = (query.Sort ?? "-created").ToLowerInvariant() switch
        {
            "created" => source.OrderBy(p => p.CreatedAt),
            "amount" => source.OrderBy(p => p.Amount),
            "-amount" => source.OrderByDescending(p => p.Amount),
            "reference" => source.OrderBy(p => p.Reference),
            "status" => source.OrderBy(p => p.Status).ThenByDescending(p => p.CreatedAt),
            _ => source.OrderByDescending(p => p.CreatedAt)
        };

        var total = await source.CountAsync(cancellationToken);
        var items = await source.Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<PaymentListItemDto>(items.Select(MapListItem).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<PaymentDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Integration)
            .Include(p => p.Events)
            .Include(p => p.LedgerEntries)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Payment", id);

        var journey = payment.Events
            .OrderBy(e => e.Sequence)
            .Select(e => new PaymentStepDto
            {
                Sequence = e.Sequence,
                Step = e.Step,
                Status = e.Status,
                ServiceName = e.ServiceName,
                Timestamp = e.Timestamp,
                DurationMs = e.DurationMs,
                OffsetMs = Math.Max(0, (int)(e.Timestamp - payment.CreatedAt).TotalMilliseconds),
                Detail = e.Detail,
                ErrorDetail = e.ErrorDetail
            }).ToList();

        var ledger = payment.LedgerEntries
            .OrderBy(l => l.EntryType)
            .Select(l => new LedgerEntryDto
            {
                EntryType = l.EntryType,
                AccountNumber = Masking.AccountNumber(l.AccountNumber),
                Amount = l.Amount,
                Currency = l.Currency,
                PostedAt = l.PostedAt,
                Description = l.Description
            }).ToList();

        var debitTotal = payment.LedgerEntries.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount);
        var creditTotal = payment.LedgerEntries.Where(l => l.EntryType == LedgerEntryType.Credit).Sum(l => l.Amount);

        return new PaymentDetailDto
        {
            Id = payment.Id,
            Reference = payment.Reference,
            Status = payment.Status,
            Type = payment.Type,
            Amount = payment.Amount,
            Currency = payment.Currency,
            DebtorAccountNumber = Masking.AccountNumber(payment.DebtorAccountNumber),
            CreditorName = payment.CreditorName,
            CreditorAccountNumber = Masking.AccountNumber(payment.CreditorAccountNumber),
            CreditorBic = payment.CreditorBic,
            Channel = payment.Channel,
            IntegrationId = payment.IntegrationId,
            IntegrationName = payment.Integration?.Name,
            Description = payment.Description,
            FailureCode = payment.FailureCode,
            FailureReason = payment.FailureReason,
            RiskScore = payment.RiskScore,
            ProcessingDurationMs = payment.ProcessingDurationMs,
            CorrelationId = payment.CorrelationId,
            IdempotencyKey = string.IsNullOrEmpty(payment.IdempotencyKey)
                ? "—"
                : $"{payment.IdempotencyKey[..Math.Min(8, payment.IdempotencyKey.Length)]}…",
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt,
            CompletedAt = payment.CompletedAt,
            Journey = journey,
            LedgerEntries = ledger,
            LedgerDebitTotal = debitTotal,
            LedgerCreditTotal = creditTotal,
            LedgerBalanced = payment.LedgerEntries.Count == 0 || debitTotal == creditTotal
        };
    }

    public async Task<PaymentCreatedDto> CreateAsync(CreatePaymentRequest request, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var hash = HashRequest(request);
            var existing = await _db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Scope == "payments" && r.Key == idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                if (existing.RequestHash != hash)
                    throw new ConflictException("This idempotency key was already used with a different request payload.", "idempotency_key_reuse");

                var replayed = JsonSerializer.Deserialize<PaymentCreatedDto>(existing.ResponseJson, JsonOptions)
                    ?? throw new ConflictException("Stored idempotent response could not be read.", "idempotency_corrupt");
                return replayed with { Replayed = true };
            }
        }

        var account = await ResolveDebtorAsync(request, cancellationToken);
        ValidateInstruction(request, account);

        var now = _clock.UtcNow;
        var correlationId = References.Correlation();
        var payment = new Payment
        {
            Reference = References.Payment(),
            IdempotencyKey = idempotencyKey ?? string.Empty,
            Type = request.Type,
            Status = PaymentStatus.Initiated,
            Amount = decimal.Round(request.Amount, 2, MidpointRounding.ToEven),
            Currency = request.Currency.ToUpperInvariant(),
            DebtorAccountId = account.Id,
            DebtorAccountNumber = account.AccountNumber,
            CreditorName = request.CreditorName.Trim(),
            CreditorAccountNumber = request.CreditorAccountNumber.Trim(),
            CreditorBic = string.IsNullOrWhiteSpace(request.CreditorBic) ? null : request.CreditorBic.Trim().ToUpperInvariant(),
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "API" : request.Channel.Trim(),
            Description = request.Description?.Trim(),
            CorrelationId = correlationId,
            InitiatedByUserId = _currentUser.Id,
            RiskScore = RiskScoreFor(request, account),
            CreatedAt = now,
            UpdatedAt = now
        };

        var targetIntegrationKey = payment.CreditorBic == null ? "payment-processing" : "external-payment-gateway";
        var router = await _db.Integrations
            .AsNoTracking()
            .Where(i => i.Key == targetIntegrationKey)
            .Select(i => new { i.Id })
            .FirstOrDefaultAsync(cancellationToken);
        payment.IntegrationId = router?.Id;

        var cursor = now;
        var totalDuration = 0;
        var sequence = 0;

        void Step(PaymentStep step, PaymentStepStatus status, string service, int duration, string detail, string? error = null)
        {
            cursor = cursor.AddMilliseconds(duration);
            totalDuration += duration;
            _db.PaymentEvents.Add(new PaymentEvent
            {
                PaymentId = payment.Id,
                Sequence = ++sequence,
                Step = step,
                Status = status,
                ServiceName = service,
                Timestamp = cursor,
                DurationMs = duration,
                Detail = detail,
                ErrorDetail = error
            });
        }

        Step(PaymentStep.Initiated, PaymentStepStatus.Succeeded, "NexusBank API", Next(4, 14),
            $"Instruction accepted on the {payment.Channel} channel.");
        Step(PaymentStep.Validation, PaymentStepStatus.Succeeded, "Validation Service", Next(18, 42),
            "Amount, currency, account status and instruction schema validated.");

        var creditorAccount = await FindAccountAsync(payment.CreditorAccountNumber, cancellationToken);
        Step(PaymentStep.AccountLookup, PaymentStepStatus.Succeeded, "Account Management Service", Next(12, 32),
            creditorAccount is null
                ? "Beneficiary is held at an external institution; routing to the payment gateway."
                : "Beneficiary account located and currency matched.");

        var fails = request.SimulateFailure;
        var failureAtGateway = payment.CreditorBic is not null;
        var failureCode = failureAtGateway ? "GATEWAY_TIMEOUT" : "PROCESSOR_REJECTED";
        var failureReason = failureAtGateway
            ? "External payment gateway did not respond within the 10s timeout after 3 retries."
            : "Payment processor rejected the instruction during posting (synthetic failure requested).";

        if (fails && !failureAtGateway)
        {
            Step(PaymentStep.PaymentProcessing, PaymentStepStatus.Failed, "Payment Processing Service", Next(38, 70),
                "Instruction rejected by the processor.", failureReason);
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = failureCode;
            payment.FailureReason = failureReason;
        }
        else
        {
            Step(PaymentStep.PaymentProcessing, PaymentStepStatus.Succeeded, "Payment Processing Service", Next(40, 88),
                "Funds reserved and ledger postings prepared.");

            if (payment.CreditorBic is not null)
            {
                if (fails)
                {
                    Step(PaymentStep.ExternalGateway, PaymentStepStatus.Failed, "External Payment Gateway", 10_000,
                        "Gateway call timed out.", failureReason);
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureCode = failureCode;
                    payment.FailureReason = failureReason;
                }
                else
                {
                    Step(PaymentStep.ExternalGateway, PaymentStepStatus.Succeeded, "External Payment Gateway", Next(180, 640),
                        "Instruction accepted by the beneficiary institution.");
                }
            }

            if (payment.Status != PaymentStatus.Failed)
            {
                Step(PaymentStep.Confirmation, PaymentStepStatus.Succeeded, "Payment Processing Service", Next(8, 22),
                    "Payment confirmed and receipt generated.");
                payment.Status = PaymentStatus.Completed;
                payment.CompletedAt = cursor;
                PostLedger(payment, account, creditorAccount, cursor);
            }
        }

        payment.ProcessingDurationMs = totalDuration;
        payment.UpdatedAt = cursor;

        if (payment.Status == PaymentStatus.Completed)
        {
            account.AvailableBalance -= payment.Amount;
            account.Balance -= payment.Amount;
        }

        if (creditorAccount is not null && payment.Status == PaymentStatus.Completed)
        {
            creditorAccount.AvailableBalance += payment.Amount;
            creditorAccount.Balance += payment.Amount;
        }

        _db.Payments.Add(payment);
        AddOutbox(payment.Status == PaymentStatus.Completed ? "payment.completed" : "payment.failed", payment, cursor);
        Audit("payment.create", payment.Id.ToString(),
            $"{payment.Reference} {payment.Status} {payment.Amount:0.00} {payment.Currency} for {payment.CreditorName}");

        var created = new PaymentCreatedDto
        {
            Id = payment.Id,
            Reference = payment.Reference,
            Status = payment.Status,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CreatedAt = payment.CreatedAt,
            Replayed = false,
            CorrelationId = payment.CorrelationId
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Scope = "payments",
                Key = idempotencyKey,
                RequestHash = HashRequest(request),
                StatusCode = payment.Status == PaymentStatus.Completed ? 201 : 202,
                ResponseJson = JsonSerializer.Serialize(created, JsonOptions),
                CreatedAt = now,
                ExpiresAt = now.AddHours(24)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment {Reference} {Status} amount {Amount} {Currency} correlation {CorrelationId}",
            payment.Reference, payment.Status, payment.Amount, payment.Currency, payment.CorrelationId);

        return created;
    }

    public async Task<PaymentCreatedDto> RefundAsync(Guid id, RefundPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var original = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Payment", id);

        if (original.Status != PaymentStatus.Completed)
            throw new ConflictException($"Only completed payments can be refunded. {original.Reference} is {original.Status}.", "invalid_state");

        var amount = decimal.Round(request.Amount ?? original.Amount, 2, MidpointRounding.ToEven);
        if (amount <= 0) throw new ValidationFailedException(new Dictionary<string, string[]> { ["amount"] = new[] { "Refund amount must be greater than zero." } });
        if (amount > original.Amount) throw new ValidationFailedException(new Dictionary<string, string[]> { ["amount"] = new[] { "Refund amount cannot exceed the original payment amount." } });

        var now = _clock.UtcNow;
        var refund = new Payment
        {
            Reference = References.Payment(),
            Type = PaymentType.Refund,
            Status = PaymentStatus.Completed,
            Amount = amount,
            Currency = original.Currency,
            DebtorAccountId = original.DebtorAccountId,
            DebtorAccountNumber = original.DebtorAccountNumber,
            CreditorName = $"Refund · {original.Reference}",
            CreditorAccountNumber = original.DebtorAccountNumber,
            Channel = "Operations",
            Description = string.IsNullOrWhiteSpace(request.Reason) ? $"Refund of {original.Reference}" : request.Reason.Trim(),
            CorrelationId = References.Correlation(),
            InitiatedByUserId = _currentUser.Id,
            RiskScore = 0,
            CreatedAt = now,
            CompletedAt = now,
            UpdatedAt = now,
            ProcessingDurationMs = Next(30, 90)
        };

        _db.PaymentEvents.Add(new PaymentEvent
        {
            PaymentId = refund.Id,
            Sequence = 1,
            Step = PaymentStep.Initiated,
            Status = PaymentStepStatus.Succeeded,
            ServiceName = "Operations Console",
            Timestamp = now,
            DurationMs = 0,
            Detail = $"Refund raised against {original.Reference} by {_currentUser.Email}."
        });
        _db.PaymentEvents.Add(new PaymentEvent
        {
            PaymentId = refund.Id,
            Sequence = 2,
            Step = PaymentStep.Confirmation,
            Status = PaymentStepStatus.Succeeded,
            ServiceName = "Payment Processing Service",
            Timestamp = now.AddMilliseconds(refund.ProcessingDurationMs ?? 0),
            DurationMs = refund.ProcessingDurationMs ?? 0,
            Detail = "Refund settled to the original debtor account."
        });

        var account = original.DebtorAccountId is null
            ? null
            : await _db.Accounts.FirstOrDefaultAsync(a => a.Id == original.DebtorAccountId, cancellationToken);
        if (account is not null)
        {
            account.Balance += amount;
            account.AvailableBalance += amount;
        }

        _db.LedgerEntries.Add(new LedgerEntry
        {
            PaymentId = refund.Id,
            AccountNumber = SuspenseAccount,
            EntryType = LedgerEntryType.Debit,
            Amount = amount,
            Currency = refund.Currency,
            PostedAt = now,
            Description = $"Refund funding for {original.Reference}"
        });
        _db.LedgerEntries.Add(new LedgerEntry
        {
            PaymentId = refund.Id,
            AccountId = account?.Id,
            AccountNumber = original.DebtorAccountNumber,
            EntryType = LedgerEntryType.Credit,
            Amount = amount,
            Currency = refund.Currency,
            PostedAt = now,
            Description = $"Refund credit for {original.Reference}"
        });

        _db.Payments.Add(refund);
        AddOutbox("payment.refunded", refund, now);
        Audit("payment.refund", original.Id.ToString(), $"Refund {refund.Reference} of {amount:0.00} {refund.Currency} against {original.Reference}");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Refund {Refund} issued against {Original}", refund.Reference, original.Reference);

        return new PaymentCreatedDto
        {
            Id = refund.Id,
            Reference = refund.Reference,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            CreatedAt = refund.CreatedAt,
            Replayed = false,
            CorrelationId = refund.CorrelationId
        };
    }

    public async Task<PaymentDetailDto> ReverseAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Payment", id);

        if (payment.Status != PaymentStatus.Completed)
            throw new ConflictException($"Only completed payments can be reversed. {payment.Reference} is {payment.Status}.", "invalid_state");

        var now = _clock.UtcNow;
        payment.Status = PaymentStatus.Reversed;
        payment.UpdatedAt = now;

        _db.PaymentEvents.Add(new PaymentEvent
        {
            PaymentId = payment.Id,
            Sequence = payment.Events.Count + 1,
            Step = PaymentStep.Reversal,
            Status = PaymentStepStatus.Succeeded,
            ServiceName = "Payment Processing Service",
            Timestamp = now,
            DurationMs = Next(20, 55),
            Detail = string.IsNullOrWhiteSpace(reason)
                ? $"Reversal processed by {_currentUser.Email}."
                : $"Reversal processed by {_currentUser.Email}: {reason.Trim()}"
        });

        var account = payment.DebtorAccountId is null
            ? null
            : await _db.Accounts.FirstOrDefaultAsync(a => a.Id == payment.DebtorAccountId, cancellationToken);
        if (account is not null)
        {
            account.Balance += payment.Amount;
            account.AvailableBalance += payment.Amount;
        }

        var creditorAccount = await FindAccountAsync(payment.CreditorAccountNumber, cancellationToken);
        if (creditorAccount is not null && creditorAccount.Id != account?.Id)
        {
            creditorAccount.Balance -= payment.Amount;
            creditorAccount.AvailableBalance -= payment.Amount;
        }

        _db.LedgerEntries.Add(new LedgerEntry
        {
            PaymentId = payment.Id,
            AccountId = account?.Id,
            AccountNumber = payment.DebtorAccountNumber,
            EntryType = LedgerEntryType.Credit,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PostedAt = now,
            Description = $"Reversal credit for {payment.Reference}"
        });
        _db.LedgerEntries.Add(new LedgerEntry
        {
            PaymentId = payment.Id,
            AccountId = creditorAccount?.Id,
            AccountNumber = creditorAccount?.AccountNumber ?? SuspenseAccount,
            EntryType = LedgerEntryType.Debit,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PostedAt = now,
            Description = $"Reversal debit for {payment.Reference}"
        });

        AddOutbox("payment.reversed", payment, now);
        Audit("payment.reverse", payment.Id.ToString(), $"Reversed {payment.Reference}: {reason}");
        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(payment.Id, cancellationToken);
    }

    public async Task<PaymentStatsDto> GetStatsAsync(DateTimeOffset? from, CancellationToken cancellationToken = default)
    {
        var windowStart = from ?? _clock.UtcNow.AddDays(-30);
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.CreatedAt >= windowStart)
            .Select(p => new { p.Status, p.Type, p.Amount, p.ProcessingDurationMs })
            .ToListAsync(cancellationToken);

        var completed = rows.Count(r => r.Status == PaymentStatus.Completed);
        var failed = rows.Count(r => r.Status == PaymentStatus.Failed);
        var decided = completed + failed;

        return new PaymentStatsDto
        {
            TotalCount = rows.Count,
            TotalVolume = rows.Sum(r => r.Amount),
            SuccessRatePct = decided == 0 ? 100 : Math.Round(completed * 100.0 / decided, 2),
            FailedCount = failed,
            PendingCount = rows.Count(r => r.Status is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.Validating),
            AverageProcessingMs = rows.Where(r => r.ProcessingDurationMs.HasValue).Select(r => r.ProcessingDurationMs!.Value).DefaultIfEmpty(0).Average(),
            ByStatus = rows.GroupBy(r => r.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            ByType = rows.GroupBy(r => r.Type.ToString()).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public async Task<IReadOnlyList<AccountOptionDto>> GetAccountOptionsAsync(string? search, CancellationToken cancellationToken = default)
    {
        var accounts = _db.Accounts.AsNoTracking().Include(a => a.Customer).Where(a => a.Status == AccountStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            accounts = accounts.Where(a =>
                a.AccountNumber.Contains(term) ||
                a.Customer!.FirstName.ToLower().Contains(term) ||
                a.Customer.LastName.ToLower().Contains(term));
        }

        var rows = await accounts.OrderBy(a => a.AccountNumber).Take(50).ToListAsync(cancellationToken);
        return rows.Select(a => new AccountOptionDto
        {
            Id = a.Id,
            Label = $"{a.Customer?.FirstName} {a.Customer?.LastName} · {a.Currency}",
            MaskedNumber = Masking.AccountNumber(a.AccountNumber),
            Currency = a.Currency,
            AvailableBalance = a.AvailableBalance,
            CustomerName = $"{a.Customer?.FirstName} {a.Customer?.LastName}"
        }).ToList();
    }

    private async Task<Account> ResolveDebtorAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        if (request.DebtorAccountId.HasValue)
        {
            return await _db.Accounts.Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.Id == request.DebtorAccountId, cancellationToken)
                ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["debtorAccountId"] = new[] { "Selected debtor account no longer exists." } });
        }

        if (!string.IsNullOrWhiteSpace(request.DebtorAccountNumber))
        {
            var digits = new string(request.DebtorAccountNumber.Where(char.IsDigit).ToArray());
            return await _db.Accounts.Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.AccountNumber == digits, cancellationToken)
                ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["debtorAccountNumber"] = new[] { "No account matches that number." } });
        }

        var fallback = await _db.Accounts.Include(a => a.Customer)
            .Where(a => a.Status == AccountStatus.Active)
            .OrderBy(a => a.AccountNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("No synthetic accounts are available to fund this payment.", "no_accounts");

        return fallback;
    }

    private void ValidateInstruction(CreatePaymentRequest request, Account account)
    {
        var errors = new Dictionary<string, string[]>();

        if (account.Status != AccountStatus.Active)
            errors["debtorAccountId"] = new[] { "The debtor account is not active." };
        if (request.Amount <= 0)
            errors["amount"] = new[] { "Amount must be greater than zero." };
        if (!string.Equals(account.Currency, request.Currency, StringComparison.OrdinalIgnoreCase))
            errors["currency"] = new[] { $"Account currency {account.Currency} does not match instruction currency {request.Currency}." };
        if (string.IsNullOrWhiteSpace(request.CreditorName))
            errors["creditorName"] = new[] { "Beneficiary name is required." };
        if (string.IsNullOrWhiteSpace(request.CreditorAccountNumber))
            errors["creditorAccountNumber"] = new[] { "Beneficiary account number is required." };
        if (request.Amount > account.AvailableBalance)
            errors["amount"] = new[] { $"Insufficient available funds. Available balance is {account.AvailableBalance:N2} {account.Currency}." };

        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }

    private static int RiskScoreFor(CreatePaymentRequest request, Account account)
    {
        var score = 8;
        if (request.Amount > 5000) score += 18;
        if (request.Amount > 20000) score += 24;
        if (request.CreditorBic is not null) score += 12;
        if (account.Balance > 0 && request.Amount / Math.Max(account.Balance, 1) > 0.8m) score += 14;
        return Math.Min(99, score);
    }

    private void PostLedger(Payment payment, Account debtor, Account? creditor, DateTimeOffset postedAt)
    {
        _db.LedgerEntries.Add(new LedgerEntry
        {
            PaymentId = payment.Id,
            AccountId = debtor.Id,
            AccountNumber = debtor.AccountNumber,
            EntryType = LedgerEntryType.Debit,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PostedAt = postedAt,
            Description = $"Debit for {payment.Reference} to {payment.CreditorName}"
        });

        _db.LedgerEntries.Add(new LedgerEntry
        {
            PaymentId = payment.Id,
            AccountId = creditor?.Id,
            AccountNumber = creditor?.AccountNumber ?? SuspenseAccount,
            EntryType = LedgerEntryType.Credit,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PostedAt = postedAt,
            Description = $"Credit for {payment.Reference} to {payment.CreditorName}"
        });
    }

    private async Task<Account?> FindAccountAsync(string accountNumber, CancellationToken cancellationToken)
    {
        var digits = new string(accountNumber.Where(char.IsDigit).ToArray());
        if (digits.Length < 6) return null;
        return await _db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == digits, cancellationToken);
    }

    private void AddOutbox(string eventType, Payment payment, DateTimeOffset occurredAt)
    {
        var payload = JsonSerializer.Serialize(new
        {
            paymentId = payment.Id,
            reference = payment.Reference,
            status = payment.Status.ToString(),
            amount = payment.Amount,
            currency = payment.Currency,
            occurredAt
        }, JsonOptions);

        _db.OutboxMessages.Add(new OutboxMessage
        {
            Type = eventType,
            Topic = PaymentTopic,
            Payload = payload,
            OccurredAt = occurredAt
        });
    }

    private void Audit(string action, string entityId, string detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.UtcNow,
            ActorUserId = _currentUser.Id,
            ActorEmail = _currentUser.Email,
            Action = action,
            EntityType = "Payment",
            EntityId = entityId,
            Outcome = AuditOutcome.Success,
            Severity = "info",
            IpAddress = _currentUser.IpAddress,
            Metadata = detail
        });
    }

    private static string HashRequest(CreatePaymentRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.DebtorAccountId,
            request.DebtorAccountNumber,
            request.CreditorName,
            request.CreditorAccountNumber,
            request.CreditorBic,
            request.Amount,
            request.Currency,
            request.Type,
            request.SimulateFailure
        }, JsonOptions);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static int Next(int min, int max) => Random.Shared.Next(min, max);

    private static PaymentListItemDto MapListItem(Payment p) => new()
    {
        Id = p.Id,
        Reference = p.Reference,
        Status = p.Status,
        Type = p.Type,
        Amount = p.Amount,
        Currency = p.Currency,
        CreditorName = p.CreditorName,
        DebtorAccountNumber = Masking.AccountNumber(p.DebtorAccountNumber),
        Channel = p.Channel,
        IntegrationName = p.Integration?.Name,
        RiskScore = p.RiskScore,
        ProcessingDurationMs = p.ProcessingDurationMs,
        CreatedAt = p.CreatedAt,
        CompletedAt = p.CompletedAt
    };
}
