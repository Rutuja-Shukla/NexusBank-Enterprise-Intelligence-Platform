using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using NexusBank.Application.Common.Exceptions;

namespace NexusBank.Api.Filters;

/// <summary>
/// Resolves a FluentValidation validator for each action argument and converts failures to
/// the platform's standard validation problem response. Validators live in the application
/// layer and are registered in DI, so controllers stay free of validation code.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator) continue;

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "request" : char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

            throw new ValidationFailedException(errors);
        }

        await next();
    }
}
