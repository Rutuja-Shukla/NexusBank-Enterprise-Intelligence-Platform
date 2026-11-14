# Architecture decision records

Short records of the decisions that shape the codebase, including the options rejected and the
consequences accepted. Each is written to be defensible in a design review.

---

## ADR-001 — Modular monolith instead of microservices

**Context.** The brief describes an integration platform with eight modules. Microservices would
demonstrate distribution, but a portfolio platform with no real traffic would pay the operational
cost (service discovery, distributed tracing infrastructure, eventual consistency, N× pipelines) for
no benefit.

**Decision.** One ASP.NET Core host, internally split into Domain / Application / Infrastructure /
IntegrationServices / Workers / Shared, with explicit dependency rules. Boundaries are real
(projects, interfaces, no circular references) and extraction points are identified.

**Consequences.** Simple local start (`dotnet run`), one migration, one deployment. Cross-module
calls are in-process, so there is no distributed transaction to reason about. Scaling is
horizontal. If a module ever needs independent scaling, payments or telemetry are the first seams —
they already have their own workers and data access patterns.

---

## ADR-002 — One `IApplicationDbContext` abstraction, no repositories

**Context.** Clean Architecture examples often add `IRepository<T>` per aggregate. EF Core's
`DbContext` is already a unit of work with composable queries.

**Decision.** Application services depend on a single `IApplicationDbContext` exposing `DbSet`s and
`SaveChangesAsync`. No generic repository, no specification pattern.

**Consequences.** Queries stay readable and provider-agnostic enough for SQL Server and SQLite.
Services can still be unit tested (SQLite in-memory) without mocking an ORM. The trade-off is that
Application references EF Core types — accepted deliberately; the alternative hides the query shape
and encourages N+1s.

---

## ADR-003 — SQL Server as provider of record, SQLite for development and tests

**Context.** The brief names SQL Server. Requiring a container or a server install to run the demo
would make the repository harder to evaluate, and the authoring environment had no Docker engine.

**Decision.** Model and migrations target SQL Server. `Database:Provider=Sqlite` switches the
provider for local development and tests, with `EnsureCreated` and a conditional
`DateTimeOffsetToBinaryConverter`.

**Consequences.** One model, two providers; the integration suites run in seconds without
containers. SQLite lacks native `DateTimeOffset`, so the converter is applied only for SQLite (its
absence caused an initial bug — now guarded by `SqliteDateRoundTripTests`). Provider-specific SQL
must be avoided; analytics cast decimals to `double` and bucket time in memory where translation
would differ.

---

## ADR-004 — Transactional outbox for event publishing

**Context.** Publishing to Kafka inside the payment transaction would either lose events (publish
after commit fails) or produce events for rolled-back payments (publish before commit).

**Decision.** Business writes and an `OutboxMessages` row are committed in the same `SaveChanges`.
`OutboxPublisherWorker` claims pending rows, publishes, marks them processed, and backs off with
`Attempts`/`NextAttemptAt` on failure.

**Consequences.** At-least-once delivery with idempotent consumers assumed. Extra table and worker.
Events can lag during a broker outage — visible in the event stream, which is better than silently
dropping them.

---

## ADR-005 — Idempotency keys with stored responses

**Context.** Payment creation is the one operation where a retry must never create a second
instruction (network timeout, user double-click, client retry policy).

**Decision.** `Idempotency-Key` header, scoped to `payments`, with a request hash and the serialised
response stored for 24 hours. Same key + same hash → replay with `replayed: true`; same key +
different hash → `409`.

**Consequences.** Safe retries and a clear client contract. The client must send the header; calls
without it are not idempotent. Storage grows with unique keys, hence the expiry.

---

## ADR-006 — JWT access tokens plus rotating hashed refresh tokens

**Context.** The console needs a stateless API credential and a session that survives a page reload
without storing a password.

**Decision.** HS256 JWT (30 min) with `iss`/`aud`/lifetime validation; refresh token random 256-bit,
stored as SHA-256 hash with IP/user-agent, rotated on every refresh, revocable per session.

**Consequences.** No server-side session lookup per request. Refresh reuse is detectable (the old
hash is revoked). Tokens live in `localStorage`, which is XSS-readable — the documented trade-off and
the top item on the hardening list (`docs/SECURITY.md` §8). At bank scale: RS256 + JWKS, short
access tokens, HttpOnly cookie for refresh, proof-of-possession.

---

## ADR-007 — Dynamic permission policies, roles as compositions

**Context.** Role checks scattered through controllers are the usual source of privilege bugs.

**Decision.** Every action carries `[Authorize(Policy = NexusPermissions.X)]`. A custom policy
provider maps any policy name to a permission requirement; the handler checks the caller's permission
claims (role grants + direct grants). Roles exist only to bundle permissions.

**Consequences.** Adding a permission needs no new policy class. The same codes drive API enforcement
and console navigation. Tests assert denials (auditor cannot write payments, operations cannot read
the security directory). Misconfigured role grants remain a governance risk, mitigated by the audit
trail and the role screen.

---

## ADR-008 — Simulated integration fixtures, labelled everywhere

**Context.** The platform needs realistic integration telemetry without contacting real banking
systems or inventing a healthy upstream.

**Decision.** Seeded integrations use `mock://` URLs served by a deterministic simulator derived from
the integration key. Every sample records `TelemetrySource = "simulated fixture"`, and the UI labels
such nodes **FIXTURE**. Real `https://` integrations are probed over the network with timeout, retry,
backoff and circuit breaker.

**Consequences.** Demos are reproducible and offline-friendly without lying: the label is visible on
the node, the detail panel and the API payload. A reviewer can distinguish fixture telemetry from a
live probe at a glance.

---

## ADR-009 — Degrade visibly instead of failing or faking

**Context.** Redis and Kafka are external dependencies that may be absent in development, CI and
review environments.

**Decision.** Both are optional. When unavailable, the cache falls back to memory and the event bus
to in-process delivery; the mode string is returned by `/health`, `/api/v1/system/health`,
`/api/v1/events/status` and the event objects (`isSimulated`). Overall health is `degraded`, never
`healthy`. The database is the only dependency whose loss makes the platform `unhealthy`.

**Consequences.** The app runs anywhere, and its status is truthful. Reviewers can see exactly which
capabilities are real in a given environment. The risk is normalising degraded operation — mitigated
by alerts on the command centre and the health page.

---

## ADR-010 — Request telemetry as the single source for observability

**Context.** Charts that are fed by fixtures while the backend runs are the classic "fake dashboard"
failure mode.

**Decision.** A middleware writes one `ApiRequestLog` per request (method, path, status, duration,
correlation id, service). Dashboard, traffic, latency distribution, error groups, traces, alerts and
the Prometheus exposition are all aggregations of those rows plus live probes.

**Consequences.** Every number is explainable and reproducible; the dashboard is empty on a fresh
database until traffic exists, which is honest. Retention is trimmed by a worker. At high volume the
table needs partitioning or a move to a time-series store (documented).

---

## ADR-011 — Tailwind with CSS-variable tokens, shadcn-style primitives

**Context.** The product needs a distinctive yellow/charcoal identity with full light and dark
themes, without pulling in a component framework that dictates the look.

**Decision.** CSS variables for colour, radius, typography and elevation; Tailwind maps them to
utilities; Radix primitives back dialog/select/tabs/switch with local styling in `components/ui`.
Recharts is styled through the same tokens.

**Consequences.** Theme switching is a class on `<html>` with no re-render of components, and chart
colours stay in sync. The cost is that the design system is hand-maintained rather than generated by
a CLI; components are few and shared, so the surface is manageable.

---

## ADR-012 — Vitest + React Testing Library; Playwright kept optional

**Context.** Component and unit tests must run in CI in seconds; browser tests need two live servers
and are slower and flakier.

**Decision.** Vitest with jsdom for unit and component tests; Playwright configured as an optional
end-to-end layer. No coverage threshold is enforced or quoted, because no coverage run has been
published.

**Consequences.** Fast feedback on formatting, API client behaviour and key presentation logic.
Browser-level journeys remain a manual/optional step and are documented as not executed in this
environment.

---

## ADR-013 — Docker assets provided but not run-verified here

**Context.** The Docker engine was not running in the authoring environment. Shipping untested
Dockerfiles silently would be worse than saying so.

**Decision.** Multi-stage Dockerfiles, nginx configuration and a Compose stack are included and
reviewed; README, `docs/DEPLOYMENT.md` and `docs/TESTING.md` state they were not executed, and the
verification that *was* performed (local SDK + Vite, live API calls, all test suites) is listed
explicitly.

**Consequences.** A reviewer knows exactly which claims are verified. The first `docker compose up`
should be treated as an unverified step; `docs/DEPLOYMENT.md` names the likely failure points
(SQL Server password policy, missing JWT key, Kafka startup time).

---

## ADR-014 — `fetch` wrapper instead of an HTTP library

**Context.** Axios adds a dependency for behaviour that is mostly a thin wrapper: base URL, JSON,
auth header, one refresh retry.

**Decision.** A ~100-line typed wrapper with single-flight refresh, `ApiError` carrying status/code/
field errors, and `describeError` for consistent toast copy.

**Consequences.** No extra dependency, full control over refresh semantics, and one place to add
telemetry. Interceptors and upload progress would have to be written by hand if ever needed.
