# Observability

This document describes how NexusBank is observed in a running environment: what is recorded, where
it is stored, what is exposed to operators, and — just as importantly — what is **not** collected.

The guiding rule in the codebase is that a number shown to an operator must be derivable from a real
record. There are no seeded dashboard metrics: every figure on the command centre, observability and
health screens is an aggregation of `ApiRequestLogs` rows, live dependency probes, or persisted
business rows.

> **Status:** all controls below are **implemented and available in the project**. None of them
> currently ships to a remote collector in the hosted deployment; see §10.

---

## 1. Instrumentation map

| Signal | Implementation | Location | Persisted? |
| --- | --- | --- | --- |
| Structured logs | Serilog console sink, custom output template, enriched with `service`, `CorrelationId`, `ClientIp` | `NexusBank.Api/Program.cs` | No — console/stdout only |
| Request telemetry | One `ApiRequestLog` row per request | `Api/Middleware/RequestTelemetryMiddleware.cs` | Yes — database |
| Correlation ids | Generated or accepted, echoed as `X-Correlation-ID` | `RequestTelemetryMiddleware` | Yes (request, audit) |
| Distributed tracing | OpenTelemetry ASP.NET Core + `HttpClient` instrumentation; W3C `Activity` trace id persisted per request | `Program.cs`, `RequestTelemetryMiddleware` | Trace **id** yes; spans no |
| Metrics scraping | Prometheus text exposition at `/metrics` | `SystemHealthService.GetPrometheusMetricsAsync` | Derived on demand |
| Traces / latency | Aggregations over `ApiRequestLogs` | `ObservabilityService` | Derived on demand |
| Audit trail | Append-only `AuditLogs` | `AuthService`, `PaymentService`, `SecurityService`, others | Yes |
| Dependency health | On-demand probes (database, cache, broker, integrations, API) | `ComponentHealthProbe` | Health response only |
| Worker health | In-memory heartbeat registry | `WorkerHeartbeatRegistry` | No — process memory |
| Alerts | Rule evaluation over live state | `ObservabilityService.GetAlertsAsync` | No — computed per read |

Serilog levels default to `Information`, with `Microsoft.AspNetCore`, EF Core command logging and
`System.Net.Http.HttpClient` overridden to `Warning` so a request does not produce a cascade of
framework noise. Request bodies, headers and tokens are never logged.

---

## 2. Correlation ids

```
X-Correlation-ID: cor_4f1c9b2a7d3e5f60
```

- If the caller supplies `X-Correlation-ID`, it is reused (allows an upstream gateway or support
  engineer to inject one).
- Otherwise the middleware generates `cor_` + 16 hex characters.
- The value is written to `HttpContext.Items["correlationId"]`, returned on the response, stored on
  the `ApiRequestLog` row, and embedded in problem-details responses and JWT rejection logs.

This is the primary join key for support: browser network tab → response body → request row → audit
entry.

---

## 3. Structured logging

Serilog writes one line per event to stdout:

```
[13:56:59 INF] User ops@nexusbank.dev signed in from 10.0.0.4
[13:57:01 WRN] Kafka broker not reachable at localhost:9092: ... Using in-process fallback.
```

`UseSerilogRequestLogging` adds a completion event per request enriched with `CorrelationId` and
`ClientIp`. Domain warnings (handled `NexusException`s) are logged at `Warning` with their code,
status and path; unexpected exceptions at `Error` with the full exception and correlation id.

On Render, stdout is captured by the platform log stream. On Docker Compose the same lines are
available with `docker compose logs -f api`.

**Not implemented:** a remote sink (Seq, Log Analytics, OpenSearch), log shipping, or PII scrubbing
rules beyond "do not log sensitive values in the first place".

---

## 4. Request telemetry

Every request except `OPTIONS` and the paths `/health`, `/metrics`, `/swagger`, `/favicon` produces
one row:

| Column | Meaning |
| --- | --- |
| `Timestamp` | UTC completion time |
| `Method`, `Path` | Request line |
| `StatusCode`, `IsError` | Outcome (`IsError` = status ≥ 400) |
| `DurationMs` | Measured end-to-end duration, 2 dp |
| `UserId` | Caller id from the bearer token, null when anonymous |
| `ServiceName` | Logical module derived from the path (`Payments Platform`, `Integration Hub`, `Event Streaming`, `Operations`, `Identity & Access`, `Observability`, `Developer Platform`, else `NexusBank API`) |
| `CorrelationId`, `TraceId` | Join keys |
| `ClientIp` | `HttpContext.Connection.RemoteIpAddress` |

Telemetry persistence failure is caught and logged as a warning; it never changes the client
response. The middleware is registered **after** authentication so `UserId` is available, and before
authorization so rejected (403) calls are still measured.

Trade-off: this design adds one `INSERT` per API request to the same database that serves the
workload. See `docs/PERFORMANCE.md` §4 for the implications and mitigations.

---

## 5. Metrics

### 5.1 Prometheus exposition

`GET /metrics` (anonymous) and `GET /api/v1/system/metrics` (permission `health:read`) return
Prometheus text:

```
nexusbank_api_requests_last_hour 179
nexusbank_api_errors_last_hour 2
nexusbank_api_latency_avg_ms 148.31
nexusbank_uptime_seconds 1320
nexusbank_component_up{component="sql_server",type="Database"} 1
nexusbank_component_up{component="redis",type="Cache"} 0.5
nexusbank_component_up{component="apache_kafka",type="Message broker"} 0.5
```

Component gauges are `1` healthy, `0.5` degraded, `0` down — computed by running the probes at scrape
time, so a scrape is a real dependency check.

`infrastructure/monitoring/prometheus.yml` contains a starter scrape configuration targeting
`api:8080` inside the Compose network. Prometheus itself is **not** part of the Compose stack; the
file is a starting point, not a deployed monitoring service.

### 5.2 OpenTelemetry

`AddOpenTelemetry()` registers ASP.NET Core and `HttpClient` instrumentation for traces and metrics.
An OTLP exporter is attached **only** when `OpenTelemetry:OtlpEndpoint` is set; otherwise nothing is
exported and instrumentation is effectively local. This is the correct default: an exporter pointed
at nothing produces noise, not observability.

There is no collector, agent or APM backend provisioned in any documented environment.

---

## 6. Health endpoints

| Endpoint | Auth | Purpose | Semantics |
| --- | --- | --- | --- |
| `GET /health/live` | anonymous | Liveness | Process is running and serving; no dependency checks |
| `GET /health` | anonymous | Readiness / status | Compact component truth + availability over the last 24h |
| `GET /api/v1/system/health` | `health:read` | Operator view | Every component with latency/mode, worker heartbeats, recent failures, dead-letter depth |

`/health` returns `degraded` in the current Render deployment by design:

```json
{
  "status": "degraded",
  "database": "connected",
  "cache": "In-process memory cache (Redis unreachable)",
  "eventBus": "In-process development bus (Kafka disabled by configuration)",
  "availabilityPct": 98.417,
  "uptimeSeconds": 1320,
  "checkedAt": "2026-11-15T13:56:33.3435366+00:00"
}
```

Overall status rules (implemented in `SystemHealthService`):

- `unhealthy` — only when the database **or** the API component is `down`.
- `degraded` — any component or worker is `down`/`degraded`.
- `healthy` — everything nominal.

A degraded cache or broker reduces throughput but does not stop the platform serving, so it must not
page as an outage.

The Docker healthcheck uses `/health/live` (liveness, no database dependency) so a database blip does
not cause the orchestrator to restart a healthy container.

---

## 7. Dependency health

`ComponentHealthProbe.CheckComponentsAsync` probes:

| Component | Probe | Degraded when |
| --- | --- | --- |
| SQL Server | `Database.CanConnectAsync()` | Cannot connect (reported `down`) |
| Redis | `IsDistributed` (connection + `PING`) then a set/get round trip on `health:probe` | Redis not in use; in-process memory cache serving |
| Apache Kafka | `IEventBus.BrokerConnected` (admin metadata call, cached 30 s) | Broker disabled or unreachable |
| NexusBank API | 5xx rate over the last hour | >2% degraded, >5% down |
| Integration mesh | Count of enabled integrations by status | Any degraded/down |
| Database | (cloud platform's responsibility) | — |

The Redis and Kafka rows always carry the *mode string* as their detail, so the health page states
which implementation is actually serving requests rather than a generic "unavailable".

---

## 8. Worker health

Each `BackgroundService` records a heartbeat after every cycle:

| Worker | Interval | Reports |
| --- | --- | --- |
| Outbox publisher | 5 s | messages relayed / examined |
| Integration health monitor | 60 s (8 s initial delay) | integrations probed |
| Telemetry retention | 6 h (30 s initial delay) | rows purged per table |
| Kafka consumer | continuous | subscription / last consumed offset / idle reason |

`WorkerHeartbeatRegistry` is an in-memory `ConcurrentDictionary`. Status rules in
`ComponentHealthProbe.CheckWorkersAsync`:

- `down` — `ConsecutiveFailures >= 3`
- `degraded` — last heartbeat older than 180 s, or no heartbeat yet
- `healthy` — otherwise

**Known consequence:** heartbeats do not survive a restart, so for the first cycle after boot the
health page reports workers as degraded ("No worker heartbeat has been recorded yet"). This is
intentional honesty, not a fault — but it means worker health cannot be scraped from `/metrics`
(it is only in the authenticated `/api/v1/system/health` payload).

---

## 9. Traces, error groups and alerts

### Traces

`GET /api/v1/observability/traces` returns recent request rows as "traces": correlation id, W3C trace
id (when `Activity.Current` was present), method, path, status, duration, service and error flag.

This is a request-level trace list, **not** a span tree. There is no collector, no sampling
configuration and no cross-service span propagation beyond the standard `traceparent` header emitted
by ASP.NET Core. Treat it as a fast "what happened to this request" view, not as distributed tracing
infrastructure.

### Error groups

`GET /observability/errors` groups rows with status ≥ 400 by `(path, method, status)`, with count,
mean latency and last-seen. This is how a regression is spotted without a log aggregation platform.

### Alerts

`GET /observability/alerts` evaluates these rules on every read:

| Rule | Threshold | Severity |
| --- | --- | --- |
| Integration down / degraded | current integration status | critical / warning |
| API error rate | >2% of ≥20 requests in the last hour (critical >5%) | warning / critical |
| Dead-letter depth | >0 (critical >5) | warning / critical |
| Sev1 / Sev2 incidents open | incident severity | critical / warning |
| Kafka unreachable | `!BrokerConnected` | info |
| Redis not in use | `!IsDistributed` | info |

**Not implemented:** alert state persistence, deduplication windows, acknowledgement, escalation,
silence periods, or outbound notification (email, Slack, PagerDuty). Alerts are an operator-facing
read model, not a paging system. The "Alerts (n)" badge in the console header is this same payload.

---

## 10. What is not collected

State this plainly in a review; none of these is pretend-implemented:

| Capability | Status |
| --- | --- |
| Remote log aggregation / search | Not implemented — stdout only |
| Span-level distributed tracing | Not implemented — request-level only |
| APM / application performance backend | Not implemented |
| Redis cache hit/miss ratio | **Not collected** — the cache is probed for reachability, not instrumented for hit rate |
| Kafka consumer lag from the broker (`kafka-consumer-groups`) | Not collected — lag is read from the `ConsumerGroups` table maintained by the platform, which is seeded/simulated data in the current build |
| Broker-side producer/consumer metrics (bytes, ISR, under-replicated partitions) | Not collected |
| Database query latency / plan / connection-pool metrics | Not collected — only `CanConnect` and request-level latency |
| Host metrics (CPU, memory, disk, GC) | Not collected by the application; rely on the hosting platform |
| Slow-query logging | Not collected |
| Frontend real-user monitoring, Core Web Vitals | Not collected |
| Synthetic uptime monitoring from outside the platform | Not configured |
| Log retention beyond 7 days | `TelemetryRetentionWorker` deletes request logs older than 7 days |
| PII scrubbing pipeline | Not implemented (discipline is "do not log it") |

---

## 11. Troubleshooting workflow

A worked example — "the console shows server errors on the payments page":

1. **Reproduce with a correlation id.** Open DevTools → Network → the failing request. Copy
   `X-Correlation-ID` from the response headers (also present in the problem-details body).
2. **Identify the scope.** `GET /api/v1/observability/errors?hours=1` — is this one path, one status,
   or a spread? A single `POST /api/v1/payments` group is a business-rule regression; a spread across
   many paths is infrastructure.
3. **Read the request row.** `GET /api/v1/observability/traces?limit=50` and match the correlation
   id: method, path, status, duration, `serviceName`, `traceId`.
4. **Read the log line.** Platform logs (Render → Logs; Compose → `docker compose logs -f api`) for
   the same correlation id. Unexpected failures are logged at `Error` with the exception; handled
   domain failures at `Warning` with the code.
5. **Check whether it is the platform or the caller.** `GET /api/v1/system/health` — if the database
   is `down`, nothing else matters. If only Redis/Kafka are degraded, expect fallback behaviour, not
   5xx.
6. **Check the write path.** For payments, `GET /api/v1/payments/{id}` → `journey`,
   `ledgerBalanced`, `failureCode`. A `409` with `idempotency_key_reuse` is a client retry defect, not
   a server error.
7. **Check the audit trail.** `GET /api/v1/security/audit-logs?outcome=failure` for denied or failed
   security-relevant actions in the same window.
8. **Confirm recovery.** Repeat step 2: the error group should stop growing, and `/metrics`
   `nexusbank_api_errors_last_hour` should fall as the one-hour window rolls.

Incident-specific procedures (API down, database failure, Redis/Kafka outage, dead letters, payment
defects, auth failures) are in `docs/OPERATIONS_RUNBOOK.md`.

---

## 12. Operational targets (aspirational, not measured)

These are the thresholds the platform is designed around; they are **not** measured results and no
load test has been executed against the hosted deployment.

| Objective | Threshold | Where it is enforced/observed |
| --- | --- | --- |
| API availability | ≥ 99.5% monthly | Derived from request telemetry in-app; no external monitor |
| 5xx rate | < 2% of requests | Alert rule, `ObservabilityService` |
| p95 latency, read endpoints | < 500 ms | Displayed, not enforced |
| Outbox lag | < 30 s | Worker interval 5 s; visible in event stream |
| Dead letters | 0 | Alert when > 0 |

`docs/PERFORMANCE.md` describes how to measure these properly with k6 instead of asserting them.
