# Architecture decision records

Short records of the decisions that shape the codebase — including the options rejected and the
consequences accepted. Each is written to be defensible in a design review.

**Status key:** `Accepted` — in force in the code. `Proposed` — recommended, not implemented.
`Superseded` — replaced by a later record.

| ADR | Title | Status |
| --- | --- | --- |
| 001 | Modular monolith instead of microservices | Accepted |
| 002 | One `IApplicationDbContext`, no repositories | Accepted |
| 003 | SQL Server as provider of record, SQLite for development and tests | Accepted |
| 004 | Transactional outbox for event publishing | Accepted |
| 005 | Idempotency keys with stored responses | Accepted |
| 006 | JWT access tokens plus rotating hashed refresh tokens | Accepted |
| 007 | Dynamic permission policies, roles as compositions | Accepted |
| 008 | Simulated integration fixtures, labelled everywhere | Accepted |
| 009 | Degrade visibly instead of failing or faking | Accepted |
| 010 | Request telemetry as the single source for observability | Accepted |
| 011 | Tailwind with CSS-variable tokens, Radix primitives | Accepted |
| 012 | Vitest + React Testing Library; Playwright optional | Accepted |
| 013 | Docker assets provided but not run-verified at authoring time | Accepted (partially resolved — see ADR-015) |
| 014 | `fetch` wrapper instead of an HTTP library | Accepted |
| 015 | Hosted demonstration on Vercel + Render with Redis and Kafka disabled | Accepted |
| 016 | Same-origin `/api` proxy and SPA fallback as deployment routing | Accepted |
| 017 | Per-integration resilience settings are stored, not yet applied | **Proposed (gap)** |
| 018 | Maker–checker for high-value payments and reversals | **Proposed** |
| 019 | Conditional update / optimistic concurrency on account balances | **Proposed** |
| 020 | Cache the dashboard and observability read models in Redis | **Proposed** |
| 021 | Security gates in CI (advisories, Dependabot, image scanning) | **Proposed** |

---

## ADR-001 — Modular monolith instead of microservices

**Context.** The brief describes an integration platform with eight modules. Microservices would
demonstrate distribution, but a portfolio platform with no real traffic would pay the operational cost
(service discovery, distributed tracing infrastructure, eventual consistency, N× pipelines) for no
benefit.

**Problem.** How to keep module boundaries credible without buying distributed-systems complexity that
does not pay for itself at this scale.

**Options considered.**

1. Microservices per module — rejected: no traffic to justify it, and every cross-module call becomes a
   network failure mode.
2. Single project with folders as boundaries — rejected: nothing enforces the boundary, and the
   dependency graph rots silently.
3. **Modular monolith with enforced project-level dependencies** — chosen.

**Decision.** One ASP.NET Core host, internally split into Domain / Application / Infrastructure /
IntegrationServices / Workers / Shared, with explicit dependency rules (see `docs/ARCHITECTURE.md` §2).
Boundaries are real (projects, interfaces, no circular references) and extraction points are identified.

**Rationale.** A boundary that the compiler can enforce is worth more than one that only a convention
document describes. Cross-module calls stay in-process, so there is no distributed transaction to reason
about, one migration, one deployment and one log stream.

**Consequences.** Simple local start (`dotnet run`), one deployable, horizontal scaling for request
serving. If a module ever needs independent scaling, event relay, payments and telemetry are the first
seams — they already have their own workers and data-access patterns.

**Trade-offs.** One process means one failure domain and one deploy unit: a memory leak in any module
affects all. Independent team ownership is harder than with services. Those costs are acceptable here and
would be revisited at organisational scale, not technical scale.

---

## ADR-002 — One `IApplicationDbContext` abstraction, no repositories

**Context.** Clean Architecture examples often add `IRepository<T>` per aggregate.

**Problem.** How to keep application services testable and provider-agnostic without hiding query shape.

**Options considered.**

1. Generic repository + specification pattern — rejected: hides `Include`/projection and invites N+1s.
2. DbContext injected directly into services — rejected: couples Application to a concrete
   infrastructure type and makes provider substitution harder.
3. **Single `IApplicationDbContext` exposing `DbSet`s and `SaveChangesAsync`** — chosen.

**Decision.** Application services depend on one abstraction. No generic repository, no specification
pattern.

**Rationale.** EF Core is already a unit of work with composable queries; wrapping it again adds
indirection without safety. Services remain unit-testable against SQLite in memory without mocking an ORM.

**Consequences.** Queries stay readable and portable enough for SQL Server and SQLite. Application
references EF Core types — accepted deliberately.

**Trade-offs.** Application is not perfectly persistence-ignorant. Swapping to Dapper or a document store
would touch every service. The bet is that query clarity beats theoretical portability.

---

## ADR-003 — SQL Server as provider of record, SQLite for development and tests

**Context.** The brief names SQL Server. Requiring a container or a server install to run the demo would
make the repository harder to evaluate, and the authoring environment had no Docker engine.

**Problem.** Demonstrate a real relational schema without making first-run setup a barrier.

**Options considered.**

1. SQL Server only — rejected: first run needs a container or an install.
2. SQLite only — rejected: the brief names SQL Server and the migration must exist for a real target.
3. **SQL Server migrations + a provider switch for SQLite** — chosen.

**Decision.** Model and migrations target SQL Server. `Database:Provider=Sqlite` switches the provider for
local development and tests, with `EnsureCreated` and a conditional
`DateTimeOffsetToBinaryConverter`.

**Rationale.** One model, two providers; integration suites run in seconds without containers. The
provider switch is a first-class configuration concern, not a hack.

**Consequences.** SQLite lacks native `DateTimeOffset`, so the converter is applied only for SQLite (its
absence caused an initial bug — now guarded by `SqliteDateRoundTripTests`). Provider-specific SQL must be
avoided; analytics that would translate differently are computed in memory.

**Trade-offs.** Two providers mean two failure modes and the temptation to use SQLite-only behaviour.
SQLite is a single-writer file database: fine for a demo, unsuitable for a shared deployment (see
ADR-015 and proposed ADR-019).

---

## ADR-004 — Transactional outbox for event publishing

**Context.** Publishing to Kafka inside the payment transaction would either lose events (publish after
commit fails) or produce events for rolled-back payments (publish before commit).

**Problem.** Keep "the business write committed" and "the event exists" consistent without a distributed
transaction.

**Options considered.**

1. Publish inline inside the transaction — rejected: a broker failure would roll back a valid payment, or
   a commit failure would emit an event for money that never moved.
2. Publish after commit, fire and forget — rejected: silent event loss.
3. Two-phase commit / XA — rejected: not supported across Kafka and the database, and operationally
   fragile.
4. **Transactional outbox with a relay worker** — chosen.

**Decision.** Business writes and an `OutboxMessages` row are committed in the same `SaveChanges`.
`OutboxPublisherWorker` claims pending rows every 5 s (batch 25), publishes, marks them processed, and backs
off with `Attempts`/`NextAttemptAt` on failure.

**Rationale.** The event becomes part of the transaction; delivery becomes a retried side effect. Failure
is visible (pending rows, attempt counts) instead of silent.

**Consequences.** At-least-once delivery with idempotent consumers assumed. Extra table and worker. Events
can lag during a broker outage — visible in the event stream, which is better than dropping them.

**Trade-offs.** The relay is a throughput ceiling (~5 msg/s per instance with current constants) and has
no leader election, so it does not scale out without a claim/lease. Duplicates are possible by design.

---

## ADR-005 — Idempotency keys with stored responses

**Context.** Payment creation is the one operation where a retry must never create a second instruction
(network timeout, double click, client retry policy).

**Problem.** Make client retries safe without asking the client for a full two-phase protocol.

**Options considered.**

1. Natural-key deduplication (amount + beneficiary + minute) — rejected: false positives on legitimate
   repeats.
2. Client-supplied request id checked per request — rejected: still needs stored responses to return the
   original result.
3. **`Idempotency-Key` header, request hash, stored response, 24 h expiry** — chosen.

**Decision.** Key scoped to `payments`, canonical request hash, serialised response stored for 24 hours.
Same key + same hash → replay with `replayed: true`; same key + different hash → `409
idempotency_key_reuse`.

**Rationale.** It is the industry pattern for payment APIs, it is cheap to implement on top of the
existing transaction, and the conflict semantics are explicit rather than silent.

**Consequences.** Safe retries and a clear client contract. Storage grows with unique keys, hence expiry
(cleaned by the retention worker). The client must send the header; calls without it are not idempotent.

**Trade-offs.** The read-then-insert on `IdempotencyRecords` is not atomic under concurrency: racing
requests can both write and one hits the unique index. That surfaces as a 500 today and is tracked in
`docs/TESTING.md` §6.2.

---

## ADR-006 — JWT access tokens plus rotating hashed refresh tokens

**Context.** The console needs a stateless API credential and a session that survives a reload without
storing a password.

**Problem.** Authorise requests without a session lookup per call, while keeping revocation possible.

**Options considered.**

1. Server-side sessions with a cookie — rejected: adds a shared session store requirement and CORS/cookie
   complexity for a separately hosted console.
2. Long-lived JWT only — rejected: cannot revoke, and a stolen token is valid for its whole lifetime.
3. **Short-lived JWT + rotating refresh token stored as a hash** — chosen.

**Decision.** HS256 JWT (30 min) with `iss`/`aud`/lifetime validation; refresh token random 48 bytes,
stored as SHA-256 hex with IP and user agent, rotated on every refresh, revocable per session.

**Rationale.** No server-side lookup per request. Rotation makes replay detectable (the presented hash is
revoked). Hashing at rest means a database read does not yield usable tokens.

**Consequences.** Tokens live in `localStorage`, which is XSS-readable — the documented trade-off and the
top item on the hardening list (`docs/SECURITY.md` §8). Access tokens omit `nbf` deliberately: a
not-before claim tied validity to the issuing host's clock and produced spurious `IDX10222` rejections.

**Trade-offs.** A revoked session's access token remains valid until expiry (≤30 min). Symmetric signing
means every validator holds the signing key. At bank scale: RS256 + JWKS, 5–10 minute access tokens,
HttpOnly refresh cookie, proof-of-possession.

---

## ADR-007 — Dynamic permission policies, roles as compositions

**Context.** Role checks scattered through controllers are the usual source of privilege bugs.

**Problem.** Express authorisation once, consistently, and make it testable.

**Options considered.**

1. `[Authorize(Roles = "Administrator,Operations")]` — rejected: role lists are brittle and leak policy
   into controllers.
2. Hand-written policies registered at startup — rejected: every new permission needs plumbing.
3. **Custom `IAuthorizationPolicyProvider` mapping any permission code to a requirement** — chosen.

**Decision.** Every action carries `[Authorize(Policy = NexusPermissions.X)]`. The handler checks the
caller's `permission` claims (role grants + direct grants). Roles exist only to bundle permissions.

**Rationale.** Adding a permission needs no new policy class. The same codes drive API enforcement and
console navigation, so the two cannot drift.

**Consequences.** Tests assert denials (auditor cannot write payments; operations cannot read the security
directory). Misconfigured role grants remain a governance risk, mitigated by the audit trail and the role
screen.

**Trade-offs.** The handler grants access when the role claim is `administrator`, regardless of individual
permission claims — a deliberate shortcut that makes the administrator role a privileged entitlement. No
data scoping: all operators with a permission see all records.

---

## ADR-008 — Simulated integration fixtures, labelled everywhere

**Context.** The platform needs realistic integration telemetry without contacting real banking systems
or inventing a healthy upstream.

**Problem.** Make the demo meaningful without lying about where the numbers come from.

**Options considered.**

1. Random noise per request — rejected: not reproducible, destroys demos.
2. A separate mock server — rejected: extra runtime dependency for a portfolio build.
3. **Deterministic simulator for `mock://` URLs, flagged in the payload and the UI** — chosen.

**Decision.** Seeded integrations use `mock://` URLs served by a deterministic simulator derived from the
integration key. Every sample records `TelemetrySource = "simulated fixture"`, and the UI labels such nodes
**FIXTURE**. Real `https://` integrations are probed over the network with timeout, retry, backoff and
circuit breaker.

**Rationale.** Demos are reproducible and offline-friendly, and the label is visible on the node, in the
detail panel and in the API payload.

**Consequences.** A reviewer can distinguish fixture telemetry from a live probe at a glance. Seeded
estates look "healthy" or "degraded" by construction, not by measurement.

**Trade-offs.** The simulator's failure distribution is invented data (it is labelled as such). Fixture
integrations cannot detect a real outage because there is no real dependency.

---

## ADR-009 — Degrade visibly instead of failing or faking

**Context.** Redis and Kafka are external dependencies that may be absent in development, CI, review
environments and the hosted deployment.

**Problem.** Decide the behaviour when an optional dependency is missing.

**Options considered.**

1. Fail fast — rejected: makes the platform unusable without infrastructure it does not need to
   demonstrate its core behaviour.
2. Pretend everything is healthy — rejected: destroys the credibility of every other status on screen.
3. **Fall back and label the fallback everywhere** — chosen.

**Decision.** Both are optional. When unavailable, the cache falls back to memory and the event bus to
in-process delivery; the mode string is returned by `/health`, `/api/v1/system/health`,
`/api/v1/events/status` and the event objects (`isSimulated`). Overall health is `degraded`, never
`healthy`. The database is the only dependency whose loss makes the platform `unhealthy`.

**Rationale.** The app runs anywhere and its status is truthful. Reviewers can see exactly which
capabilities are real in a given environment.

**Consequences.** The hosted deployment reports `degraded` permanently and by design. The risk is
normalising degraded operation — mitigated by alerts on the command centre and the health page.

**Trade-offs.** "Degraded" no longer distinguishes "cheap deployment" from "something broke", which is
exactly why the mode strings exist and why `docs/OPERATIONS_RUNBOOK.md` §4–§5 say when a degraded
dependency is expected rather than an incident.

---

## ADR-010 — Request telemetry as the single source for observability

**Context.** Charts fed by fixtures while the backend runs are the classic "fake dashboard" failure mode.

**Problem.** Make every number on screen explainable from a record that exists.

**Options considered.**

1. Seeded dashboard metrics — rejected: indistinguishable from a canned demo.
2. A metrics backend (Prometheus + Grafana) as a runtime dependency — rejected: heavy for the deployment
   target, though `/metrics` is still exposed for one.
3. **One `ApiRequestLog` row per request; all views aggregate those rows** — chosen.

**Decision.** A middleware writes one row per request (method, path, status, duration, correlation id,
user, service). Dashboard, traffic, latency distribution, error groups, traces, alerts and the Prometheus
exposition are aggregations of those rows plus live probes.

**Rationale.** Every number is explainable and reproducible; the dashboard is empty on a fresh database
until traffic exists, which is honest.

**Consequences.** One extra insert per request. Retention is trimmed by a worker after 7 days. At high
volume the table needs partitioning or a move to a time-series store.

**Trade-offs.** Write amplification on every read endpoint, and analytics that load raw rows into memory
(`docs/PERFORMANCE.md` §3.3, §4). This is the first thing to change at real volume.

---

## ADR-011 — Tailwind with CSS-variable tokens, Radix primitives

**Context.** The product needs a distinctive yellow/charcoal identity with full light and dark themes
without pulling in a component framework that dictates the look.

**Problem.** Theme-able, consistent UI with a small dependency surface.

**Options considered.**

1. A component framework (MUI, Ant) — rejected: the visual identity would fight the library.
2. Plain CSS per component — rejected: no consistency, no tokens, theme switching becomes a rewrite.
3. **CSS variables + Tailwind mapping + Radix primitives styled locally** — chosen.

**Decision.** CSS variables for colour, radius, typography and elevation; Tailwind maps them to utilities;
Radix backs dialog/select/tabs/switch with local styling in `components/ui`. Recharts is styled through the
same tokens.

**Rationale.** Theme switching is a class on `<html>` with no re-render, and chart colours stay in sync
because they read the same variables.

**Consequences.** The design system is hand-maintained rather than generated by a CLI; components are few
and shared, so the surface is manageable.

**Trade-offs.** No library upgrades deliver accessibility fixes for free; Radix covers the hard
interaction primitives, and the rest is small enough to audit.

---

## ADR-012 — Vitest + React Testing Library; Playwright optional

**Context.** Component and unit tests must run in CI in seconds; browser tests need two live servers and
are slower and flakier.

**Problem.** Choose a frontend testing layer that gives real feedback without dominating CI time.

**Options considered.**

1. Jest + RTL — rejected: heavier config for a Vite project.
2. Playwright only — rejected: too slow for the feedback loop, needs the API running.
3. **Vitest + RTL in CI, Playwright as an optional layer** — chosen.

**Decision.** Vitest with jsdom for unit and component tests; Playwright configured as an optional
end-to-end layer. No coverage threshold is enforced or quoted.

**Rationale.** Fast feedback on formatting, API client behaviour and key presentation logic, which is
where the console's logic actually lives.

**Consequences.** Browser-level journeys remain a manual/optional step and are documented as not executed
(`docs/TESTING.md` §13).

**Trade-offs.** No protection against integration-level UI regressions (router guards, real auth flow).
The specs exist but do not run in CI; that is a known gap, not a claim.

---

## ADR-013 — Docker assets provided but not run-verified at authoring time

**Context.** The Docker engine was not running in the authoring environment. Shipping untested Dockerfiles
silently would be worse than saying so.

**Problem.** Decide how to handle unverifiable infrastructure deliverables.

**Options considered.**

1. Omit container assets — rejected: they are part of the delivery story and the Compose stack is the only
   way to run the full infrastructure locally.
2. Ship and stay silent — rejected: dishonest.
3. **Ship, review, and document exactly what was not executed** — chosen.

**Decision.** Multi-stage Dockerfiles, nginx configuration and a Compose stack are included and reviewed;
README, `docs/DEPLOYMENT.md` and `docs/TESTING.md` state they were not executed, and the verification that
*was* performed (local SDK + Vite, live API calls, all test suites) is listed explicitly.

**Rationale.** A reviewer knows exactly which claims are verified. The first `docker compose up` should be
treated as an unverified step; `docs/DEPLOYMENT.md` names the likely failure points (SQL Server password
policy, missing JWT key, Kafka startup time).

**Consequences.** Partially resolved since: the Docker CLI is available in this workspace,
`docker compose config` validates with the required variables set, and CI builds both images. The stack
has still not been started end-to-end and that remains documented.

**Trade-offs.** The Compose path is a "reviewed, not rehearsed" deliverable. Anyone using it should expect
to debug a first-run issue.

---

## ADR-014 — `fetch` wrapper instead of an HTTP library

**Context.** Axios adds a dependency for behaviour that is mostly a thin wrapper: base URL, JSON, auth
header, one refresh retry.

**Problem.** A predictable API client with explicit refresh semantics and no extra dependency.

**Options considered.**

1. Axios — rejected: interceptors would be used for exactly the four behaviours a small wrapper already
   handles, plus another dependency to audit.
2. Raw `fetch` at every call site — rejected: duplicated auth/error handling and inconsistent refresh.
3. **A ~100-line typed wrapper** — chosen.

**Decision.** One module with `request<T>`, `ApiError` carrying status/code/field errors,
`describeError`, single-flight refresh and a `tokenStore`.

**Rationale.** No extra dependency, full control over refresh semantics, one place to add telemetry or a
retry policy later.

**Consequences.** Interceptors, upload progress and request cancellation would have to be written by hand
if ever needed. The wrapper is the natural seam for a future `AbortController` implementation.

**Trade-offs.** Less ecosystem tooling (mock adapters, devtools). The Vitest suite covers the wrapper
directly, which compensates.

---

## ADR-015 — Hosted demonstration on Vercel + Render with Redis and Kafka disabled

**Status:** Accepted (2026-11-15).

**Context.** The platform needed a live, clickable deployment for portfolio review without a recurring
infrastructure bill. The full stack (SQL Server + Redis + Kafka) costs more than a demo justifies.

**Problem.** Ship a live demonstration that is honest about what is running.

**Options considered.**

1. No live deployment — rejected: reviewers should be able to click the real product.
2. Full stack on managed services (Azure Container Apps + Azure SQL + Redis + Kafka) — deferred: correct
   production target, disproportionate cost for a demonstration.
3. **Vercel (static console) + Render (single API instance, SQLite), Redis and Kafka disabled** — chosen.

**Decision.** Frontend on Vercel at `https://nexus-bank-sand.vercel.app`; API on Render at
`https://nexusbank-api-w53j.onrender.com`; database SQLite on the instance; `Redis__Enabled` and
`Kafka__Enabled` off. The fallbacks described in ADR-009 are what serve requests.

**Rationale.** Zero-cost hosting, a real URL, and the degraded-mode design means the console reports the
truth instead of pretending the dependencies exist.

**Consequences.**

- `/health` is permanently `degraded` in this environment, with the reason strings visible.
- Kafka events are recorded and flagged `isSimulated: true`; replay works from the database.
- The SQLite file is ephemeral: state created during a session is lost on redeploy (see
  `docs/DISASTER_RECOVERY.md` §2).
- Cold starts on the free tier add latency to the first request after idle.

**Trade-offs.** Redis and Kafka are **implemented and available in the project** and **supported in
local/containerised environments**, but they do not execute in the hosted deployment. Any claim that the
hosted demo exercises Kafka or Redis is false. Enabling them is a configuration change (plus a managed
instance), not a code change.

---

## ADR-016 — Same-origin `/api` proxy and SPA fallback as deployment routing

**Status:** Accepted (2026-11-15).

**Context.** The console is served from Vercel and the API from Render — different origins. The console's
API client uses relative paths.

**Problem.** Wire two platforms together without shipping an API origin into the browser bundle or
depending on CORS for every call.

**Options considered.**

1. Absolute API base URL baked at build time (`VITE_API_BASE_URL`) — rejected for this deployment: couples
   the artefact to an environment, and every call becomes cross-origin (preflight + CORS).
2. Server-side BFF on Vercel — rejected: extra runtime for a static console.
3. **Rewrite `/api/:path*` to the API origin at the Vercel edge, plus an SPA fallback** — chosen.

**Decision.** `frontend/vercel.json` declares two rewrites: `/api/:path*` → the Render origin, and a
catch-all → `/index.html` (excluding `/api/`). The client keeps relative paths.

**Rationale.** Same-origin requests from the browser's perspective: no preflight, no CORS dependency, no
environment-specific build. The same relative paths work behind the Vite dev proxy and nginx in Compose.

**Consequences.** Vercel's rewrite table becomes application routing and must be maintained with the code.
Omitting the SPA fallback makes every deep link (including `/login`) a platform 404 — verified during the
audit: `/` returned 200, `/login` returned 404 before the fallback was added.

**Trade-offs.** The browser no longer knows the API origin, so diagnosing "which backend am I hitting"
requires reading `vercel.json`. CORS is still configured server-side because direct cross-origin calls
remain possible (and are the fallback path if the proxy is removed).

---

## ADR-017 — Per-integration resilience settings are stored, not yet applied

**Status:** **Proposed (documented gap).** Not an accepted design.

**Context.** Each integration row carries `RetryMaxAttempts`, `RetryBaseDelayMs`, `MaxDelayMs`,
`CircuitBreakerFailureThreshold` and `CircuitBreakerBreakSeconds`. The probe client uses a **fixed**
standard resilience handler (3 retries, exponential + jitter, 50% failure-ratio breaker, 10 s attempt
timeout, 30 s total).

**Problem.** Operators can edit values in the UI that do not affect behaviour — a documentation and
correctness hazard.

**Options considered.**

1. Remove the columns and expose a single global policy — rejected: per-integration policy is genuinely
   needed (a reconciliation service tolerates different latency than a card authoriser).
2. Apply the values via a named client per integration — possible but creates client sprawl.
3. **Apply the values through a dynamically configured `ResilienceHandler` keyed by integration** —
   preferred.

**Decision (proposed).** Read the integration's stored settings when building the pipeline for that
probe, keeping the fixed standard handler as the default when a value is absent.

**Rationale.** The UI should not offer a control that does nothing, and per-integration policy is a real
requirement for an integration platform.

**Consequences.** Until implemented, documentation must describe the resilience pipeline as fixed and the
columns as stored metadata.

**Trade-offs.** Dynamic pipeline construction must respect `IHttpClientFactory` handler lifetime and avoid
rebuilding a handler per request.

---

## ADR-018 — Maker–checker for high-value payments and reversals

**Status:** **Proposed.** Not implemented.

**Context.** Any operator with `payments:write` can create, refund and reverse a payment. There is no
second approver, no threshold and no four-eyes control (`docs/SECURITY.md` §2.5).

**Problem.** Single-actor money movement is unacceptable in a bank, regardless of how well it is audited.

**Options considered.**

1. Status quo — rejected for any real deployment: audit is detective, not preventive.
2. Approve every payment — rejected: unusable at volume.
3. **Threshold-based approval (amount, type, reversal always) with a maker/checker state machine** —
   preferred.

**Decision (proposed).** Add a `PendingApproval` state to `PaymentStatus`, record `InitiatedByUserId` (it
already exists) and `ApprovedByUserId`, and reject self-approval in the service. Reversals always require
a second actor.

**Rationale.** Matches how payment operations actually work and reuses the existing journey/audit
machinery to show both actors.

**Consequences.** Changes the payment lifecycle, the console (an approvals queue), the API and the tests;
`PaymentServiceTests` and the idempotency contract would need extending.

**Trade-offs.** Adds latency to payment completion and a new operational queue. A deliberate scope
decision for the portfolio build, now recorded as a gap rather than left implicit.

---

## ADR-019 — Conditional update / optimistic concurrency on account balances

**Status:** **Proposed.** Correctness gap identified during this audit.

**Context.** `ValidateInstruction` reads `AvailableBalance` and rejects insufficient funds; the balance is
adjusted later in the same unit of work. Player-level serialisation on SQLite hides the race; SQL Server's
read-committed isolation would not.

**Problem.** Two concurrent debits can both pass the funds check and overdraw an account.

**Options considered.**

1. `SERIALIZABLE` transaction — rejected: contention and deadlock risk on the hot account row.
2. Application-level lock — rejected: ineffective across instances.
3. **Conditional update (`WHERE AvailableBalance >= @amount`) or `rowversion` optimistic concurrency** —
   preferred.

**Decision (proposed).** Perform the debit as a conditional set-based update and treat zero rows affected
as `insufficient_funds`; or add `rowversion` to `Accounts` and retry on `DbUpdateConcurrencyException`.
Also catch the `IdempotencyRecords` unique-constraint violation and re-read instead of returning 500.

**Rationale.** Moves the invariant into the database, where it is enforced regardless of instance count or
isolation-level subtleties.

**Consequences.** Requires a migration, a service change and the concurrency tests described in
`docs/TESTING.md` §6.

**Trade-offs.** Optimistic concurrency pushes retry logic into the service and needs a bounded retry count
to avoid livelock under contention.

---

## ADR-020 — Cache the dashboard and observability read models in Redis

**Status:** **Proposed.** Redis is implemented but currently on no business read path.

**Context.** The console polls the dashboard every 25 s per open session; each load issues several
aggregate queries. `ICacheService` exists, supports Redis with an automatic memory fallback, and is used
only by the health probe.

**Problem.** Repeated expensive aggregation for every viewer, with an idle cache abstraction.

**Options considered.**

1. Leave it — rejected at any real concurrency: the dashboard is the most-hit expensive endpoint.
2. Cache in the frontend only — partially present (TanStack Query, 15 s staleTime) but per-browser, so N
   users still cost N× server work.
3. **Server-side cache of the read model with write-through invalidation** — preferred.

**Decision (proposed).** Cache `dashboard` and `observability:overview:{hours}` for 10–60 s, invalidated
by payment/incident/integration writes rather than TTL alone.

**Rationale.** Turns N users polling into one query per interval, and gives the existing cache layer a
real purpose.

**Consequences.** Requires cache tests (`docs/TESTING.md` §8) and careful invalidation — a stale
permission or incident list is a correctness issue, not cosmetic.

**Trade-offs.** Adds a failure mode (stale reads) and a dependency on Redis being reachable for freshness
guarantees. The memory fallback makes correctness per-instance rather than global.

---

## ADR-021 — Security gates in CI (advisories, Dependabot, image scanning)

**Status:** **Proposed.** Findings recorded in `docs/SECURITY.md` §6.

**Context.** CI builds, tests and builds images, but performs no dependency audit, no image scan, no SBOM,
and Dependabot/secret scanning are not configured. Seven advisories are open across NuGet and npm,
including a high-severity native SQLite advisory in the provider the hosted deployment actually uses.

**Problem.** Advisory drift is discovered by a human running a command, or not at all.

**Options considered.**

1. Rely on manual audits — the current state; already behind.
2. Fail the build on any advisory — rejected initially: a critical dev-only test-runner finding would block
   all work.
3. **Tiered gates: fail on high/critical runtime findings, report dev-only findings** — preferred.

**Decision (proposed).**

- `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev --audit-level=high`
  as failing steps.
- Dependabot for `nuget`, `npm`, `docker`, `github-actions`.
- `dependency-review-action` on pull requests.
- Image scan (Trivy) + SBOM upload for both images.
- Secret scanning and push protection enabled at repository level.

**Rationale.** Gates must match real risk: a vulnerable test runner is not the same as a vulnerable
native library in the runtime image.

**Consequences.** CI time increases by ~1–2 minutes; the four npm findings and three NuGet findings must be
triaged (fix or formal exception with an expiry) before the gate can be strict.

**Trade-offs.** False positives and upgrade churn. Mitigated by scoping gates to runtime paths first and
recording exceptions explicitly rather than suppressing warnings.
