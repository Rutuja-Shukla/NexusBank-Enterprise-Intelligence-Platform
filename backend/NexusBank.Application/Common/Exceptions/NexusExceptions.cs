namespace NexusBank.Application.Common.Exceptions;

/// <summary>Base type for expected, mapped-to-HTTP failures.</summary>
public abstract class NexusException : Exception
{
    protected NexusException(string message, string code) : base(message) => Code = code;
    public string Code { get; }
    public abstract int StatusCode { get; }
}

public class NotFoundException : NexusException
{
    public NotFoundException(string message, string code = "not_found") : base(message, code) { }
    public override int StatusCode => 404;

    public static NotFoundException For(string entity, object id) =>
        new($"{entity} '{id}' was not found.", $"{entity.ToLowerInvariant()}_not_found");
}

public class ValidationFailedException : NexusException
{
    public ValidationFailedException(string message = "One or more validation errors occurred.")
        : base(message, "validation_failed") { }

    public ValidationFailedException(IDictionary<string, string[]> errors) : this()
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>();
    public override int StatusCode => 400;
}

public class ConflictException : NexusException
{
    public ConflictException(string message, string code = "conflict") : base(message, code) { }
    public override int StatusCode => 409;
}

public class ForbiddenException : NexusException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message, "forbidden") { }
    public override int StatusCode => 403;
}

public class AuthenticationException : NexusException
{
    public AuthenticationException(string message = "Invalid credentials.", string code = "invalid_credentials")
        : base(message, code) { }
    public override int StatusCode => 401;
}

public class RateLimitedException : NexusException
{
    public RateLimitedException(string message = "Too many requests. Please retry shortly.")
        : base(message, "rate_limited") { }
    public override int StatusCode => 429;
}

public class ExternalServiceException : NexusException
{
    public ExternalServiceException(string message, string code = "upstream_unavailable") : base(message, code) { }
    public override int StatusCode => 502;
}
