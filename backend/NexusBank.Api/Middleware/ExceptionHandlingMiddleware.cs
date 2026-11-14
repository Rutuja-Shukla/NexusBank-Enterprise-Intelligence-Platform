using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NexusBank.Application.Common.Exceptions;

namespace NexusBank.Api.Middleware;

/// <summary>
/// Maps known application exceptions to consistent problem responses. Unexpected errors are
/// logged with the correlation id and returned without stack traces or internal details.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nothing to write.
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(ex, "Unhandled exception after the response started");
                throw;
            }

            var correlationId = context.Items["correlationId"]?.ToString() ?? Activity.Current?.TraceId.ToString() ?? "unknown";
            var problem = BuildProblem(ex, context, correlationId);

            if (ex is NexusException nexus)
            {
                _logger.LogWarning("Handled {Code} ({Status}) on {Method} {Path}: {Message}",
                    nexus.Code, nexus.StatusCode, context.Request.Method, context.Request.Path, nexus.Message);
            }
            else
            {
                _logger.LogError(ex, "Unhandled exception on {Method} {Path} (correlation {CorrelationId})",
                    context.Request.Method, context.Request.Path, correlationId);
            }

            context.Response.Clear();
            context.Response.StatusCode = problem.Status ?? 500;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    private ProblemDetails BuildProblem(Exception exception, HttpContext context, string correlationId)
    {
        var problem = new ProblemDetails
        {
            Instance = context.Request.Path,
            Extensions = { ["correlationId"] = correlationId, ["timestamp"] = DateTimeOffset.UtcNow }
        };

        switch (exception)
        {
            case ValidationFailedException validation:
                problem.Title = "Validation failed";
                problem.Detail = validation.Message;
                problem.Status = validation.StatusCode;
                problem.Extensions["code"] = validation.Code;
                problem.Extensions["errors"] = validation.Errors;
                break;
            case NexusException nexus:
                problem.Title = nexus.StatusCode switch
                {
                    401 => "Unauthorised",
                    403 => "Forbidden",
                    404 => "Not found",
                    409 => "Conflict",
                    429 => "Too many requests",
                    _ => "Request failed"
                };
                problem.Detail = nexus.Message;
                problem.Status = nexus.StatusCode;
                problem.Extensions["code"] = nexus.Code;
                break;
            default:
                problem.Title = "An unexpected error occurred";
                problem.Detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "The request could not be completed. Quote the correlation id when contacting the platform team.";
                problem.Status = 500;
                problem.Extensions["code"] = "internal_error";
                break;
        }

        return problem;
    }
}
