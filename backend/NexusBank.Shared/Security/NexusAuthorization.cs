namespace NexusBank.Shared.Security;

/// <summary>Role slugs used across the platform. Seeded as system roles.</summary>
public static class NexusRoles
{
    public const string Administrator = "administrator";
    public const string OperationsEngineer = "operations-engineer";
    public const string Developer = "developer";
    public const string ReadOnlyAuditor = "read-only-auditor";
}

/// <summary>
/// Fine-grained permission codes. JWT access tokens carry these as individual
/// <c>permission</c> claims and API policies are built from them.
/// </summary>
public static class NexusPermissions
{
    public const string DashboardRead = "dashboard:read";
    public const string IntegrationsRead = "integrations:read";
    public const string IntegrationsWrite = "integrations:write";
    public const string PaymentsRead = "payments:read";
    public const string PaymentsWrite = "payments:write";
    public const string EventsRead = "events:read";
    public const string EventsWrite = "events:write";
    public const string ObservabilityRead = "observability:read";
    public const string IncidentsRead = "incidents:read";
    public const string IncidentsWrite = "incidents:write";
    public const string SecurityRead = "security:read";
    public const string SecurityWrite = "security:write";
    public const string AuditRead = "audit:read";
    public const string HealthRead = "health:read";
    public const string ExplorerExecute = "explorer:execute";

    public static readonly IReadOnlyList<(string Code, string Description, string Category)> All = new[]
    {
        (DashboardRead, "View the operational command centre", "Platform"),
        (IntegrationsRead, "View integrations and topology", "Integrations"),
        (IntegrationsWrite, "Create, update and disable integrations", "Integrations"),
        (PaymentsRead, "View payments and journeys", "Payments"),
        (PaymentsWrite, "Create, refund and reverse payments", "Payments"),
        (EventsRead, "View topics, events and consumer groups", "Events"),
        (EventsWrite, "Publish events and replay dead letters", "Events"),
        (ObservabilityRead, "View telemetry, traces and logs", "Observability"),
        (IncidentsRead, "View incidents", "Operations"),
        (IncidentsWrite, "Create and progress incidents", "Operations"),
        (SecurityRead, "View users, roles and sessions", "Security"),
        (SecurityWrite, "Manage users, roles and access", "Security"),
        (AuditRead, "View the security audit trail", "Security"),
        (HealthRead, "View component health", "Platform"),
        (ExplorerExecute, "Execute requests from the API explorer", "Developers")
    };

    public static IEnumerable<string> ForRole(string roleSlug) => roleSlug switch
    {
        NexusRoles.Administrator => All.Select(p => p.Code),
        NexusRoles.OperationsEngineer => new[]
        {
            DashboardRead, IntegrationsRead, IntegrationsWrite, PaymentsRead, PaymentsWrite,
            EventsRead, EventsWrite, ObservabilityRead, IncidentsRead, IncidentsWrite,
            HealthRead, ExplorerExecute
        },
        NexusRoles.Developer => new[]
        {
            DashboardRead, IntegrationsRead, PaymentsRead, PaymentsWrite, EventsRead,
            EventsWrite, ObservabilityRead, IncidentsRead, HealthRead, ExplorerExecute
        },
        NexusRoles.ReadOnlyAuditor => new[]
        {
            DashboardRead, IntegrationsRead, PaymentsRead, EventsRead, ObservabilityRead,
            IncidentsRead, SecurityRead, AuditRead, HealthRead
        },
        _ => Array.Empty<string>()
    };
}
