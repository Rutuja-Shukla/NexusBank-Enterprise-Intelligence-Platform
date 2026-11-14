using NexusBank.Domain.Enums;

namespace NexusBank.Domain.Entities;

public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Segment { get; set; } = "Personal";
    public KycStatus KycStatus { get; set; } = KycStatus.Verified;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string ProductType { get; set; } = "Everyday";
    public string Currency { get; set; } = "NZD";
    public decimal Balance { get; set; }
    public decimal AvailableBalance { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;

    public Customer? Customer { get; set; }
}

/// <summary>
/// A payment instruction. Status transitions are domain-guarded and recorded as
/// <see cref="PaymentEvent"/> steps so the journey is fully reconstructable.
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public PaymentType Type { get; set; } = PaymentType.Transfer;
    public PaymentStatus Status { get; set; } = PaymentStatus.Initiated;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NZD";
    public Guid? DebtorAccountId { get; set; }
    public string DebtorAccountNumber { get; set; } = string.Empty;
    public string CreditorName { get; set; } = string.Empty;
    public string CreditorAccountNumber { get; set; } = string.Empty;
    public string? CreditorBic { get; set; }
    public string Channel { get; set; } = "API";
    public Guid? IntegrationId { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public int? ProcessingDurationMs { get; set; }
    public int RiskScore { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public Account? DebtorAccount { get; set; }
    public Integration? Integration { get; set; }
    public ICollection<PaymentEvent> Events { get; set; } = new List<PaymentEvent>();
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}

/// <summary>One hop in a payment's journey through the banking platform.</summary>
public class PaymentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public int Sequence { get; set; }
    public PaymentStep Step { get; set; }
    public PaymentStepStatus Status { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public int DurationMs { get; set; }
    public string? Detail { get; set; }
    public string? ErrorDetail { get; set; }
    public Payment? Payment { get; set; }
}

/// <summary>Simplified double-entry posting. Debits must equal credits per payment.</summary>
public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public Guid? AccountId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public LedgerEntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NZD";
    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; } = string.Empty;
    public Payment? Payment { get; set; }
}
