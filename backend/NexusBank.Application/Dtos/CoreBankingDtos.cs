using NexusBank.Domain.Enums;

namespace NexusBank.Application.Dtos;

public sealed record CustomerDto
{
    public Guid Id { get; init; }
    public string CustomerNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public KycStatus KycStatus { get; init; }
    public bool IsActive { get; init; }
    public int AccountCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record AccountDto
{
    public Guid Id { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string ProductType { get; init; } = string.Empty;
    public string Currency { get; init; } = "NZD";
    public decimal Balance { get; init; }
    public decimal AvailableBalance { get; init; }
    public AccountStatus Status { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
}
