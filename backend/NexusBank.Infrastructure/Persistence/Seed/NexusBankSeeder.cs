using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Shared.Security;

namespace NexusBank.Infrastructure.Persistence.Seed;

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates deterministic synthetic data so the platform can be demonstrated without any
/// real customer information. All names, accounts and identifiers are fictitious.
/// </summary>
public sealed class NexusBankSeeder : IDatabaseSeeder
{
    public const string DemoPassword = "NexusBank!2026";

    private sealed record IntegrationSpec(
        string Key, string Name, IntegrationCategory Category, string Description, string BaseUrl,
        IntegrationAuthType Auth, IntegrationStatus Status, string Team, string Version,
        double LatencyMs, double FailureRatePct, long Requests24h, int X, int Y, bool IsInternal);

    private static readonly IntegrationSpec[] IntegrationSpecs =
    {
        new("customer-management", "Customer Management System", IntegrationCategory.Core,
            "System of record for customer identity, KYC state and contact details.",
            "mock://customer-management", IntegrationAuthType.MutualTls, IntegrationStatus.Healthy,
            "Customer Platforms", "4.8.2", 42, 0.12, 18_420, 26, 26, true),
        new("account-management", "Account Management Service", IntegrationCategory.Core,
            "Owns account balances, product enrolment and posting of ledger entries.",
            "mock://account-management", IntegrationAuthType.MutualTls, IntegrationStatus.Healthy,
            "Core Banking", "5.2.0", 58, 0.20, 24_180, 46, 18, true),
        new("payment-processing", "Payment Processing Service", IntegrationCategory.Payments,
            "Orchestrates payment instructions, validation and settlement routing.",
            "mock://payment-processing", IntegrationAuthType.JwtBearer, IntegrationStatus.Healthy,
            "Payments Platform", "7.1.4", 96, 0.35, 12_760, 62, 40, true),
        new("card-management", "Card Management System", IntegrationCategory.Cards,
            "Card issuing, authorisation decisions and limit management.",
            "mock://card-management", IntegrationAuthType.OAuth2ClientCredentials, IntegrationStatus.Degraded,
            "Cards & Payments", "3.9.1", 412, 4.8, 9_240, 34, 56, false),
        new("fraud-detection", "Fraud Detection Service", IntegrationCategory.Risk,
            "Real-time risk scoring and transaction anomaly detection.",
            "mock://fraud-detection", IntegrationAuthType.ApiKey, IntegrationStatus.Healthy,
            "Financial Crime", "2.4.7", 74, 0.9, 15_300, 22, 44, true),
        new("transaction-processing", "Transaction Processing Service", IntegrationCategory.Payments,
            "Ledger projection and reconciliation for all posted transactions.",
            "mock://transaction-processing", IntegrationAuthType.JwtBearer, IntegrationStatus.Healthy,
            "Payments Platform", "6.0.3", 88, 0.44, 20_100, 62, 68, true),
        new("external-payment-gateway", "External Payment Gateway", IntegrationCategory.External,
            "Third-party rail for outbound international and domestic payments.",
            "mock://external-payment-gateway", IntegrationAuthType.OAuth2ClientCredentials, IntegrationStatus.Down,
            "Payments Platform", "1.8.0", 1_240, 12.4, 3_980, 86, 38, false),
        new("data-platform", "Data Platform", IntegrationCategory.Data,
            "Streaming ingestion into the analytics lakehouse and regulatory reporting.",
            "mock://data-platform", IntegrationAuthType.ApiKey, IntegrationStatus.Healthy,
            "Data & Analytics", "2.2.0", 132, 0.6, 7_450, 80, 74, false)
    };

    private static readonly (string Source, string Target, string Protocol, bool Critical, string Description)[] DependencySpecs =
    {
        ("customer-management", "account-management", "REST", true, "Account opening requires verified customer identity."),
        ("customer-management", "fraud-detection", "gRPC", false, "Customer risk profile enrichment."),
        ("account-management", "payment-processing", "REST", true, "Balance and account status checks before posting."),
        ("fraud-detection", "payment-processing", "REST", true, "Risk score is required before instruction release."),
        ("payment-processing", "external-payment-gateway", "REST", true, "Outbound payments are routed through the external rail."),
        ("payment-processing", "transaction-processing", "Kafka", true, "Payment events feed the transaction ledger projection."),
        ("card-management", "transaction-processing", "Kafka", false, "Card authorisations are projected into the ledger."),
        ("transaction-processing", "data-platform", "Kafka", false, "Reconciliation output feeds the data platform."),
        ("payment-processing", "data-platform", "Kafka", false, "Payment lifecycle events feed regulatory reporting.")
    };

    private static readonly (string Name, string Description, int Partitions, int Replication, int Retention, string Team, string[] Types)[] TopicSpecs =
    {
        ("payments.events", "Payment lifecycle transitions emitted by the payment processing service.", 6, 3, 72, "Payments Platform",
            new[] { "payment.initiated", "payment.completed", "payment.failed", "payment.reversed", "payment.refunded" }),
        ("accounts.events", "Account state changes and balance-affecting postings.", 3, 3, 72, "Core Banking",
            new[] { "account.opened", "account.updated", "account.balance.changed" }),
        ("cards.events", "Card issuing and authorisation decisions.", 3, 3, 48, "Cards & Payments",
            new[] { "card.authorised", "card.declined", "card.limit.changed" }),
        ("fraud.signals", "Risk signals raised by the fraud detection service.", 3, 3, 168, "Financial Crime",
            new[] { "fraud.signal.raised", "fraud.case.opened" }),
        ("customer.events", "Customer profile and consent changes.", 3, 3, 168, "Customer Platforms",
            new[] { "customer.created", "customer.kyc.updated" }),
        ("notifications.outbound", "Outbound customer notifications dispatched by the platform.", 2, 3, 24, "Customer Platforms",
            new[] { "notification.email.requested", "notification.sms.requested" })
    };

    private static readonly string[] Creditors =
    {
        "Harbour Consulting Ltd", "Kōwhai Design Studio", "Southern Cross Logistics", "Aotearoa Fresh Produce",
        "Meridian Energy Retail", "Vulcan Steel Supplies", "Kiwi Rail Freight", "Rangitoto Insurance",
        "Tasman Cloud Services", "Ponsonby Dental Group", "Waiheke Hospitality", "Northern Medical Supplies"
    };

    private readonly NexusBankDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly IEventBus _bus;
    private readonly ILogger<NexusBankSeeder> _logger;
    private readonly Random _random = new(20261113);

    public NexusBankSeeder(
        NexusBankDbContext db,
        IPasswordHasher hasher,
        IDateTimeProvider clock,
        IEventBus bus,
        ILogger<NexusBankSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _bus = bus;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Users.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Seed data already present; skipping");
            return;
        }

        _logger.LogInformation("Seeding NexusBank synthetic data");
        var now = _clock.UtcNow;

        var roles = await SeedIdentityAsync(now, cancellationToken);
        var integrations = SeedIntegrations(now);
        var accounts = SeedCustomersAndAccounts(now);
        SeedPayments(accounts, integrations, now);
        SeedMessaging(now);
        SeedIncidents(integrations, now);
        SeedTelemetry(now);
        SeedAuditTrail(roles, now);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seed complete: {Payments} payments, {Events} events, {Logs} request records",
            await _db.Payments.CountAsync(cancellationToken),
            await _db.KafkaEvents.CountAsync(cancellationToken),
            await _db.ApiRequestLogs.CountAsync(cancellationToken));
    }

    private async Task<Dictionary<string, Role>> SeedIdentityAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var permissions = NexusPermissions.All.Select(p => new Permission
        {
            Code = p.Code,
            Description = p.Description,
            Category = p.Category
        }).ToList();
        _db.Permissions.AddRange(permissions);

        var roleDefinitions = new (string Slug, string Name, string Description)[]
        {
            (NexusRoles.Administrator, "Administrator", "Full platform control including users, roles and access policy."),
            (NexusRoles.OperationsEngineer, "Operations Engineer", "Runs the platform day to day: integrations, payments, events and incidents."),
            (NexusRoles.Developer, "Developer", "Builds and tests integrations and payment flows in the sandbox."),
            (NexusRoles.ReadOnlyAuditor, "Read-Only Auditor", "Read-only visibility across the platform with a full audit trail.")
        };

        var roles = new Dictionary<string, Role>();
        foreach (var definition in roleDefinitions)
        {
            var role = new Role
            {
                Slug = definition.Slug,
                Name = definition.Name,
                Description = definition.Description,
                IsSystem = true
            };
            roles[role.Slug] = role;
            _db.Roles.Add(role);

            var codes = NexusPermissions.ForRole(role.Slug).ToHashSet();
            foreach (var permission in permissions.Where(p => codes.Contains(p.Code)))
            {
                _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }

        var users = new (string Email, string Name, string Role, DateTimeOffset? LastLogin, bool Active)[]
        {
            ("admin@nexusbank.dev", "Aroha Mitchell", NexusRoles.Administrator, now.AddHours(-2), true),
            ("ops@nexusbank.dev", "Daniel Whitfield", NexusRoles.OperationsEngineer, now.AddMinutes(-42), true),
            ("dev@nexusbank.dev", "Priya Raman", NexusRoles.Developer, now.AddHours(-26), true),
            ("auditor@nexusbank.dev", "Marcus Chen", NexusRoles.ReadOnlyAuditor, now.AddDays(-3), true),
            ("sre@nexusbank.dev", "Sofia Almeida", NexusRoles.OperationsEngineer, now.AddDays(-1), true),
            ("contractor@nexusbank.dev", "Liam O'Connor", NexusRoles.Developer, null, false)
        };

        foreach (var definition in users)
        {
            _db.Users.Add(new User
            {
                Email = definition.Email,
                DisplayName = definition.Name,
                PasswordHash = _hasher.Hash(DemoPassword),
                RoleId = roles[definition.Role].Id,
                IsActive = definition.Active,
                CreatedAt = now.AddMonths(-8),
                LastLoginAt = definition.LastLogin
            });
        }

        await Task.CompletedTask;
        return roles;
    }

    private Dictionary<string, Integration> SeedIntegrations(DateTimeOffset now)
    {
        var integrations = new Dictionary<string, Integration>();

        foreach (var spec in IntegrationSpecs)
        {
            var integration = new Integration
            {
                Key = spec.Key,
                Name = spec.Name,
                Category = spec.Category,
                Description = spec.Description,
                BaseUrl = spec.BaseUrl,
                HealthEndpointPath = "/health",
                AuthType = spec.Auth,
                AuthHeaderName = spec.Auth switch
                {
                    IntegrationAuthType.ApiKey => "X-Nexus-Key",
                    IntegrationAuthType.JwtBearer => "Authorization",
                    _ => null
                },
                SecretReference = spec.Auth == IntegrationAuthType.None ? null : $"kv://nexusbank/{spec.Key}",
                Status = spec.Status,
                Enabled = true,
                IsInternal = spec.IsInternal,
                OwnerTeam = spec.Team,
                Version = spec.Version,
                TimeoutSeconds = spec.Category is IntegrationCategory.External ? 10 : 5,
                RetryMaxAttempts = 3,
                RetryBaseDelayMs = 250,
                RetryMaxDelayMs = 5_000,
                CircuitBreakerFailureThreshold = 5,
                CircuitBreakerBreakSeconds = 30,
                AvgLatencyMs = spec.LatencyMs,
                FailureRatePct = spec.FailureRatePct,
                RequestCount24h = spec.Requests24h,
                LastSuccessAt = spec.Status == IntegrationStatus.Down ? now.AddMinutes(-96) : now.AddSeconds(-_random.Next(5, 240)),
                LastFailureAt = spec.Status == IntegrationStatus.Healthy ? now.AddHours(-9) : now.AddMinutes(-_random.Next(3, 90)),
                LastFailureReason = spec.Status switch
                {
                    IntegrationStatus.Down => "Connection refused after 3 retries (circuit breaker open).",
                    IntegrationStatus.Degraded => "Elevated p95 latency: 1,480 ms against a 400 ms SLO.",
                    _ => null
                },
                TopologyX = spec.X,
                TopologyY = spec.Y,
                CreatedAt = now.AddMonths(-14),
                UpdatedAt = now.AddHours(-3)
            };

            integrations[spec.Key] = integration;
            _db.Integrations.Add(integration);

            for (var hour = 23; hour >= 0; hour--)
            {
                var degradedNow = spec.Status != IntegrationStatus.Healthy;
                var degradedThen = degradedNow && hour > 6;
                _db.IntegrationHealthSamples.Add(new IntegrationHealthSample
                {
                    IntegrationId = integration.Id,
                    Timestamp = now.AddHours(-hour),
                    Status = degradedNow && !degradedThen ? spec.Status : IntegrationStatus.Healthy,
                    LatencyMs = Math.Round(spec.LatencyMs * (0.82 + _random.NextDouble() * 0.4), 1),
                    SuccessRatePct = degradedNow && !degradedThen ? 100 - spec.FailureRatePct : 99.6 + _random.NextDouble() * 0.4,
                    RequestCount = (int)(spec.Requests24h / 24)
                });
            }

            _db.IntegrationAuditEntries.Add(new IntegrationAuditEntry
            {
                IntegrationId = integration.Id,
                Timestamp = now.AddMonths(-14),
                Action = "created",
                Actor = "admin@nexusbank.dev",
                Details = $"Integration '{spec.Name}' registered with endpoint {spec.BaseUrl}."
            });
            _db.IntegrationAuditEntries.Add(new IntegrationAuditEntry
            {
                IntegrationId = integration.Id,
                Timestamp = now.AddDays(-21),
                Action = "updated",
                Actor = "ops@nexusbank.dev",
                Details = "Retry policy tightened: maxAttempts 5 → 3, baseDelay 500 ms → 250 ms."
            });
            if (spec.Status != IntegrationStatus.Healthy)
            {
                _db.IntegrationAuditEntries.Add(new IntegrationAuditEntry
                {
                    IntegrationId = integration.Id,
                    Timestamp = now.AddHours(-7),
                    Action = "health",
                    Actor = "system",
                    Details = $"Probe failed: status set to {spec.Status} after sustained error rate above threshold."
                });
            }
        }

        foreach (var dependency in DependencySpecs)
        {
            _db.IntegrationDependencies.Add(new IntegrationDependency
            {
                SourceIntegrationId = integrations[dependency.Source].Id,
                TargetIntegrationId = integrations[dependency.Target].Id,
                Protocol = dependency.Protocol,
                IsCritical = dependency.Critical,
                Description = dependency.Description
            });
        }

        return integrations;
    }

    private List<Account> SeedCustomersAndAccounts(DateTimeOffset now)
    {
        var customers = new (string First, string Last, string Segment, KycStatus Kyc)[]
        {
            ("Aroha", "Mitchell", "Private", KycStatus.Verified),
            ("Daniel", "Whitfield", "Personal", KycStatus.Verified),
            ("Priya", "Raman", "Personal", KycStatus.Verified),
            ("Marcus", "Chen", "Business", KycStatus.Verified),
            ("Sofia", "Almeida", "Personal", KycStatus.Verified),
            ("James", "Kōrero", "Business", KycStatus.Verified),
            ("Hana", "Takahashi", "Personal", KycStatus.Pending),
            ("Liam", "O'Connor", "Personal", KycStatus.Verified),
            ("Nia", "Patel", "Personal", KycStatus.Verified),
            ("Ethan", "Brooks", "Business", KycStatus.Verified),
            ("Amelia", "Ngata", "Private", KycStatus.Verified),
            ("Tomasz", "Nowak", "Personal", KycStatus.Rejected)
        };

        var accounts = new List<Account>();
        var index = 0;

        foreach (var customer in customers)
        {
            var entity = new Customer
            {
                CustomerNumber = $"CUS-{_random.Next(100000, 999999)}",
                FirstName = customer.First,
                LastName = customer.Last,
                Email = $"{customer.First.ToLowerInvariant()}.{customer.Last.ToLowerInvariant().Replace("'", string.Empty).Replace("ō", "o")}@example.test",
                Phone = $"+64 21 {_random.Next(100, 999)} {_random.Next(1000, 9999)}",
                Segment = customer.Segment,
                KycStatus = customer.Kyc,
                IsActive = customer.Kyc != KycStatus.Rejected,
                CreatedAt = now.AddMonths(-_random.Next(4, 60))
            };
            _db.Customers.Add(entity);

            var productCount = customer.Segment == "Business" ? 2 : 1;
            for (var p = 0; p < productCount; p++)
            {
                index++;
                var product = customer.Segment == "Business"
                    ? (p == 0 ? "Business Transaction" : "Business Savings")
                    : (p == 0 ? "Everyday" : "Serious Saver");
                var balance = Math.Round((decimal)(_random.NextDouble() * 180_000 + 24_000), 2);

                var account = new Account
                {
                    CustomerId = entity.Id,
                    AccountNumber = $"12{3141 + index:D4}{index:D7}{index % 100:D2}",
                    Iban = $"NZ12ASB0{index:D10}",
                    ProductType = product,
                    Currency = "NZD",
                    Balance = balance,
                    AvailableBalance = Math.Round(balance * 0.94m, 2),
                    Status = customer.Kyc == KycStatus.Pending ? AccountStatus.Dormant : AccountStatus.Active,
                    OpenedAt = now.AddMonths(-_random.Next(3, 48)),
                    Customer = entity
                };
                accounts.Add(account);
                _db.Accounts.Add(account);
            }
        }

        return accounts;
    }

    private void SeedPayments(List<Account> accounts, Dictionary<string, Integration> integrations, DateTimeOffset now)
    {
        var activeAccounts = accounts.Where(a => a.Status == AccountStatus.Active).ToList();
        if (activeAccounts.Count == 0) return;

        for (var i = 0; i < 240; i++)
        {
            var account = activeAccounts[_random.Next(activeAccounts.Count)];
            var status = PickWeighted(
                (PaymentStatus.Completed, 165),
                (PaymentStatus.Failed, 22),
                (PaymentStatus.Reversed, 8),
                (PaymentStatus.Processing, 9),
                (PaymentStatus.Pending, 6));

            var type = PickWeighted(
                (PaymentType.Transfer, 150),
                (PaymentType.CardPayment, 40),
                (PaymentType.DirectDebit, 30),
                (PaymentType.Refund, 20));

            var createdAt = now.AddMinutes(-_random.Next(5, 60 * 24 * 14));
            var creditor = Creditors[_random.Next(Creditors.Length)];
            var external = _random.NextDouble() < 0.28;
            var amount = Math.Round((decimal)(_random.NextDouble() * 2_400 + 18.5), 2);
            var integrationKey = external ? "external-payment-gateway" : "payment-processing";

            var payment = new Payment
            {
                Reference = $"PAY-{createdAt:yyMMdd}-{_random.Next(100000, 999999)}",
                Type = type,
                Status = status,
                Amount = amount,
                Currency = "NZD",
                DebtorAccountId = account.Id,
                DebtorAccountNumber = account.AccountNumber,
                CreditorName = creditor,
                CreditorAccountNumber = external
                    ? $"{_random.Next(10, 99)}{_random.Next(1000, 9999)}{_random.Next(1000000, 9999999)}{_random.Next(10, 99)}"
                    : accounts[_random.Next(accounts.Count)].AccountNumber,
                CreditorBic = external ? PickBic() : null,
                Channel = PickWeighted(("API", 60), ("Internet Banking", 45), ("Mobile", 60), ("Operations", 15)),
                Description = $"Invoice {_random.Next(1000, 9999)}-{_random.Next(100, 999)}",
                CorrelationId = $"cor_{Guid.NewGuid():N}"[..20],
                InitiatedByUserId = null,
                RiskScore = _random.Next(4, 78),
                IntegrationId = integrations[integrationKey].Id,
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };

            var stepIndex = 0;
            var cursor = createdAt;
            var serviceNames = new[] { "NexusBank API", "Validation Service", "Account Management Service", "Payment Processing Service", "External Payment Gateway", "Payment Processing Service" };
            var steps = new[] { PaymentStep.Initiated, PaymentStep.Validation, PaymentStep.AccountLookup, PaymentStep.PaymentProcessing, PaymentStep.ExternalGateway, PaymentStep.Confirmation };
            var details = new[]
            {
                $"Instruction accepted on the {payment.Channel} channel.",
                "Amount, currency, account status and instruction schema validated.",
                "Beneficiary account located and currency matched.",
                "Funds reserved and ledger postings prepared.",
                "Instruction accepted by the beneficiary institution.",
                "Payment confirmed and receipt generated."
            };

            var stopAt = status switch
            {
                PaymentStatus.Completed or PaymentStatus.Reversed => 6,
                PaymentStatus.Failed => external ? 5 : 4,
                PaymentStatus.Processing => 4,
                PaymentStatus.Pending => 3,
                _ => 3
            };

            var failedStepReached = status == PaymentStatus.Failed;
            for (var s = 0; s < stopAt; s++)
            {
                if (!external && s == 4) continue;
                var duration = s == 4 ? _random.Next(180, 640) : _random.Next(8, 120);
                cursor = cursor.AddMilliseconds(duration);
                var isFailurePoint = failedStepReached && s == stopAt - 1;
                var isRunning = status is PaymentStatus.Processing or PaymentStatus.Pending && s == stopAt - 1;

                _db.PaymentEvents.Add(new PaymentEvent
                {
                    PaymentId = payment.Id,
                    Sequence = ++stepIndex,
                    Step = steps[s],
                    Status = isFailurePoint ? PaymentStepStatus.Failed : isRunning ? PaymentStepStatus.Running : PaymentStepStatus.Succeeded,
                    ServiceName = serviceNames[s],
                    Timestamp = cursor,
                    DurationMs = duration,
                    Detail = details[s],
                    ErrorDetail = isFailurePoint ? "Downstream processor rejected the instruction after 3 retries." : null
                });
            }

            if (status == PaymentStatus.Reversed)
            {
                cursor = cursor.AddMinutes(_random.Next(20, 900));
                _db.PaymentEvents.Add(new PaymentEvent
                {
                    PaymentId = payment.Id,
                    Sequence = ++stepIndex,
                    Step = PaymentStep.Reversal,
                    Status = PaymentStepStatus.Succeeded,
                    ServiceName = "Payment Processing Service",
                    Timestamp = cursor,
                    DurationMs = _random.Next(20, 60),
                    Detail = "Reversal processed by Operations after beneficiary dispute."
                });
            }

            payment.ProcessingDurationMs = (int)(cursor - createdAt).TotalMilliseconds;
            payment.CompletedAt = status is PaymentStatus.Completed or PaymentStatus.Reversed ? cursor : null;
            payment.UpdatedAt = cursor;

            if (status == PaymentStatus.Failed)
            {
                payment.FailureCode = external ? "GATEWAY_TIMEOUT" : "PROCESSOR_REJECTED";
                payment.FailureReason = external
                    ? "External payment gateway did not respond within the 10s timeout after 3 retries."
                    : "Payment processor rejected the instruction during posting.";
            }

            _db.Payments.Add(payment);

            if (status is PaymentStatus.Completed or PaymentStatus.Reversed)
            {
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    PaymentId = payment.Id,
                    AccountId = account.Id,
                    AccountNumber = account.AccountNumber,
                    EntryType = LedgerEntryType.Debit,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    PostedAt = cursor,
                    Description = $"Debit for {payment.Reference} to {payment.CreditorName}"
                });
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    PaymentId = payment.Id,
                    AccountNumber = external ? "NEXUSBANK-SUSPENSE" : payment.CreditorAccountNumber,
                    EntryType = LedgerEntryType.Credit,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    PostedAt = cursor,
                    Description = $"Credit for {payment.Reference} to {payment.CreditorName}"
                });
            }

            if (status == PaymentStatus.Reversed)
            {
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    PaymentId = payment.Id,
                    AccountId = account.Id,
                    AccountNumber = account.AccountNumber,
                    EntryType = LedgerEntryType.Credit,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    PostedAt = cursor,
                    Description = $"Reversal credit for {payment.Reference}"
                });
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    PaymentId = payment.Id,
                    AccountNumber = external ? "NEXUSBANK-SUSPENSE" : payment.CreditorAccountNumber,
                    EntryType = LedgerEntryType.Debit,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    PostedAt = cursor,
                    Description = $"Reversal debit for {payment.Reference}"
                });
            }
        }
    }

    private void SeedMessaging(DateTimeOffset now)
    {
        var brokerConnected = _bus.BrokerConnected;
        var topics = new Dictionary<string, EventTopic>();

        foreach (var spec in TopicSpecs)
        {
            var topic = new EventTopic
            {
                Name = spec.Name,
                Description = spec.Description,
                Partitions = spec.Partitions,
                ReplicationFactor = spec.Replication,
                RetentionHours = spec.Retention,
                OwnerTeam = spec.Team,
                CreatedAt = now.AddMonths(-11)
            };
            topics[topic.Name] = topic;
            _db.EventTopics.Add(topic);

            var groups = new (string Name, string Team, ConsumerGroupState State, long Lag)[]
            {
                ($"{spec.Name}.ledger-projector", "Payments Platform", ConsumerGroupState.Stable, _random.Next(0, 40)),
                ($"{spec.Name}.analytics-sink", "Data & Analytics", ConsumerGroupState.Stable, _random.Next(0, 120))
            };

            foreach (var group in groups)
            {
                _db.ConsumerGroups.Add(new ConsumerGroup
                {
                    GroupId = group.Name,
                    TopicId = topic.Id,
                    State = group.State,
                    Members = _random.Next(1, 4),
                    Lag = group.Lag,
                    CommittedOffset = _random.Next(120_000, 480_000),
                    OwnerTeam = group.Team,
                    LastHeartbeatAt = now.AddSeconds(-_random.Next(2, 40))
                });
            }

            var offset = _random.Next(100_000, 250_000);
            for (var i = 0; i < 48; i++)
            {
                var producedAt = now.AddMinutes(-_random.Next(2, 60 * 24));
                var eventType = spec.Types[_random.Next(spec.Types.Length)];
                var status = PickWeighted(
                    (KafkaEventStatus.Consumed, 82),
                    (KafkaEventStatus.Produced, 12),
                    (KafkaEventStatus.Failed, 3),
                    (KafkaEventStatus.DeadLettered, 3));

                _db.KafkaEvents.Add(new KafkaEvent
                {
                    TopicId = topic.Id,
                    EventKey = $"evt-{Guid.NewGuid():N}"[..16],
                    EventType = eventType,
                    Payload = BuildPayload(eventType),
                    Headers = "{\"producer\":\"nexusbank.payment-processing\",\"schemaVersion\":\"3\"}",
                    Partition = _random.Next(0, spec.Partitions),
                    Offset = offset++,
                    ProducedAt = producedAt,
                    ConsumedAt = status == KafkaEventStatus.Consumed ? producedAt.AddMilliseconds(_random.Next(20, 900)) : null,
                    Status = status,
                    RetryCount = status is KafkaEventStatus.DeadLettered or KafkaEventStatus.Failed ? _random.Next(1, 4) : 0,
                    ErrorMessage = status is KafkaEventStatus.DeadLettered ? "Consumer deserialisation failed: unexpected schema version 3 payload."
                        : status is KafkaEventStatus.Failed ? "Transient consumer exception while projecting the ledger entry."
                        : null,
                    IsSimulated = !brokerConnected,
                    CorrelationId = $"cor_{Guid.NewGuid():N}"[..20]
                });
            }
        }
    }

    private void SeedIncidents(Dictionary<string, Integration> integrations, DateTimeOffset now)
    {
        var specs = new (string Title, string Description, IncidentSeverity Severity, IncidentStatus Status, IncidentCategory Category,
            string Team, string? Service, string? IntegrationKey, int DetectedHoursAgo, string? RootCause, string? Corrective, string? Impact)[]
        {
            ("External payment gateway timing out on outbound transfers",
                "Outbound international payments are failing after 10 second timeouts against the external gateway. Retries are exhausted and the circuit breaker has opened for the gateway dependency.",
                IncidentSeverity.Sev1, IncidentStatus.Mitigated, IncidentCategory.Integration, "Payments Platform",
                "External Payment Gateway", "external-payment-gateway", 4,
                "Upstream gateway latency spike caused by a provider-side certificate rotation.",
                "Failover routing to the secondary rail enabled; provider engaged on a P1 bridge.",
                "12.4% of outbound payments failing; approximately 480 instructions affected."),
            ("Card authorisation p95 latency above SLO",
                "Card Management System p95 authorisation latency has held above 1,400 ms for the last six hours against a 400 ms SLO.",
                IncidentSeverity.Sev2, IncidentStatus.Investigating, IncidentCategory.Performance, "Cards & Payments",
                "Card Management System", "card-management", 6,
                null, null,
                "Card present authorisations experiencing elevated decline rates at peak."),
            ("Consumer lag growing on payments.events",
                "The ledger projector consumer group is 120 messages behind on payments.events and the lag is trending upward.",
                IncidentSeverity.Sev3, IncidentStatus.Open, IncidentCategory.DataQuality, "Payments Platform",
                "Transaction Processing Service", "transaction-processing", 2,
                null, null,
                "Ledger projection delayed by up to four minutes."),
            ("Elevated 5xx responses on account lookup",
                "Account Management Service is returning intermittent 503 responses during balance lookups, raising the platform error rate above the 2% threshold.",
                IncidentSeverity.Sev2, IncidentStatus.Acknowledged, IncidentCategory.Integration, "Core Banking",
                "Account Management Service", "account-management", 1,
                null, null,
                "Balance display intermittently unavailable in internet banking."),
            ("Fraud signal duplicates observed downstream",
                "Downstream analytics has observed duplicate fraud.signal.raised events for the same transaction identifier.",
                IncidentSeverity.Sev3, IncidentStatus.Investigating, IncidentCategory.DataQuality, "Financial Crime",
                "Fraud Detection Service", "fraud-detection", 9,
                null, null,
                "Duplicate risk cases created for a small subset of transactions."),
            ("Data platform ingestion delay",
                "Regulatory reporting extracts are arriving more than 30 minutes late into the data platform.",
                IncidentSeverity.Sev3, IncidentStatus.Open, IncidentCategory.Infrastructure, "Data & Analytics",
                "Data Platform", "data-platform", 3, null, null,
                "T+1 reporting extract may slip its delivery window."),
            ("Payment processor circuit breaker open",
                "The payment processing circuit breaker opened after five consecutive downstream failures, rejecting new instructions at the API boundary.",
                IncidentSeverity.Sev2, IncidentStatus.Resolved, IncidentCategory.Integration, "Payments Platform",
                "Payment Processing Service", "payment-processing", 52,
                "Connection pool exhaustion caused by a slow downstream fraud check.",
                "Pool size increased and fraud check timeout reduced from 5s to 2s. Breaker reset manually.",
                "New payment instructions rejected for 11 minutes."),
            ("Scheduled maintenance window completed",
                "Planned database maintenance completed inside the agreed window with no customer impact.",
                IncidentSeverity.Sev4, IncidentStatus.Resolved, IncidentCategory.Infrastructure, "Platform Engineering",
                "SQL Server", null, 120,
                "Planned index rebuild on Payments and ApiRequestLogs.",
                "Maintenance completed without incident.", "No customer impact.")
        };

        var sequence = 1;
        foreach (var spec in specs)
        {
            var detectedAt = now.AddHours(-spec.DetectedHoursAgo);
            var resolved = spec.Status == IncidentStatus.Resolved;
            var incident = new Incident
            {
                Reference = $"INC-{now.Year}-{sequence++:D4}",
                Title = spec.Title,
                Description = spec.Description,
                Severity = spec.Severity,
                Status = spec.Status,
                Category = spec.Category,
                Team = spec.Team,
                AffectedService = spec.Service,
                IntegrationId = spec.IntegrationKey is null ? null : integrations[spec.IntegrationKey].Id,
                ImpactSummary = spec.Impact,
                RootCause = spec.RootCause,
                CorrectiveActions = spec.Corrective,
                CreatedBy = "ops@nexusbank.dev",
                Assignee = spec.Team == "Payments Platform" ? "Daniel Whitfield" : "Sofia Almeida",
                DetectedAt = detectedAt,
                AcknowledgedAt = spec.Status is IncidentStatus.Open ? null : detectedAt.AddMinutes(_random.Next(3, 22)),
                ResolvedAt = resolved ? detectedAt.AddMinutes(_random.Next(35, 240)) : null,
                UpdatedAt = resolved ? detectedAt.AddMinutes(240) : now.AddMinutes(-_random.Next(2, 40))
            };

            _db.Incidents.Add(incident);
            _db.IncidentActivities.Add(new IncidentActivity
            {
                IncidentId = incident.Id,
                Timestamp = detectedAt,
                Type = IncidentActivityType.Created,
                Author = "system",
                Detail = $"Incident raised automatically as {spec.Severity} from the {spec.Category} monitor."
            });
            if (incident.AcknowledgedAt is { } acknowledgedAt)
            {
                _db.IncidentActivities.Add(new IncidentActivity
                {
                    IncidentId = incident.Id,
                    Timestamp = acknowledgedAt,
                    Type = IncidentActivityType.StatusChanged,
                    Author = "daniel.whitfield@nexusbank.dev",
                    Detail = "Acknowledged by the on-call engineer and moved to Acknowledged."
                });
            }
            if (!resolved)
            {
                _db.IncidentActivities.Add(new IncidentActivity
                {
                    IncidentId = incident.Id,
                    Timestamp = detectedAt.AddMinutes(_random.Next(12, 50)),
                    Type = IncidentActivityType.Note,
                    Author = "sofia.almeida@nexusbank.dev",
                    Detail = "Correlated with the 14:20 deployment of the payment routing configuration. Dashboards checked; error budget burn rate 3.4x."
                });
            }
            if (resolved)
            {
                _db.IncidentActivities.Add(new IncidentActivity
                {
                    IncidentId = incident.Id,
                    Timestamp = incident.ResolvedAt!.Value,
                    Type = IncidentActivityType.Resolution,
                    Author = "daniel.whitfield@nexusbank.dev",
                    Detail = "Service restored and verified for 30 minutes before closing the incident."
                });
            }
        }
    }

    private void SeedTelemetry(DateTimeOffset now)
    {
        var paths = new (string Method, string Path, int Weight, double BaseLatency)[]
        {
            ("GET", "/api/v1/dashboard", 70, 96),
            ("GET", "/api/v1/integrations", 55, 64),
            ("GET", "/api/v1/payments", 90, 88),
            ("GET", "/api/v1/payments/{id}", 45, 72),
            ("POST", "/api/v1/payments", 22, 320),
            ("GET", "/api/v1/events", 48, 54),
            ("GET", "/api/v1/events/topics", 30, 48),
            ("GET", "/api/v1/observability/overview", 40, 120),
            ("GET", "/api/v1/incidents", 28, 58),
            ("GET", "/api/v1/system/health", 36, 42),
            ("GET", "/api/v1/security/audit-logs", 12, 66),
            ("POST", "/api/v1/auth/login", 18, 210),
            ("GET", "/api/v1/accounts", 26, 51),
            ("GET", "/api/v1/customers", 20, 57)
        };

        var totalWeight = paths.Sum(p => p.Weight);

        for (var hour = 47; hour >= 0; hour--)
        {
            var hourStart = now.AddHours(-hour).AddMinutes(-now.Minute);
            var businessHour = hourStart.Hour is >= 8 and <= 18;
            var volume = (businessHour ? 96 : 38) + _random.Next(0, 26);

            for (var i = 0; i < volume; i++)
            {
                var roll = _random.Next(totalWeight);
                var selected = paths[0];
                foreach (var path in paths)
                {
                    if (roll < path.Weight) { selected = path; break; }
                    roll -= path.Weight;
                }

                var status = PickWeighted((200, 880), (201, 40), (400, 26), (401, 12), (403, 6), (404, 18), (409, 4), (429, 3), (500, 9), (503, 5));
                var latency = Math.Round(selected.BaseLatency * (0.6 + _random.NextDouble() * 1.9), 1);
                if (status >= 500) latency = Math.Round(latency * 2.2, 1);

                _db.ApiRequestLogs.Add(new ApiRequestLog
                {
                    Timestamp = hourStart.AddSeconds(_random.Next(0, 3600)),
                    Method = selected.Method,
                    Path = selected.Path,
                    StatusCode = status,
                    DurationMs = latency,
                    ServiceName = selected.Path.Contains("payments") ? "Payments Platform" : "NexusBank API",
                    CorrelationId = $"cor_{Guid.NewGuid():N}"[..20],
                    TraceId = Guid.NewGuid().ToString("N"),
                    ClientIp = $"10.20.{_random.Next(0, 40)}.{_random.Next(2, 250)}",
                    IsError = status >= 400
                });
            }
        }
    }

    private void SeedAuditTrail(Dictionary<string, Role> roles, DateTimeOffset now)
    {
        var actions = new (string Action, string Entity, string Severity, AuditOutcome Outcome, string Actor)[]
        {
            ("auth.login", "User", "info", AuditOutcome.Success, "admin@nexusbank.dev"),
            ("auth.login", "User", "info", AuditOutcome.Success, "ops@nexusbank.dev"),
            ("auth.login", "User", "warning", AuditOutcome.Failure, "unknown@example.test"),
            ("auth.login", "User", "warning", AuditOutcome.Denied, "contractor@nexusbank.dev"),
            ("auth.logout", "RefreshToken", "info", AuditOutcome.Success, "dev@nexusbank.dev"),
            ("integration.update", "Integration", "info", AuditOutcome.Success, "ops@nexusbank.dev"),
            ("integration.disable", "Integration", "warning", AuditOutcome.Success, "admin@nexusbank.dev"),
            ("payment.create", "Payment", "info", AuditOutcome.Success, "dev@nexusbank.dev"),
            ("payment.reverse", "Payment", "warning", AuditOutcome.Success, "ops@nexusbank.dev"),
            ("incident.update", "Incident", "info", AuditOutcome.Success, "ops@nexusbank.dev"),
            ("security.user.create", "User", "info", AuditOutcome.Success, "admin@nexusbank.dev"),
            ("event.retry", "KafkaEvent", "info", AuditOutcome.Success, "ops@nexusbank.dev")
        };

        for (var i = 0; i < 90; i++)
        {
            var template = actions[_random.Next(actions.Length)];
            _db.AuditLogs.Add(new AuditLog
            {
                Timestamp = now.AddMinutes(-_random.Next(5, 60 * 24 * 6)),
                ActorEmail = template.Actor,
                Action = template.Action,
                EntityType = template.Entity,
                EntityId = template.Entity == "User" ? null : Guid.NewGuid().ToString(),
                Outcome = template.Outcome,
                Severity = template.Severity,
                IpAddress = $"10.20.{_random.Next(0, 40)}.{_random.Next(2, 250)}",
                CorrelationId = $"cor_{Guid.NewGuid():N}"[..20],
                Metadata = template.Action switch
                {
                    "auth.login" => "Signed in from the operations console",
                    "integration.update" => "Retry policy updated for external-payment-gateway",
                    "payment.create" => "Created payment via the sandbox API",
                    "incident.update" => "Incident status advanced to Investigating",
                    _ => "Recorded by the audit interceptor"
                }
            });
        }

        _ = roles;
    }

    private string BuildPayload(string eventType) => eventType switch
    {
        "payment.completed" => "{\"paymentId\":\"" + Guid.NewGuid() + "\",\"status\":\"Completed\",\"amount\":184.20,\"currency\":\"NZD\"}",
        "payment.failed" => "{\"paymentId\":\"" + Guid.NewGuid() + "\",\"status\":\"Failed\",\"reason\":\"PROCESSOR_REJECTED\"}",
        "payment.initiated" => "{\"paymentId\":\"" + Guid.NewGuid() + "\",\"channel\":\"API\",\"amount\":2620.00}",
        _ => "{\"eventType\":\"" + eventType + "\",\"source\":\"nexusbank\",\"schemaVersion\":3}"
    };

    private string PickBic() => new[] { "ASBBNZ2A", "ANZBNZ22", "BNZBNZ22", "WPACNZ2W" }[_random.Next(4)];

    private T PickWeighted<T>(params (T Value, int Weight)[] options)
    {
        var total = options.Sum(o => o.Weight);
        var roll = _random.Next(total);
        foreach (var option in options)
        {
            if (roll < option.Weight) return option.Value;
            roll -= option.Weight;
        }
        return options[^1].Value;
    }
}
