using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexusBank.Application.Dtos;

namespace NexusBank.Application.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.CreditorName).NotEmpty().MaximumLength(140)
            .WithMessage("Beneficiary name is required.");
        RuleFor(x => x.CreditorAccountNumber).NotEmpty().MinimumLength(8).MaximumLength(34)
            .WithMessage("A beneficiary account number with at least 8 digits is required.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000)
            .WithMessage("Amount must be between 0.01 and 1,000,000.");
        RuleFor(x => x.Currency).NotEmpty().Length(3).Must(c => c is "NZD" or "AUD" or "USD" or "GBP")
            .WithMessage("Currency must be one of NZD, AUD, USD or GBP.");
        RuleFor(x => x.Description).MaximumLength(280);
        RuleFor(x => x.Channel).MaximumLength(40);
        RuleFor(x => x.CreditorBic).MaximumLength(11)
            .Matches("^[A-Za-z0-9]{8}([A-Za-z0-9]{3})?$").When(x => !string.IsNullOrEmpty(x.CreditorBic))
            .WithMessage("BIC must be 8 or 11 alphanumeric characters.");
        RuleFor(x => x).Must(x => x.DebtorAccountId.HasValue || !string.IsNullOrWhiteSpace(x.DebtorAccountNumber))
            .WithMessage("Select a debtor account.");
    }
}

public sealed class CreateIntegrationRequestValidator : AbstractValidator<CreateIntegrationRequest>
{
    public CreateIntegrationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Key).NotEmpty().Matches("^[a-z0-9][a-z0-9-]{2,63}$")
            .WithMessage("Key must be 3-64 characters: lowercase letters, numbers and hyphens.");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(400)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _) || url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Base URL must be an absolute URI (or a mock:// fixture URL in development).");
        RuleFor(x => x.HealthEndpointPath).MaximumLength(200);
        RuleFor(x => x.OwnerTeam).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Version).NotEmpty().MaximumLength(20);
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(1, 120);
        RuleFor(x => x.RetryPolicy.MaxAttempts).InclusiveBetween(0, 10);
        RuleFor(x => x.RetryPolicy.BaseDelayMs).InclusiveBetween(0, 10_000);
        RuleFor(x => x.RetryPolicy.MaxDelayMs).GreaterThanOrEqualTo(x => x.RetryPolicy.BaseDelayMs);
        RuleFor(x => x.AuthHeaderName).MaximumLength(80);
    }
}

public sealed class CreateIncidentRequestValidator : AbstractValidator<CreateIncidentRequest>
{
    public CreateIncidentRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(8).MaximumLength(160);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(20).MaximumLength(2000);
        RuleFor(x => x.Team).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ImpactSummary).MaximumLength(500);
        RuleFor(x => x.Assignee).MaximumLength(120);
    }
}

public sealed class UpdateIncidentRequestValidator : AbstractValidator<UpdateIncidentRequest>
{
    public UpdateIncidentRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(160).When(x => x.Title is not null);
        RuleFor(x => x.RootCause).MaximumLength(1000);
        RuleFor(x => x.CorrectiveActions).MaximumLength(1000);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Assignee).MaximumLength(120);
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.RoleSlug).NotEmpty();
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(12).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.RoleSlug).NotEmpty();
        RuleFor(x => x.NewPassword).MinimumLength(12).MaximumLength(128)
            .Matches("[A-Z]").When(x => !string.IsNullOrEmpty(x.NewPassword))
            .Matches("[0-9]").When(x => !string.IsNullOrEmpty(x.NewPassword));
    }
}

public sealed class PublishEventRequestValidator : AbstractValidator<PublishEventRequest>
{
    public PublishEventRequestValidator()
    {
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EventType).MaximumLength(200);
        RuleFor(x => x.EventKey).MaximumLength(200);
        RuleFor(x => x.Payload).MaximumLength(64_000);
    }
}

public sealed class ExecuteRequestValidator : AbstractValidator<ExecuteRequest>
{
    public ExecuteRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().Must(m => m is "GET" or "POST" or "PUT" or "PATCH" or "DELETE")
            .WithMessage("Method must be one of GET, POST, PUT, PATCH or DELETE.");
        RuleFor(x => x.Path).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Body).MaximumLength(64_000);
        RuleFor(x => x.IdempotencyKey).MaximumLength(120);
    }
}
