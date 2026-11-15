# Performance engineering

This document describes how the current implementation behaves under load, which optimisations are
actually present in the code, where the known bottlenecks are, and how to measure the system properly.

> **Measurement policy.** No load test has been executed against the hosted deployment. Every
> number in this repository is either a documented target or a single-observation smoke reading with
> its conditions stated. Nothing here is a benchmark result.

---

## 1. Observed single-request behaviour

Recorded while auditing the hosted deployment (2026-11-15, `nexusbank-api-w53j.onrender.com`, Render
free instance, SQLite, Redis and Kafka disabled in that environment). These are **sequential,
single-request timings**, not throughput figures:

| Request | Observed |
| --- | --- |
| `GET /health/live` (direct to Render) | ~176 ms total, ~80 ms TLS/connect, ~95 ms server |
| `GET /health` (direct) | ~164 ms total |
| `GET /api/v1/auth/me` via the Vercel proxy, no token | ~182 ms total (401) |
| `POST /api/v1/auth/login` (BCrypt cost 12 verification) | ~200–400 ms end-to-end through the proxy |

The login figure is dominated by BCrypt verification at work factor 12 — that cost is deliberate
(brute-force resistance) and is the single most expensive operation on an anonymous endpoint.

These numbers say "the platform responds in hundreds of milliseconds on a shared free instance".
They do not say anything about concurrency, and should not be quoted as p95.

---

## 2. Request path cost model

```
Client → [Vercel edge/proxy] → [Render edge] → Kestrel
   → SecurityHeadersMiddleware (OnStarting only, no allocation per request)
   → ExceptionHandlingMiddleware (try/catch frame)
   → Serilog request logging (two log events)
   → CORS
   → Rate limiter (per-IP fixed window, in-memory partition)
   → JWT authentication (signature + lifetime validation, no DB call)
   → RequestTelemetryMiddleware (one INSERT on completion)
   → Authorization (permission claims, no DB call)
   → Controller → Service → EF Core → database
```

Two properties are worth noting for a review:

1. **Authentication costs no database round trip.** Permissions are claims inside the access token, so
   every authorised request is validated cryptographically and then served. There is no per-request
   session lookup.
2. **Every request writes a telemetry row.** Read endpoints therefore perform `1 + n` database round
   trips rather than `n`. On SQLite this is a serialised writer; on SQL Server it is an insert to a
   narrow, indexed table. This is the first thing to change at genuine volume (see §4).

---

## 3. Database access

### 3.1 Query shape

- Read paths use `AsNoTracking()` and project to DTOs; entities are not tracked for reads
  (`PaymentService.ListAsync`, `ObservabilityService`, `EventStreamService`).
- List endpoints page in SQL (`Skip/Take`) with `Count` for totals — one extra aggregate query per
  page, which is the price of returning `totalCount`.
- Sorting is an explicit allow-list `switch` over column names, never string-interpolated SQL.
- Search is `Contains` over lower-cased columns. This is an unanchored `LIKE '%term%'` and **cannot use
  a B-tree index** — acceptable at seeded volume (~240 payments, ~300 events), a known limitation at
  scale (full-text index or a search service would be the fix).

### 3.2 Index inventory (as defined in `NexusBankDbContext`)

| Table | Index | Serves |
| --- | --- | --- |
| `Users` | unique `Email` | Login lookup, duplicate prevention |
| `RefreshTokens` | `TokenHash`; `(UserId, ExpiresAt)` | Refresh validation; session list per user |
| `AuditLogs` | `Timestamp`; `(ActorEmail, Timestamp)` | Audit paging and per-actor history |
| `Payments` | unique `Reference`; `CreatedAt`; `Status`; `(IdempotencyKey, CreatedAt)` | List ordering, filters, idempotency lookup |
| `PaymentEvents` | `(PaymentId, Sequence)` | Journey rendering in order |
| `LedgerEntries` | `PaymentId` | Ledger panel; balance checks |
| `IdempotencyRecords` | unique `(Scope, Key)` | Replay lookup — the hot index for retries |
| `OutboxMessages` | `(ProcessedAt, NextAttemptAt)` | Outbox claim query (runs every 5 s) |
| `ApiRequestLogs` | `Timestamp`; `(StatusCode, Timestamp)` | Traffic/latency windows, error grouping |
| `KafkaEvents` | `ProducedAt`; `Status` | Event list, dead-letter counts |
| `ConsumerGroups` | unique `(GroupId, TopicId)` | Lag display |
| `IntegrationHealthSamples` | `(IntegrationId, Timestamp)` | Integration history |
| `Integrations` | unique `Key`; `Status` | Lookup by key, status filtering |
| `Incidents` | unique `Reference`; `(Status, Severity)` | Register ordering and filters |
| `Accounts` | unique `AccountNumber` | Debtor resolution by number |
| `Customers` | unique `CustomerNumber`; `Email` | Lookup |

### 3.3 Analytics aggregation

The observability endpoints load the requested window into memory and aggregate there:

```csharp
var rows = await _db.ApiRequestLogs.AsNoTracking()
    .Where(l => l.Timestamp >= since)
    .Select(l => new { l.StatusCode, l.DurationMs, l.IsError })
    .ToListAsync(cancellationToken);
```

This is deliberate: percentile and bucketing logic that would translate differently across SQL Server
and SQLite is computed in C#, guaranteeing identical results under both providers. The cost is linear
memory in the window size. At the seeded 48-hour volume (~1.9k rows) this is trivial; at millions of
rows per day it is not, and the design would move to pre-aggregated rollups (§8).

### 3.4 Write path

A payment commits **one** `SaveChanges` covering the payment, its journey steps, ledger entries, the
outbox message, the audit entry and the idempotency record. That is intentional atomicity: it trades
a wider transaction for the guarantee that books, journey and event cannot diverge. A single payment
write therefore inserts roughly `1 + journey(4–6) + ledger(2) + outbox(1) + audit(1) + idempotency(1)`
rows — about 10–12 inserts, in one transaction.

---

## 4. Known bottlenecks

| # | Bottleneck | Impact | Mitigation in code | Next step at scale |
| --- | --- | --- | --- | --- |
| 1 | One telemetry `INSERT` per request | Write amplification on every read; SQLite writer contention | Failure is swallowed and logged; retention trims after 7 days | Batch/buffer telemetry, or move to a time-series store; drop to sampled writes for high-volume paths |
| 2 | In-memory analytics over raw rows | Linear memory and CPU per observability request; no pagination of the aggregation | Small windows, clamped to 1–168 h | Hourly rollup table written by a worker; query the rollup |
| 3 | `%contains%` search | Sequential scan per search | Allow-listed sort, bounded page size | Full-text index (SQL Server FTS) or a search service |
| 4 | BCrypt cost 12 on login | ~100–300 ms CPU per sign-in | Rate limit 12/min/IP, lockout after 5 failures | Accept the cost; scale out; consider a queue for authentication at very high volume |
| 5 | PDF-free but chatty dashboard | Dashboard issues several queries per load (summary, traffic, volume, recent payments, activity, incidents, topology) | TanStack Query caches for 15 s; polling every 25 s | Composite dashboard endpoint, or a cached read model |
| 6 | Outbox cycle is 5 s with batch 25 | Event delivery latency ~0–5 s; ceiling ~300 msg/min per instance | Attempts counter + exponential backoff; rows never lost | Increase batch, shorten interval, or run the relay as a dedicated worker |
| 7 | Rate limiting partitions on connection IP | Behind Vercel→Render the IP is a proxy address, so limits are effectively per-proxy, not per-user | Limits still bound total load | Honour `X-Forwarded-For` (see `docs/SECURITY.md` §3) with a trusted-proxy configuration |
| 8 | Single Render instance, SQLite | No horizontal scale; writes serialised; ephemeral disk | Seed is deterministic and recreated on boot | Managed Postgres/SQL Server, then multiple API replicas behind a load balancer |

---

## 5. Caching: implemented, but not yet on the read path

This is the most commonly misunderstood part of the build, so it is stated precisely.

**Implemented and available in the project:**

- `ICacheService` abstraction with `GetAsync`, `SetAsync`, `RemoveAsync`, `GetOrCreateAsync`.
- `NexusCacheService`: Redis via `IDistributedCache` when reachable, otherwise a process-local
  `IMemoryCache`, with automatic demotion on the first failure (`MarkUnhealthy`).
- `Redis__Enabled` / `Redis__Configuration` configuration, Docker Compose Redis 7 service with
  `appendonly` and `allkeys-lru` at 256 MB, and a health probe that reports which mode is serving.

**Actual usage in the current codebase:** `ICacheService` is consumed by `ComponentHealthProbe`
(set/get of `health:probe`) and read for mode reporting by `ObservabilityService`. **No business read
path is cached.** Therefore Redis currently provides reachability and interface readiness, not a
latency improvement — enabling Redis in the hosted deployment would change `/health` from `degraded`
to `healthy` without making any query faster.

**Where caching would pay off, if wired:**

| Candidate | Key | TTL | Reason |
| --- | --- | --- | --- |
| Dashboard summary | `dashboard:{userId scope}` | 10–30 s | Expensive multi-query read, polled every 25 s by every open console |
| Observability overview | `obs:overview:{hours}` | 30–60 s | Full-window scan; identical for all viewers of the same window |
| Integration topology | `integrations:topology` | 60 s | Changes only on configuration writes |
| Permission catalogue / roles | `security:roles` | 5 min | Read-heavy, write-rare |

Invalidation would be write-through on the owning service (`IntegrationsService` update/delete,
`SecurityService` role update) rather than TTL-only, because a stale permission set is a security
defect, not a cosmetic one.

---

## 6. Messaging throughput

| Property | Value | Source |
| --- | --- | --- |
| Outbox poll interval | 5 s | `OutboxPublisherWorker.Interval` |
| Outbox batch size | 25 messages per cycle | `OutboxPublisherWorker.BatchSize` |
| Backoff on failure | `min(300 s, 2^attempts s)` | `OutboxPublisherWorker` |
| Producer guarantees | `Acks.All`, `EnableIdempotence = true`, `MessageTimeoutMs = 5000` | `KafkaEventBus` |
| Consumer poll | 750 ms `Consume` timeout, `AutoOffsetReset.Latest`, auto-commit | `KafkaEventConsumerWorker` |
| Kafka partitions (Compose) | 3, auto-topic-creation on | `docker-compose.yml` |
| Event persistence | Every publish also writes a `KafkaEvents` row, so history survives a broker outage | `OutboxPublisherWorker`, `EventStreamService` |

The outbox relay is the throughput ceiling: 25 messages per 5 seconds ≈ 5 msg/s per instance. That
is far above the seeded workload (single-digit events per minute) and far below a real payment
platform. The design is correct; the constants are demo-sized and are the first tuning knobs.

---

## 7. Frontend rendering

Implemented:

- **TanStack Query** with `staleTime: 15s`, `gcTime: 5min`, `retry: 1`, `refetchOnWindowFocus: false`
  — tab switching does not stampede the API, and a page revisit inside 15 s is served from cache.
- **`placeholderData`** on paged queries so changing page or filter keeps the previous rows visible
  instead of flashing an empty table.
- **Single-flight token refresh** — concurrent 401s share one refresh call rather than issuing N.
- **Vite production build** with hashed asset filenames; nginx serves `assets/` as
  `public, immutable` for one year and `index.html` as `no-store`.
- **Token-driven theming** via CSS variables, so theme switching does not re-render the tree.
- **Fonts self-hosted** through `@fontsource-variable`, so there is no third-party font request on the
  critical path.

Not implemented (honest gaps):

| Gap | Consequence |
| --- | --- |
| No route-level code splitting (`React.lazy`) | All 11 feature screens are in one bundle; the build warns above 900 kB and the limit has been raised to 900 to silence it rather than split routes |
| No virtualised tables | Lists are paged by the API (max 100), so DOM size is bounded by page size, not by total rows |
| No service worker / offline support | Every cold load hits the network |
| No image optimisation pipeline | There are no raster images; branding is inline SVG |
| No real-user monitoring | No field data on Core Web Vitals is collected |

The dashboard polls every 25 s while open. With N open consoles that is N× the dashboard query set per
25 s against a single instance — the reason a cached dashboard read (§5) is the highest-value
performance change in the project.

---

## 8. Concurrency and scaling

- **Stateless API.** No process-local state is required to serve a request except: the rate-limiter
  partitions, the worker heartbeat registry, and the `_redisHealthy`/`_connected` probe caches. All are
  per-process caches; none is authoritative. Multiple replicas are therefore safe for request serving.
- **The outbox worker is not leader-elected.** Running two instances would claim and publish the same
  pending rows concurrently (at-least-once delivery means consumers must tolerate duplicates, but it
  also means duplicate broker writes). Horizontal scaling currently requires either a single worker
  replica or a claim/lease column. This is documented rather than hidden.
- **SQLite cannot serve concurrent writers.** In the hosted deployment the database is a file; write
  contention appears quickly under any real concurrency. SQL Server (Compose/Azure) or another
  server-grade provider is required for genuine multi-user load.
- **Database connection pool** uses the provider default (100 connections per process for SQL Server).
  No pool size is configured explicitly.
- **Kestrel** runs with framework defaults; request body limits are the ASP.NET Core defaults plus the
  application's own validation length limits.

---

## 9. How to benchmark (reproducible, not yet run)

Use [k6](https://grafana.com/docs/k6/latest/) against a **local or dedicated** environment. Do not
point load tests at the public Render free instance: it will cold-start, throttle, and the numbers will
describe Render, not NexusBank.

### 9.1 Prerequisites

```bash
# Full-fidelity environment (SQL Server, Redis, Kafka)
cp .env.example .env      # set MSSQL_SA_PASSWORD and JWT_SIGNING_KEY
docker compose up --build -d
curl -s http://localhost:8080/health   # expect healthy with real Redis + Kafka
```

### 9.2 Get a token once, reuse it

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"ops@nexusbank.dev","password":"NexusBank!2026"}' | jq -r .accessToken)
```

Access tokens live 30 minutes. Keep each scenario under that, or refresh in `setup()`. Do **not** log
in inside the hot path: BCrypt at cost 12 would dominate every scenario and you would be benchmarking
password hashing.

### 9.3 Scenarios

| Scenario | Endpoint | Model | What it answers |
| --- | --- | --- | --- |
| Read-heavy browse | `GET /api/v1/dashboard`, `/integrations`, `/payments?page=1` | Constant 20 VUs, 5 min | Does the console feel responsive at realistic operator concurrency? |
| Paged list with search | `GET /api/v1/payments?search=a&page=2` | Ramping 5→50 VUs | Cost of the unanchored `LIKE` |
| Observability aggregate | `GET /api/v1/observability/overview?hours=24` | Constant 5 VUs | In-memory aggregation cost |
| Payment write | `POST /api/v1/payments` with a unique `Idempotency-Key` per iteration | Ramping 1→20 VUs | Transactional write path and ledger contention |
| Idempotent replay | `POST /api/v1/payments` with the **same** key | Constant 10 VUs | Unique-index lookup vs write |
| Auth burst | `POST /api/v1/auth/login` correct credentials | 12 VUs, 30 s | Proves the 12/min limiter returns `429`, not 5xx |

### 9.4 Example script

```javascript
// k6/read-path.js — run: k6 run k6/read-path.js
import http from 'k6/http'
import { check, sleep } from 'k6'
import { Trend, Rate } from 'k6/metrics'

const BASE = __ENV.BASE_URL || 'http://localhost:8080'
const TOKEN = __ENV.TOKEN
const dashboard = new Trend('dashboard_ms')
const errors = new Rate('business_errors')

export const options = {
  scenarios: {
    browse: { executor: 'constant-vus', vus: 20, duration: '5m' },
  },
  thresholds: {
    // Targets, not measured results. Fail the run if they are missed.
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500'],
  },
}

const headers = { Authorization: `Bearer ${TOKEN}`, Accept: 'application/json' }

export default function () {
  const res = http.get(`${BASE}/api/v1/dashboard`, { headers })
  dashboard.add(res.timings.duration)
  errors.add(res.status >= 500)
  check(res, { 'status 200': (r) => r.status === 200 })
  sleep(1)
}
```

Run it with the token in the environment:

```bash
TOKEN="$TOKEN" k6 run k6/read-path.js
```

### 9.5 Recorded results

| Date | Environment | Scenario | VUs | Throughput | p50 | p95 | p99 | Errors |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | *No benchmark has been executed* | — | — | — | — | — | — |

Add real rows here when a run is performed, including the commit SHA, machine specification, database
provider and whether Redis/Kafka were enabled. A result without those four facts is not reproducible
and should not be published.

### 9.6 Metrics to capture per run

- Latency: `p50`, `p90`, `p95`, `p99`, max (k6 `http_req_duration`).
- Throughput: requests/second sustained, and the point at which p95 degrades.
- Error rate: `http_req_failed` plus application-level 5xx from `ApiRequestLogs`.
- Server-side truth: `/metrics` before and after (`requests_last_hour`, `errors_last_hour`,
  `latency_avg_ms`), and `/api/v1/system/health` component detail.
- Resource utilisation: `docker stats` (CPU, memory) per container; SQL Server DTU/vCore if managed.
- Growth: `ApiRequestLogs` row count before/after, to quantify write amplification.

---

## 10. Performance targets (design goals)

These are objectives, not results. They belong in a review as "what we would hold ourselves to".

| Objective | Target | Current evidence |
| --- | --- | --- |
| p95 read endpoint latency (local, 20 VUs) | < 500 ms | Not measured |
| p95 payment create (local, 10 VUs) | < 800 ms | Not measured |
| 5xx rate under normal load | < 1% | Not measured |
| Dashboard time-to-interactive (cold, cable) | < 2.5 s | Not measured; single bundle is a known cost |
| Outbox event delivery latency | < 10 s | Bounded by 5 s poll by design |
| Sustained event throughput per instance | ≥ 5 msg/s | Bounded by batch 25 / 5 s by design |

---

## 11. Prioritised optimisation backlog

1. **Cache the dashboard and observability overview** using the existing `ICacheService`, with
   write-through invalidation on payment/incident/integration writes. Highest value for the current
   polling behaviour.
2. **Batch telemetry writes** (buffer + flush) or move `ApiRequestLogs` to a store designed for it.
3. **Roll up observability hourly** instead of scanning raw rows per request.
4. **Split the frontend bundle per route** and remove the raised chunk-size warning.
5. **Map per-integration retry/circuit-breaker settings into the resilience handler** — today the
   pipeline uses fixed standard values while the stored values are decorative (see
   `docs/ARCHITECTURE.md` §5).
6. **Move off SQLite for any shared deployment.** File-based SQLite is for local development only.

Each item is a proposal; none is implemented.
