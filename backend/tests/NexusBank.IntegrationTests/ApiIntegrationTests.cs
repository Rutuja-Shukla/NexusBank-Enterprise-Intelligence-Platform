using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusBank.Infrastructure.Persistence;
using Xunit;

namespace NexusBank.IntegrationTests;

public sealed class ApiIntegrationTests : IClassFixture<NexusBankApiFactory>
{
    private const string Password = "NexusBank!2026";
    private readonly NexusBankApiFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(NexusBankApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> SignInAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("accessToken").GetString()!;
    }

    private HttpClient Authorised(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Health_IsAnonymous_AndReportsDatabaseConnectivity()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("connected", payload.GetProperty("database").GetString());

        var status = payload.GetProperty("status").GetString();
        Assert.Contains(status, new[] { "healthy", "degraded" });
        // Redis and Kafka are deliberately absent in this environment: the API must say so.
        Assert.Contains("memory", payload.GetProperty("cache").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Metrics_ExposePrometheusText()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("nexusbank_", body);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsProblemDetails401()
    {
        var response = await _client.GetAsync("/api/v1/payments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unauthorised", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@nexusbank.dev", password = "DefinitelyWrong!1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsAccessToken_WithRolePermissions()
    {
        var token = await SignInAsync("admin@nexusbank.dev");
        var client = Authorised(token);

        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");

        Assert.Equal("admin@nexusbank.dev", profile.GetProperty("email").GetString());
        Assert.Equal("administrator", profile.GetProperty("roleSlug").GetString());
        Assert.Contains(profile.GetProperty("permissions").EnumerateArray(), item => item.GetString() == "integrations:read");
    }

    [Fact]
    public async Task ReadOnlyAuditor_CannotCreatePayments()
    {
        var token = await SignInAsync("auditor@nexusbank.dev");
        var client = Authorised(token);

        var read = await client.GetAsync("/api/v1/security/roles");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await client.PostAsJsonAsync("/api/v1/payments", new
        {
            creditorName = "Harbour Consulting Ltd",
            creditorAccountNumber = "99-8888-0777777-01",
            amount = 10m,
            currency = "NZD"
        });

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task OperationsEngineer_CannotReadSecurityDirectory()
    {
        var token = await SignInAsync("ops@nexusbank.dev");
        var client = Authorised(token);

        var response = await client.GetAsync("/api/v1/security/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Payment_CreateThenReplay_IsIdempotent()
    {
        var token = await SignInAsync("admin@nexusbank.dev");
        var client = Authorised(token);

        var accounts = await client.GetFromJsonAsync<JsonElement>("/api/v1/payments/accounts");
        var debtorAccountId = accounts.EnumerateArray().First().GetProperty("id").GetString();

        var instruction = new
        {
            debtorAccountId,
            creditorName = "Harbour Consulting Ltd",
            creditorAccountNumber = "99-8888-0777777-01",
            amount = 42.50m,
            currency = "NZD",
            description = "Integration test transfer",
            type = "Transfer"
        };

        var key = $"it-{Guid.NewGuid():N}";
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = JsonContent.Create(instruction)
        };
        first.Headers.Add("Idempotency-Key", key);

        var firstResponse = await client.SendAsync(first);
        firstResponse.EnsureSuccessStatusCode();
        var created = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = JsonContent.Create(instruction)
        };
        replay.Headers.Add("Idempotency-Key", key);
        var replayResponse = await client.SendAsync(replay);
        replayResponse.EnsureSuccessStatusCode();
        var replayed = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(created.GetProperty("id").GetString(), replayed.GetProperty("id").GetString());
        Assert.True(replayed.GetProperty("replayed").GetBoolean());

        var paymentId = created.GetProperty("id").GetString();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments/{paymentId}");
        Assert.True(detail.GetProperty("ledgerBalanced").GetBoolean());
        Assert.True(detail.GetProperty("journey").GetArrayLength() >= 5);
    }

    [Fact]
    public async Task Payment_WithMismatchedIdempotentPayload_IsRejected()
    {
        var token = await SignInAsync("admin@nexusbank.dev");
        var client = Authorised(token);
        var accounts = await client.GetFromJsonAsync<JsonElement>("/api/v1/payments/accounts");
        var debtorAccountId = accounts.EnumerateArray().First().GetProperty("id").GetString();

        var key = $"it-{Guid.NewGuid():N}";
        var body = new
        {
            debtorAccountId,
            creditorName = "Harbour Consulting Ltd",
            creditorAccountNumber = "99-8888-0777777-01",
            amount = 15m,
            currency = "NZD"
        };

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments") { Content = JsonContent.Create(body) };
        first.Headers.Add("Idempotency-Key", key);
        (await client.SendAsync(first)).EnsureSuccessStatusCode();

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = JsonContent.Create(new { body.debtorAccountId, body.creditorName, body.creditorAccountNumber, amount = 99m, body.currency })
        };
        second.Headers.Add("Idempotency-Key", key);
        var conflict = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task ValidationFailures_ReturnFieldErrors()
    {
        var token = await SignInAsync("admin@nexusbank.dev");
        var client = Authorised(token);

        var response = await client.PostAsJsonAsync("/api/v1/payments", new
        {
            creditorName = "",
            creditorAccountNumber = "123",
            amount = 0,
            currency = "JPY"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explorer_Catalogue_ListsExecutableEndpoints()
    {
        var token = await SignInAsync("dev@nexusbank.dev");
        var client = Authorised(token);

        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/v1/explorer/catalogue");

        Assert.True(catalogue.GetArrayLength() > 0);
        Assert.Contains(catalogue.EnumerateArray(), item => item.GetProperty("requiresAuth").GetBoolean());
        Assert.All(catalogue.EnumerateArray(), item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("permission").GetString())));

        // Execution is verified against the running server (docs/TESTING.md): the explorer's
        // self-invoker calls back over HTTP, and WebApplicationFactory exposes no TCP listener.
    }

    [Fact]
    public async Task SeededUsers_NeverStorePlaintextPasswords()
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NexusBankDbContext>();

        var hashes = await database.Users.Select(user => user.PasswordHash).ToListAsync();

        Assert.NotEmpty(hashes);
        Assert.All(hashes, hash =>
        {
            Assert.NotEqual(Password, hash);
            Assert.StartsWith("$2", hash);
        });
    }

    [Fact]
    public async Task EventPublish_WithoutBroker_IsFlaggedAsFallback()
    {
        var token = await SignInAsync("dev@nexusbank.dev");
        var client = Authorised(token);

        var response = await client.PostAsJsonAsync("/api/v1/events/publish", new
        {
            topic = "payments.events",
            eventType = "payment.reconciliation.requested",
            payload = JsonSerializer.Serialize(new { window = "T+1" })
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("isSimulated").GetBoolean());
        Assert.Equal("Produced", payload.GetProperty("status").GetString());

        var health = await _factory.CreateClient().GetAsync("/health");
        var healthBody = await health.Content.ReadAsStringAsync();
        Assert.Contains("In-process", healthBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
