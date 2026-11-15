# Changelog

Notable changes to NexusBank, newest first. Dates are release dates; the repository does not yet publish
version tags or GitHub releases, so entries are grouped by the change itself rather than by semver. The API
advertises version `1.0.0` in its OpenAPI document.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Added
- *Nothing yet.*

### Known gaps carried forward
- Payment concurrency is not covered by a test; the funds check is a lost-update candidate on a
  non-serialising provider (`docs/TESTING.md` §6.1, ADR-019).
- Idempotency read-then-insert can surface as a `500` on the unique index instead of a replay (§6.2).
- No maker–checker on payments or reversals (ADR-018).
- No leader election for the outbox relay, so the API cannot be scaled out as-is (ADR-004).

---

## 2026-11-15 — Documentation baseline

A full documentation audit and rewrite against the source code. **No application behaviour, schema or
business logic was changed** by this release, with one exception noted under *Fixed*.

### Added
- `docs/OBSERVABILITY.md` — what telemetry actually exists (request log, Serilog, correlation ids,
  OpenTelemetry, `/metrics`, health probes), what is deliberately absent (cache hit ratio, broker-side lag,
  host metrics, RUM, central log sink), and a worked diagnostic workflow.
- `docs/PERFORMANCE.md` — cost model per request, index-driven read paths, measured bottlenecks, caching
  analysis (including the fact that Redis is not yet on a business read path), and a reproducible k6 plan
  with an empty results table rather than invented numbers.
- `docs/DISASTER_RECOVERY.md` — backup/restore reality for SQL Server, SQLite and the hosted demo, RPO/RTO
  derived from configuration, failure scenarios, integrity-verification queries and a **Recovery Validation
  Evidence** section that is intentionally empty.
- `docs/OPERATIONS_RUNBOOK.md` — twelve incident procedures (API down, proxy/CORS, database, Redis, Kafka,
  consumer lag, dead letters, failed payments, duplicate payments, auth failures, latency, workers) with
  symptoms, diagnosis, recovery and post-recovery validation.
- `docs/CI_CD.md` — the actual pipeline (build, 36 unit tests, 14 integration tests, migration check,
  frontend typecheck/build/tests, both image builds), the absence of a deployment job, and proposed security
  and quality gates.
- `docs/screenshots/` — six genuine captures of the live deployment plus a manifest describing provenance,
  the tool overlay artefact, and how to re-capture cleanly.
- `CONTRIBUTING.md` — workflow, layered-backend rules, frontend conventions, PR checklist and the
  documentation honesty rules.
- `CHANGELOG.md` (this file).
- ADRs 015–021: hosted Vercel/Render configuration, SPA proxy routing, per-integration resilience settings
  (proposed), maker–checker (proposed), optimistic concurrency on balances (proposed), caching the dashboard
  read model (proposed), CI security gates (proposed).

### Changed
- `README.md` — rewritten around business problems, engineering value and evidence: capability table with
  verification pointers, architecture diagrams, live-deployment section with the real fallback strings,
  grouped technology stack, accurate quick start for both shapes, verified test results, and an explicit
  limitations/roadmap section.
- `docs/SECURITY.md` — corrected to the implementation: `Referrer-Policy: no-referrer`, HSTS **not** emitted
  by the application (edge-supplied only), no `UseHttpsRedirection`, Swagger public, rate limits keyed on the
  proxy address. Added OWASP Top 10 mapping, twelve-threat model, vulnerability management with the seven
  open advisories, and a production checklist marked implemented/partial/recommended.
- `docs/ARCHITECTURE.md` — system context, container view, request lifecycle, authentication, payment,
  ledger, outbox, event-processing (Kafka **and** in-process fallback), caching and resilience flows; module
  boundaries and their enforcement; scalability and evolution reasoning.
- `docs/TESTING.md` — suite inventory with verified pass counts, what each suite protects, twelve scenario
  specifications including payment concurrency and Kafka failure/replay, and explicit "not implemented"
  markers.
- `docs/DECISIONS.md` — consistent ADR template (context, problem, options, decision, rationale,
  consequences, trade-offs) and the new records listed above.
- `docs/DEPLOYMENT.md` — two separate deployment shapes: hosted (Vercel console + Render API, SQLite,
  Redis/Kafka disabled for cost optimisation) and full infrastructure (Compose with SQL Server, Redis 7,
  Kafka 3.7), plus Azure as an optional future target that is **not** deployed.
- `docs/DATABASE.md` — corrected index inventory (`Payments` unique `Reference` and
  `(IdempotencyKey, CreatedAt)`; `KafkaEvents` indexed on `ProducedAt`/`Status` only; `ApiRequestLogs` on
  `Timestamp` and `(StatusCode, Timestamp)`) and corrected retention behaviour (health samples and events
  are **not** trimmed).

### Fixed
- `frontend/vercel.json` — added the SPA fallback rewrite `{ "source": "/((?!api/).*)", "destination":
  "/index.html" }` after the API rewrite. Without it every deep link returned the platform 404 page (for
  example `/login`), which presented as "login is broken on the deployment" even though the API accepted the
  credentials. Committed as `df9f471` and verified live: `/`, `/login`, `/payments`, `/events` and
  `/security` all return `200 text/html`.

### Verified in this release
- `dotnet test backend/NexusBank.sln -c Release` → 36 passed (`NexusBank.Tests`), 14 passed
  (`NexusBank.IntegrationTests`), 0 failed.
- `npm test` → 19 passed. `npm run build` → typecheck and bundle succeed.
- `docker compose config --quiet` → valid with placeholder secrets; the stack itself was not started.
- Live deployment: console loads, API `/health` reports `degraded` with
  `In-process memory cache (Redis unreachable)` and `In-process development bus (Kafka disabled by
  configuration)`, Swagger JSON is publicly reachable, authenticated console views render real API data.

### Not changed, on purpose
- Redis and Kafka remain **enabled in configuration and available in the project**, and **disabled in the
  Render deployment** for cost optimisation. No attempt was made to enable them there.
- No licence file was added: choosing a licence is the repository owner's decision.
- No secrets, credentials or keys were added to the repository.

---

## Earlier — platform build (undated)

- Modular monolith: `NexusBank.Api`, `Application`, `Domain`, `Infrastructure`, `IntegrationServices`,
  `Workers`, `Shared`.
- Payment processing with double-entry ledger postings, journey steps, refunds and reversals.
- Transactional outbox with a background publisher; Kafka producer/consumer with an in-process fallback.
- Integration hub with probe pipeline, topology, health samples and audit history.
- Observability built from `ApiRequestLogs`; incidents, security centre and system health modules.
- React 19 console with TanStack Query, Zod-validated forms, light/dark themes and permission-gated UI.
- EF Core initial migration, deterministic seeder, unit/service/integration test suites.
- Docker Compose stack (SQL Server 2022, Redis 7, Kafka 3.7 in KRaft mode, API, nginx-served console).
- GitHub Actions CI: build, tests, migration-script check, container builds.
- Hosted deployment: console on Vercel, API on Render, with the Vercel `/api/:path*` rewrite so the browser
  talks to the API same-origin.
