using NexusBank.Application.Dtos;
using NexusBank.Application.Validators;

namespace NexusBank.Tests;

public sealed class ValidatorTests
{
    private readonly CreatePaymentRequestValidator _payments = new();
    private readonly CreateUserRequestValidator _users = new();
    private readonly ExecuteRequestValidator _explorer = new();
    private readonly CreateIntegrationRequestValidator _integrations = new();

    private static CreatePaymentRequest ValidPayment() => new()
    {
        DebtorAccountId = Guid.NewGuid(),
        CreditorName = "Harbour Consulting Ltd",
        CreditorAccountNumber = "99-8888-0777777-01",
        Amount = 125.50m,
        Currency = "NZD"
    };

    [Fact]
    public void Payment_WithValidInstruction_Passes()
    {
        Assert.True(_payments.Validate(ValidPayment()).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_001)]
    public void Payment_WithInvalidAmount_Fails(decimal amount)
    {
        var request = ValidPayment() with { Amount = amount };

        var result = _payments.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreatePaymentRequest.Amount));
    }

    [Fact]
    public void Payment_WithUnsupportedCurrency_Fails()
    {
        var request = ValidPayment() with { Currency = "JPY" };

        var result = _payments.Validate(request);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("NEXUNZ2A12")]
    public void Payment_WithMalformedBic_Fails(string bic)
    {
        var request = ValidPayment() with { CreditorBic = bic };

        Assert.False(_payments.Validate(request).IsValid);
    }

    [Fact]
    public void Payment_WithoutDebtor_Fails()
    {
        var request = ValidPayment() with { DebtorAccountId = null, DebtorAccountNumber = null };

        var result = _payments.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("debtor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void User_WithWeakPassword_Fails()
    {
        var result = _users.Validate(new CreateUserRequest
        {
            Email = "new.operator@nexusbank.dev",
            DisplayName = "New Operator",
            RoleSlug = "operations",
            Password = "short"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateUserRequest.Password));
    }

    [Fact]
    public void Explorer_WithUnsupportedMethod_Fails()
    {
        var result = _explorer.Validate(new ExecuteRequest { Method = "TRACE", Path = "/api/v1/payments" });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Integration_AcceptsMockFixtureUrl_AndRejectsRelativeUrl()
    {
        var request = new CreateIntegrationRequest
        {
            Name = "Fraud Detection Service",
            Key = "fraud-detection",
            BaseUrl = "mock://fraud-detection",
            OwnerTeam = "Financial Crime",
            Version = "v1",
            TimeoutSeconds = 10,
            Category = Domain.Enums.IntegrationCategory.Risk,
            AuthType = Domain.Enums.IntegrationAuthType.ApiKey,
            RetryPolicy = new RetryPolicyDto()
        };

        Assert.True(_integrations.Validate(request).IsValid);
        Assert.False(_integrations.Validate(request with { BaseUrl = "not-a-url" }).IsValid);
    }
}
