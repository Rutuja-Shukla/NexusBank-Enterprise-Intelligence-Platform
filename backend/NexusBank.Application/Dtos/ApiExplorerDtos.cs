namespace NexusBank.Application.Dtos;

public sealed record EndpointDescriptorDto
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Permission { get; init; } = string.Empty;
    public bool RequiresAuth { get; init; } = true;
    public string? SampleBody { get; init; }
    public string? PathParameters { get; init; }
}

public sealed record ExecuteRequest
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = string.Empty;
    public string? Body { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record ExecuteResponse
{
    public int StatusCode { get; init; }
    public string ReasonPhrase { get; init; } = string.Empty;
    public double DurationMs { get; init; }
    public string Body { get; init; } = string.Empty;
    public bool BodyIsJson { get; init; }
    public long SizeBytes { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public string? Error { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
}
