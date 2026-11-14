using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NexusBank.Api.Filters;
using NexusBank.Api.Middleware;
using NexusBank.Api.Security;
using NexusBank.Application;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Application.Services;
using NexusBank.Infrastructure;
using NexusBank.Infrastructure.Persistence;
using NexusBank.Infrastructure.Persistence.Seed;
using NexusBank.IntegrationServices;
using NexusBank.Workers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "nexusbank-api")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is not configured. Set it through configuration or the Jwt__SigningKey environment variable (minimum 32 bytes).");
    }

    // Development-only fallback so `dotnet run` works without secrets. Never used outside Development.
    signingKey = "nexusbank-development-signing-key-do-not-use-in-production-2026";
    builder.Configuration["Jwt:SigningKey"] = signingKey;
}

builder.Services.AddNexusApplication();
builder.Services.AddNexusInfrastructure(builder.Configuration);
builder.Services.AddNexusIntegrationServices();
builder.Services.AddNexusWorkers();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NexusBank Integration Platform API",
        Version = "v1",
        Description = "Enterprise banking integration platform — synthetic data only. Authenticate with /api/v1/auth/login and use the bearer token."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by the login endpoint."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "nexusbank-api",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "nexusbank-console",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "email",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/401",
                    title = "Unauthorised",
                    status = 401,
                    detail = "A valid access token is required for this endpoint.",
                    code = "unauthorised",
                    correlationId = context.HttpContext.Items["correlationId"]
                });
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/403",
                    title = "Forbidden",
                    status = 403,
                    detail = "Your role does not grant the permission required for this operation.",
                    code = "forbidden",
                    correlationId = context.HttpContext.Items["correlationId"]
                });
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:4173" };

builder.Services.AddCors(options => options.AddPolicy("console", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("X-Correlation-ID")));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 600),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });

    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 12),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too many requests",
            status = 429,
            detail = "The rate limit for this endpoint has been exceeded. Please retry shortly.",
            code = "rate_limited"
        }, token);
    };
});

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("nexusbank-api", serviceVersion: "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(options =>
            options.Filter = context => !context.Request.Path.StartsWithSegments("/health"));
        tracing.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["correlationId"]?.ToString() ?? "unknown");
        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    };
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "NexusBank API v1");
    options.DocumentTitle = "NexusBank API";
});

app.UseCors("console");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/live", () => Results.Json(new
{
    status = "live",
    service = "nexusbank-api",
    timestamp = DateTimeOffset.UtcNow
})).AllowAnonymous();

app.MapGet("/health", async (SystemHealthService health, CancellationToken cancellationToken) =>
{
    var snapshot = await health.GetAsync(cancellationToken);
    return Results.Json(new
    {
        status = snapshot.OverallStatus,
        database = snapshot.DatabaseConnected ? "connected" : "unavailable",
        cache = snapshot.CacheMode,
        eventBus = snapshot.EventBusMode,
        availabilityPct = snapshot.AvailabilityPct,
        uptimeSeconds = snapshot.UptimeSeconds,
        checkedAt = snapshot.CheckedAt
    });
}).AllowAnonymous();

app.MapGet("/metrics", async (SystemHealthService health, CancellationToken cancellationToken) =>
{
    var content = await health.GetPrometheusMetricsAsync(cancellationToken);
    return Results.Text(content, "text/plain; version=0.0.4");
}).AllowAnonymous();

await InitialiseDatabaseAsync(app);

app.Run();

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();
        var provider = app.Configuration["Database:Provider"] ?? "SqlServer";

        if (app.Configuration.GetValue("Database:AutoMigrate", true))
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await db.Database.EnsureCreatedAsync();
            }
            else
            {
                await db.Database.MigrateAsync();
            }
        }

        if (app.Configuration.GetValue("Database:AutoSeed", true))
        {
            await scope.ServiceProvider.GetRequiredService<IDatabaseSeeder>().SeedAsync();
        }
    }
    catch (Exception ex)
    {
        // The host still starts so the health centre can report the database as unavailable.
        logger.LogError(ex, "Database initialisation failed; the platform will report degraded health");
    }
}

public partial class Program;
