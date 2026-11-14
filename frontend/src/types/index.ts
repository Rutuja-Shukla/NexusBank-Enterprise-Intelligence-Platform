/* Domain contracts. Mirrors the API DTOs; enums are serialised as strings. */

export type IntegrationCategory = 'Core' | 'Payments' | 'Cards' | 'Risk' | 'Data' | 'External'
export type IntegrationStatus = 'Healthy' | 'Degraded' | 'Down' | 'Maintenance' | 'Unknown'
export type IntegrationAuthType = 'ApiKey' | 'OAuth2ClientCredentials' | 'MutualTls' | 'JwtBearer' | 'None'
export type PaymentStatus = 'Initiated' | 'Validating' | 'Processing' | 'Pending' | 'Completed' | 'Failed' | 'Reversed'
export type PaymentType = 'Transfer' | 'CardPayment' | 'DirectDebit' | 'Refund'
export type PaymentStepName = 'Initiated' | 'Validation' | 'AccountLookup' | 'PaymentProcessing' | 'ExternalGateway' | 'Confirmation' | 'Reversal'
export type StepStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped'
export type LedgerEntryType = 'Debit' | 'Credit'
export type KafkaEventStatus = 'Produced' | 'Consumed' | 'Failed' | 'DeadLettered' | 'Retried'
export type ConsumerGroupState = 'Stable' | 'Rebalancing' | 'Empty' | 'Dead'
export type IncidentSeverity = 'Sev1' | 'Sev2' | 'Sev3' | 'Sev4'
export type IncidentStatus = 'Open' | 'Acknowledged' | 'Investigating' | 'Mitigated' | 'Resolved'
export type IncidentCategory = 'Integration' | 'Payment' | 'Security' | 'Performance' | 'DataQuality' | 'Infrastructure'
export type IncidentActivityType = 'Created' | 'StatusChanged' | 'Note' | 'Assignment' | 'Escalation' | 'Mitigation' | 'Resolution'
export type AuditOutcome = 'Success' | 'Failure' | 'Denied'
export type AccountStatus = 'Active' | 'Dormant' | 'Frozen' | 'Closed'
export type KycStatus = 'Verified' | 'Pending' | 'Rejected'
export type PermissionCode = string
export type HealthState = 'healthy' | 'degraded' | 'down' | 'unknown'

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
}

export interface UserProfile {
  id: string
  email: string
  displayName: string
  role: string
  roleSlug: string
  permissions: string[]
  lastLoginAt: string | null
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  refreshToken: string
  user: UserProfile
}

export interface SessionDto {
  id: string
  createdAt: string
  expiresAt: string
  lastUsedAt: string | null
  isCurrent: boolean
  ipAddress: string | null
  userAgent: string | null
}

export interface AccessHistoryPoint {
  label: string
  signIns: number
  failures: number
}

export interface RetryPolicy {
  maxAttempts: number
  baseDelayMs: number
  maxDelayMs: number
  circuitBreakerFailureThreshold: number
  circuitBreakerBreakSeconds: number
}

export interface IntegrationListItem {
  id: string
  name: string
  key: string
  description: string
  category: IntegrationCategory
  status: IntegrationStatus
  authType: IntegrationAuthType
  enabled: boolean
  isInternal: boolean
  isSimulated: boolean
  ownerTeam: string
  version: string
  baseUrl: string
  avgLatencyMs: number
  failureRatePct: number
  requestCount24h: number
  lastSuccessAt: string | null
  lastFailureAt: string | null
  lastFailureReason: string | null
}

export interface IntegrationLink {
  id: string
  name: string
  protocol: string
  isCritical: boolean
  status: IntegrationStatus
}

export interface HealthSample {
  timestamp: string
  status: IntegrationStatus
  latencyMs: number
  successRatePct: number
}

export interface IntegrationAuditEntry {
  id: string
  timestamp: string
  action: string
  actor: string
  details: string
}

export interface IntegrationDetail extends IntegrationListItem {
  healthEndpointPath: string
  authHeaderName: string | null
  secretReference: string | null
  timeoutSeconds: number
  retryPolicy: RetryPolicy
  createdAt: string
  updatedAt: string
  dependsOn: IntegrationLink[]
  consumedBy: IntegrationLink[]
  healthHistory: HealthSample[]
  auditTrail: IntegrationAuditEntry[]
}

export interface TopologyNode {
  id: string
  name: string
  key: string
  category: IntegrationCategory
  status: IntegrationStatus
  enabled: boolean
  isSimulated: boolean
  baseUrl: string
  ownerTeam: string
  version: string
  avgLatencyMs: number
  failureRatePct: number
  requestCount24h: number
  x: number
  y: number
  lastSuccessAt: string | null
  lastFailureAt: string | null
}

export interface TopologyEdge {
  id: string
  source: string
  target: string
  protocol: string
  isCritical: boolean
  description: string
  degraded: boolean
}

export interface Topology {
  nodes: TopologyNode[]
  edges: TopologyEdge[]
  generatedAt: string
}

export interface HealthProbe {
  integrationId: string
  name: string
  status: IntegrationStatus
  latencyMs: number
  telemetrySource: string
  detail: string
  checkedAt: string
}

export interface PaymentListItem {
  id: string
  reference: string
  status: PaymentStatus
  type: PaymentType
  amount: number
  currency: string
  creditorName: string
  debtorAccountNumber: string
  channel: string
  integrationName: string | null
  riskScore: number
  processingDurationMs: number | null
  createdAt: string
  completedAt: string | null
}

export interface PaymentStep {
  sequence: number
  step: PaymentStepName
  status: StepStatus
  serviceName: string
  timestamp: string
  durationMs: number
  offsetMs: number
  detail: string | null
  errorDetail: string | null
}

export interface LedgerEntry {
  entryType: LedgerEntryType
  accountNumber: string
  amount: number
  currency: string
  postedAt: string
  description: string
}

export interface PaymentDetail extends PaymentListItem {
  creditorAccountNumber: string
  creditorBic: string | null
  integrationId: string | null
  description: string | null
  failureCode: string | null
  failureReason: string | null
  correlationId: string
  idempotencyKey: string
  updatedAt: string
  journey: PaymentStep[]
  ledgerEntries: LedgerEntry[]
  ledgerDebitTotal: number
  ledgerCreditTotal: number
  ledgerBalanced: boolean
}

export interface PaymentCreated {
  id: string
  reference: string
  status: PaymentStatus
  amount: number
  currency: string
  createdAt: string
  replayed: boolean
  correlationId: string
}

export interface PaymentStats {
  totalCount: number
  totalVolume: number
  successRatePct: number
  failedCount: number
  pendingCount: number
  averageProcessingMs: number
  byStatus: Record<string, number>
  byType: Record<string, number>
}

export interface AccountOption {
  id: string
  label: string
  maskedNumber: string
  currency: string
  availableBalance: number
  customerName: string
}

export interface Topic {
  id: string
  name: string
  description: string
  partitions: number
  replicationFactor: number
  retentionHours: number
  ownerTeam: string
  eventCount24h: number
  failedCount24h: number
  totalLag: number
  lastEventAt: string | null
  consumerGroups: number
}

export interface KafkaEvent {
  id: string
  topicId: string
  topic: string
  eventKey: string
  eventType: string
  status: KafkaEventStatus
  partition: number
  offset: number
  producedAt: string
  consumedAt: string | null
  retryCount: number
  isSimulated: boolean
  errorMessage: string | null
  consumerGroup: string | null
  payload: string
  headers: string | null
  correlationId: string | null
}

export interface ConsumerGroup {
  id: string
  groupId: string
  topic: string
  state: ConsumerGroupState
  members: number
  lag: number
  committedOffset: number
  ownerTeam: string
  lastHeartbeatAt: string
}

export interface StreamStatus {
  mode: string
  brokerConnected: boolean
  isFallback: boolean
  detail: string
  topicCount: number
  eventsLastHour: number
  deadLetterCount: number
  totalLag: number
  checkedAt: string
}

export interface TrafficPoint {
  bucket: string
  timestamp: string
  requests: number
  errors: number
  avgLatencyMs: number
  p95LatencyMs: number
}

export interface VolumePoint {
  bucket: string
  timestamp: string
  volume: number
  count: number
}

export interface LatencyDistribution {
  bucket: string
  count: number
}

export interface ErrorGroup {
  path: string
  method: string
  statusCode: number
  count: number
  avgLatencyMs: number
  lastSeenAt: string
}

export interface Trace {
  correlationId: string
  traceId: string | null
  method: string
  path: string
  statusCode: number
  durationMs: number
  timestamp: number | string
  isError: boolean
  serviceName: string | null
}

export interface LogEntry {
  id: number
  timestamp: string
  level: string
  source: string
  message: string
  correlationId: string | null
  traceId: string | null
  path: string | null
  statusCode: number | null
  durationMs: number | null
}

export interface Dependency {
  name: string
  type: string
  status: string
  detail: string
  latencyMs: number
  mode: string | null
}

export interface ObservabilityOverview {
  requestVolume24h: number
  avgLatencyMs: number
  p95LatencyMs: number
  errorRatePct: number
  availabilityPct: number
  activeIncidents: number
  servicesUp: number
  servicesDown: number
  uptimeSeconds: number
  generatedAt: string
  dependencies: Dependency[]
}

export interface OperationalAlert {
  id: string
  title: string
  detail: string
  severity: string
  source: string
  detectedAt: string
  linkTo: string | null
  status: string
}

export interface AlertResponse {
  alerts: OperationalAlert[]
  generatedAt: string
}

export interface ServiceNode {
  id: string
  name: string
  status: IntegrationStatus
  latencyMs: number
  errorRatePct: number
  requestCount24h: number
  incidents: number
}

export interface DashboardSummary {
  integrationsTotal: number
  integrationsActive: number
  integrationsDegraded: number
  integrationsDown: number
  apiRequests24h: number
  apiRequestsChangePct: number
  transactions24h: number
  transactionVolume24h: number
  transactionChangePct: number
  transactionSuccessRatePct: number
  avgLatencyMs: number
  latencyChangePct: number
  activeIncidents: number
  sev1Incidents: number
  availabilityPct: number
  eventsLastHour: number
  deadLetterCount: number
  generatedAt: string
}

export interface ActivityItem {
  id: string
  kind: string
  title: string
  detail: string
  tone: string
  timestamp: string
  linkTo: string | null
}

export interface IncidentListItem {
  id: string
  reference: string
  title: string
  severity: IncidentSeverity
  status: IncidentStatus
  category: IncidentCategory
  team: string
  affectedService: string | null
  assignee: string | null
  detectedAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
  activityCount: number
  minutesOpen: number
}

export interface IncidentActivity {
  id: string
  timestamp: string
  type: IncidentActivityType
  author: string
  detail: string
}

export interface IncidentDetail extends Omit<IncidentListItem, 'activityCount' | 'minutesOpen'> {
  description: string
  integrationId: string | null
  integrationName: string | null
  impactSummary: string | null
  rootCause: string | null
  correctiveActions: string | null
  createdBy: string
  updatedAt: string
  activities: IncidentActivity[]
}

export interface IncidentStats {
  open: number
  acknowledged: number
  investigating: number
  mitigated: number
  resolved: number
  sev1: number
  sev2: number
  meanTimeToResolveMinutes: number
}

export interface Role {
  id: string
  name: string
  slug: string
  description: string
  isSystem: boolean
  userCount: number
  permissions: string[]
}

export interface PermissionDto {
  code: string
  description: string
  category: string
}

export interface UserListItem {
  id: string
  email: string
  displayName: string
  role: string
  roleSlug: string
  isActive: boolean
  isLockedOut: boolean
  createdAt: string
  lastLoginAt: string | null
  activeSessions: number
}

export interface AuditLog {
  id: string
  timestamp: string
  actor: string
  action: string
  entityType: string
  entityId: string | null
  outcome: AuditOutcome
  severity: string
  ipAddress: string | null
  correlationId: string | null
  metadata: string | null
}

export interface SystemHealth {
  overallStatus: string
  checkedAt: string
  uptimeSeconds: number
  version: string
  environment: string
  databaseConnected: boolean
  cacheMode: string
  eventBusMode: string
  components: Dependency[]
  workers: Dependency[]
  recentFailures: ActivityItem[]
  availabilityPct: number
  deadLetterCount: number
}

export interface EndpointDescriptor {
  method: string
  path: string
  summary: string
  category: string
  permission: string
  requiresAuth: boolean
  sampleBody: string | null
  pathParameters: string | null
}

export interface ExecuteResponse {
  statusCode: number
  reasonPhrase: string
  durationMs: number
  body: string
  bodyIsJson: boolean
  sizeBytes: number
  headers: Record<string, string>
  error: string | null
  executedAt: string
}

export interface Dashboard {
  summary: DashboardSummary
  traffic: TrafficPoint[]
  volume: VolumePoint[]
  recentPayments: PaymentListItem[]
  activity: ActivityItem[]
  activeIncidents: IncidentListItem[]
  services: Dependency[]
  topologyNodes: TopologyNode[]
  topologyEdges: TopologyEdge[]
  generatedAt: string
}

export interface Customer {
  id: string
  customerNumber: string
  name: string
  email: string
  segment: string
  kycStatus: KycStatus
  isActive: boolean
  accountCount: number
  createdAt: string
}

export interface Account {
  id: string
  accountNumber: string
  customerName: string
  productType: string
  currency: string
  balance: number
  availableBalance: number
  status: AccountStatus
  openedAt: string
}
