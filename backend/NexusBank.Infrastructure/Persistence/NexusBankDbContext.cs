using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NexusBank.Application.Common.Interfaces;
using NexusBank.Domain.Entities;

namespace NexusBank.Infrastructure.Persistence;

public class NexusBankDbContext : DbContext, IApplicationDbContext
{
    public NexusBankDbContext(DbContextOptions<NexusBankDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<IntegrationDependency> IntegrationDependencies => Set<IntegrationDependency>();
    public DbSet<IntegrationHealthSample> IntegrationHealthSamples => Set<IntegrationHealthSample>();
    public DbSet<IntegrationAuditEntry> IntegrationAuditEntries => Set<IntegrationAuditEntry>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<EventTopic> EventTopics => Set<EventTopic>();
    public DbSet<KafkaEvent> KafkaEvents => Set<KafkaEvent>();
    public DbSet<ConsumerGroup> ConsumerGroups => Set<ConsumerGroup>();

    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentActivity> IncidentActivities => Set<IncidentActivity>();
    public DbSet<ApiRequestLog> ApiRequestLogs => Set<ApiRequestLog>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>
    /// SQLite has no native DateTimeOffset type, so ordering and range predicates fail unless
    /// the value is stored as a comparable scalar. SQL Server keeps its native datetimeoffset.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.RefreshTokens).WithOne(t => t.User).HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.DirectPermissions).WithOne(p => p.User).HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(400);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasMany(x => x.Permissions).WithOne(p => p.Role).HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Permission>(e =>
        {
            e.ToTable("Permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(240);
            e.Property(x => x.Category).HasMaxLength(60);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(r => r.Permissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany(p => p.Roles).HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserPermission>(e =>
        {
            e.ToTable("UserPermissions");
            e.HasKey(x => new { x.UserId, x.PermissionId });
            e.HasOne(x => x.User).WithMany(u => u.DirectPermissions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(256);
            e.Property(x => x.RevokedReason).HasMaxLength(80);
            e.HasIndex(x => x.TokenHash);
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasMaxLength(120).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(80);
            e.Property(x => x.Severity).HasMaxLength(20);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(80);
            e.Property(x => x.Metadata).HasMaxLength(2000);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.ActorEmail, x.Timestamp });
        });

        b.Entity<Integration>(e =>
        {
            e.ToTable("Integrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.BaseUrl).HasMaxLength(400).IsRequired();
            e.Property(x => x.HealthEndpointPath).HasMaxLength(200);
            e.Property(x => x.AuthHeaderName).HasMaxLength(80);
            e.Property(x => x.SecretReference).HasMaxLength(120);
            e.Property(x => x.OwnerTeam).HasMaxLength(80);
            e.Property(x => x.Version).HasMaxLength(20);
            e.Property(x => x.LastFailureReason).HasMaxLength(500);
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.OutgoingDependencies).WithOne(d => d.Source).HasForeignKey(d => d.SourceIntegrationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.IncomingDependencies).WithOne(d => d.Target).HasForeignKey(d => d.TargetIntegrationId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.HealthSamples).WithOne(s => s.Integration).HasForeignKey(s => s.IntegrationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<IntegrationDependency>(e =>
        {
            e.ToTable("IntegrationDependencies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Protocol).HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(240);
            e.HasIndex(x => new { x.SourceIntegrationId, x.TargetIntegrationId }).IsUnique();
        });

        b.Entity<IntegrationHealthSample>(e =>
        {
            e.ToTable("IntegrationHealthSamples");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.IntegrationId, x.Timestamp });
        });

        b.Entity<IntegrationAuditEntry>(e =>
        {
            e.ToTable("IntegrationAuditEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(60).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.Details).HasMaxLength(1000);
            e.HasIndex(x => new { x.IntegrationId, x.Timestamp });
            e.HasOne(x => x.Integration).WithMany().HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Segment).HasMaxLength(40);
            e.HasIndex(x => x.CustomerNumber).IsUnique();
            e.HasIndex(x => x.Email);
        });

        b.Entity<Account>(e =>
        {
            e.ToTable("Accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountNumber).HasMaxLength(34).IsRequired();
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.ProductType).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.Property(x => x.AvailableBalance).HasPrecision(18, 2);
            e.HasIndex(x => x.AccountNumber).IsUnique();
            e.HasOne(x => x.Customer).WithMany(c => c.Accounts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(40).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(120);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.DebtorAccountNumber).HasMaxLength(34);
            e.Property(x => x.CreditorName).HasMaxLength(140);
            e.Property(x => x.CreditorAccountNumber).HasMaxLength(34);
            e.Property(x => x.CreditorBic).HasMaxLength(11);
            e.Property(x => x.Channel).HasMaxLength(40);
            e.Property(x => x.FailureCode).HasMaxLength(60);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(280);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.IdempotencyKey, x.CreatedAt });
            e.HasOne(x => x.DebtorAccount).WithMany().HasForeignKey(x => x.DebtorAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Integration).WithMany().HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Events).WithOne(ev => ev.Payment).HasForeignKey(ev => ev.PaymentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.LedgerEntries).WithOne(l => l.Payment).HasForeignKey(l => l.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PaymentEvent>(e =>
        {
            e.ToTable("PaymentEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceName).HasMaxLength(80);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.ErrorDetail).HasMaxLength(500);
            e.HasIndex(x => new { x.PaymentId, x.Sequence });
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("LedgerEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountNumber).HasMaxLength(34);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Description).HasMaxLength(240);
            e.HasIndex(x => x.PaymentId);
        });

        b.Entity<EventTopic>(e =>
        {
            e.ToTable("EventTopics");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(400);
            e.Property(x => x.OwnerTeam).HasMaxLength(80);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Events).WithOne(ev => ev.Topic).HasForeignKey(ev => ev.TopicId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ConsumerGroups).WithOne(g => g.Topic).HasForeignKey(g => g.TopicId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<KafkaEvent>(e =>
        {
            e.ToTable("KafkaEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventKey).HasMaxLength(200);
            e.Property(x => x.EventType).HasMaxLength(200);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.Property(x => x.CorrelationId).HasMaxLength(80);
            e.HasIndex(x => x.ProducedAt);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.ConsumerGroup).WithMany().HasForeignKey(x => x.ConsumerGroupId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ConsumerGroup>(e =>
        {
            e.ToTable("ConsumerGroups");
            e.HasKey(x => x.Id);
            e.Property(x => x.GroupId).HasMaxLength(200).IsRequired();
            e.Property(x => x.OwnerTeam).HasMaxLength(80);
            e.HasIndex(x => new { x.GroupId, x.TopicId }).IsUnique();
        });

        b.Entity<Incident>(e =>
        {
            e.ToTable("Incidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(30).IsRequired();
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Team).HasMaxLength(80);
            e.Property(x => x.AffectedService).HasMaxLength(120);
            e.Property(x => x.ImpactSummary).HasMaxLength(500);
            e.Property(x => x.RootCause).HasMaxLength(1000);
            e.Property(x => x.CorrectiveActions).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.Assignee).HasMaxLength(120);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.Status, x.Severity });
            e.HasOne(x => x.Integration).WithMany().HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Activities).WithOne(a => a.Incident).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<IncidentActivity>(e =>
        {
            e.ToTable("IncidentActivities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Author).HasMaxLength(256);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.HasIndex(x => new { x.IncidentId, x.Timestamp });
        });

        b.Entity<ApiRequestLog>(e =>
        {
            e.ToTable("ApiRequestLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Method).HasMaxLength(10);
            e.Property(x => x.Path).HasMaxLength(400);
            e.Property(x => x.ServiceName).HasMaxLength(80);
            e.Property(x => x.CorrelationId).HasMaxLength(80);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.Property(x => x.ClientIp).HasMaxLength(64);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.StatusCode, x.Timestamp });
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(120).IsRequired();
            e.Property(x => x.Topic).HasMaxLength(200).IsRequired();
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasMaxLength(60).IsRequired();
            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.RequestHash).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        });
    }
}
