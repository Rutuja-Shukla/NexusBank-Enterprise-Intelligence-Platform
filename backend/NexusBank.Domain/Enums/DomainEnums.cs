namespace NexusBank.Domain.Enums;

public enum IntegrationCategory
{
    Core = 0,
    Payments = 1,
    Cards = 2,
    Risk = 3,
    Data = 4,
    External = 5
}

public enum IntegrationAuthType
{
    ApiKey = 0,
    OAuth2ClientCredentials = 1,
    MutualTls = 2,
    JwtBearer = 3,
    None = 4
}

public enum IntegrationStatus
{
    Healthy = 0,
    Degraded = 1,
    Down = 2,
    Maintenance = 3,
    Unknown = 4
}

public enum PaymentStatus
{
    Initiated = 0,
    Validating = 1,
    Processing = 2,
    Pending = 3,
    Completed = 4,
    Failed = 5,
    Reversed = 6
}

public enum PaymentType
{
    Transfer = 0,
    CardPayment = 1,
    DirectDebit = 2,
    Refund = 3
}

public enum PaymentStep
{
    Initiated = 0,
    Validation = 1,
    AccountLookup = 2,
    PaymentProcessing = 3,
    ExternalGateway = 4,
    Confirmation = 5,
    Reversal = 6
}

public enum PaymentStepStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}

public enum LedgerEntryType
{
    Debit = 0,
    Credit = 1
}

public enum KafkaEventStatus
{
    Produced = 0,
    Consumed = 1,
    Failed = 2,
    DeadLettered = 3,
    Retried = 4
}

public enum ConsumerGroupState
{
    Stable = 0,
    Rebalancing = 1,
    Empty = 2,
    Dead = 3
}

public enum IncidentSeverity
{
    Sev1 = 0,
    Sev2 = 1,
    Sev3 = 2,
    Sev4 = 3
}

public enum IncidentStatus
{
    Open = 0,
    Acknowledged = 1,
    Investigating = 2,
    Mitigated = 3,
    Resolved = 4
}

public enum IncidentCategory
{
    Integration = 0,
    Payment = 1,
    Security = 2,
    Performance = 3,
    DataQuality = 4,
    Infrastructure = 5
}

public enum IncidentActivityType
{
    Created = 0,
    StatusChanged = 1,
    Note = 2,
    Assignment = 3,
    Escalation = 4,
    Mitigation = 5,
    Resolution = 6
}

public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    Denied = 2
}

public enum AccountStatus
{
    Active = 0,
    Dormant = 1,
    Frozen = 2,
    Closed = 3
}

public enum KycStatus
{
    Verified = 0,
    Pending = 1,
    Rejected = 2
}
