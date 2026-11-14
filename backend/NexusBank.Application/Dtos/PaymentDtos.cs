using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record PaymentListItemDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public PaymentStatus Status { get; init; }
    public PaymentType Type { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NZD";
    public string CreditorName { get; init; } = string.Empty;
    public string DebtorAccountNumber { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string? IntegrationName { get; init; }
    public int RiskScore { get; init; }
    public int? ProcessingDurationMs { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record PaymentStepDto
{
    public int Sequence { get; init; }
    public PaymentStep Step { get; init; }
    public PaymentStepStatus Status { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public int DurationMs { get; init; }
    public int OffsetMs { get; init; }
    public string? Detail { get; init; }
    public string? ErrorDetail { get; init; }
}

public sealed record LedgerEntryDto
{
    public LedgerEntryType EntryType { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NZD";
    public DateTimeOffset PostedAt { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed record PaymentDetailDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public PaymentStatus Status { get; init; }
    public PaymentType Type { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NZD";
    public string DebtorAccountNumber { get; init; } = string.Empty;
    public string CreditorName { get; init; } = string.Empty;
    public string CreditorAccountNumber { get; init; } = string.Empty;
    public string? CreditorBic { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string? IntegrationName { get; init; }
    public Guid? IntegrationId { get; init; }
    public string? Description { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureReason { get; init; }
    public int RiskScore { get; init; }
    public int? ProcessingDurationMs { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyList<PaymentStepDto> Journey { get; init; } = Array.Empty<PaymentStepDto>();
    public IReadOnlyList<LedgerEntryDto> LedgerEntries { get; init; } = Array.Empty<LedgerEntryDto>();
    public decimal LedgerDebitTotal { get; init; }
    public decimal LedgerCreditTotal { get; init; }
    public bool LedgerBalanced { get; init; }
}

public sealed record CreatePaymentRequest
{
    public Guid? DebtorAccountId { get; init; }
    public string? DebtorAccountNumber { get; init; }
    public string CreditorName { get; init; } = string.Empty;
    public string CreditorAccountNumber { get; init; } = string.Empty;
    public string? CreditorBic { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NZD";
    public PaymentType Type { get; init; } = PaymentType.Transfer;
    public string Channel { get; init; } = "API";
    public string? Description { get; init; }
    public bool SimulateFailure { get; init; }
}

public sealed record PaymentCreatedDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public PaymentStatus Status { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NZD";
    public DateTimeOffset CreatedAt { get; init; }
    public bool Replayed { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed record RefundPaymentRequest
{
    public decimal? Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record ReversePaymentRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed record PaymentStatsDto
{
    public int TotalCount { get; init; }
    public decimal TotalVolume { get; init; }
    public double SuccessRatePct { get; init; }
    public int FailedCount { get; init; }
    public int PendingCount { get; init; }
    public double AverageProcessingMs { get; init; }
    public IReadOnlyDictionary<string, int> ByStatus { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> ByType { get; init; } = new Dictionary<string, int>();
}

public sealed record AccountOptionDto
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string MaskedNumber { get; init; } = string.Empty;
    public string Currency { get; init; } = "NZD";
    public decimal AvailableBalance { get; init; }
    public string CustomerName { get; init; } = string.Empty;
}
