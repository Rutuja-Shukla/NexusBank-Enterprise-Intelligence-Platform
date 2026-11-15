# Testing

The suites target the behaviour that would hurt most if it broke: authentication and lockout, payment
atomicity and idempotency, authorisation boundaries, ledger consistency, validation and the contracts the
console depends on.

This document records what is tested, what is **not** tested, the results of the last executed run, and
the scenarios a bank would expect but this build does not yet cover.

---

## 1. Verified results

Executed on **2026-11-15** in this workspace (`dotnet test backend/NexusBank.sln -c Release` and
`npm test`), on Windows with .NET SDK 9.0.102 building `net8.0`, Node 22.14.0:

| Suite | Passed | Failed | Skipped | Duration |
| --- | --- | --- | --- | --- |
| `NexusBank.Tests` (unit/service) | **36** | 0 | 0 | ~23 s |
| `NexusBank.IntegrationTests` (end-to-end API) | **14** | 0 | 0 | ~42 s |
| `frontend` Vitest (unit + component) | **19** | 0 | 0 | not recorded for this run |

Build warnings observed during that run (not failures, and not suppressed):

- `NU1902` — `OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0` has a known moderate advisory.
- `CS8604` — possible null reference in `AuthController.Refresh` (`request.RefreshToken` is passed where a
  non-null string is expected; the service rejects null at runtime).

See `docs/SECURITY.md` §6 for the full advisory list and remediation plan.

**No coverage figure is published.** No coverage run has been executed for this repository, and a
plausible-looking percentage would be worse than an honest absence.

---

## 2. Suite inventory

| Suite | Project / folder | Tests | Runs against |
| --- | --- | --- | --- |
| Backend unit and service | `backend/tests/NexusBank.Tests` | 36 | SQLite in-memory `NexusBankDbContext`, real BCrypt, real services, fakes for clock/identity/tokens |
| Backend integration | `backend/tests/NexusBank.IntegrationTests` | 14 | Real `Program` host via `WebApplicationFactory`, isolated SQLite file, full middleware/auth/seeding |
| Frontend unit | `frontend/tests/unit` | 16 | Vitest + jsdom |
| Frontend component | `frontend/tests/components` | 3 | Vitest + React Testing Library |
| End-to-end (optional) | `frontend/tests/e2e` | 2 specs | A running API (:8080) and console (:5173) — **not executed in CI** |

Backend test classes:

| File | Focus |
| --- | --- |
| `PasswordHasherTests` | Unique salts, verification, malformed-hash rejection |
| `AuthServiceTests` | Unknown email audited, successful login, counter reset, lockout after five failures, disabled account |
| `JwtTokenServiceTests` | `iat`/`exp` exactness, no `nbf`, clock-skew regression, expired/wrong-signature/wrong-issuer/wrong-audience/tampered tokens rejected |
| `PaymentServiceTests` | Balanced postings, journey steps, outbox row, balance movement, idempotent replay, key reuse conflict, simulated failure with no postings, BIC routing, insufficient funds writes nothing |
| `ValidatorTests` | Amount bounds, currency, BIC shape, missing debtor, password policy, explorer method, `mock://` accepted / relative URLs rejected |
| `SqliteDateRoundTripTests` | `DateTimeOffset` round-trip and range predicates under SQLite |
| `ApiIntegrationTests` | Anonymous health/metrics, 401/403/404 boundaries, login, RBAC denials, idempotency end-to-end, ledger balance, validation errors, explorer catalogue, seeded hashes, simulated event publishing |

---

## 3. Commands

```bash
# backend — build once, then run suites
cd backend
dotnet build NexusBank.sln -c Release
dotnet test tests/NexusBank.Tests/NexusBank.Tests.csproj -c Release --no-build
dotnet test tests/NexusBank.IntegrationTests/NexusBank.IntegrationTests.csproj -c Release --no-build

# frontend
cd frontend
npm ci
npm run build            # tsc -b && vite build — types are a hard gate
npm test                 # vitest run
npx playwright test      # optional: needs API on :8080 and console on :5173
```

CI runs the same commands plus a Release build, an idempotent migration-script check and both container
builds (`.github/workflows/ci.yml`; see `docs/CI_CD.md`).

---

## 4. What the backend suites prove

### 4.1 Authentication and session behaviour

| Scenario | Expected | Covered by |
| --- | --- | --- |
| Unknown email | Rejected with the same message as a wrong password, and an audit row is committed | `AuthServiceTests` |
| Valid credentials | Tokens issued; failed counter reset; only the refresh **hash** persisted | `AuthServiceTests` |
| Wrong password | Counter incremented, audit failure written | `AuthServiceTests` |
| Five failures | Account locked for 15 minutes; next attempt reports `account_locked` | `AuthServiceTests` |
| Disabled account | Denied with `account_disabled` | `AuthServiceTests` |
| Token minted by a clock 38 min fast | Accepted by a correctly-clocked validator | `JwtTokenServiceTests` |
| Expired / tampered / wrong issuer / wrong audience | Rejected with the expected IdentityModel exception | `JwtTokenServiceTests` |

Refresh-token rotation and revocation are exercised through the service layer but **not** asserted
end-to-end in the integration suite — listed as a gap in §10.

### 4.2 Payment integrity

| Scenario | Expected | Covered by |
| --- | --- | --- |
| Completed payment | Balanced debit/credit, ≥5 journey steps, outbox row, debtor balance reduced | `PaymentServiceTests` |
| Same idempotency key, same payload | Original response returned, no duplicate row | `PaymentServiceTests`, `ApiIntegrationTests` |
| Same key, different payload | `409 idempotency_key_reuse` | `PaymentServiceTests`, `ApiIntegrationTests` |
| Simulated processor rejection | Persisted `Failed` with a code and reason, **no** ledger entries | `PaymentServiceTests` |
| Insufficient funds | Rejected, nothing written | `PaymentServiceTests` |
| BIC present | Routes through the external gateway step | `PaymentServiceTests` |
| Payment detail | `ledgerBalanced: true`, ≥5 journey steps | `ApiIntegrationTests` |

### 4.3 Authorisation

| Actor | Action | Expected | Covered by |
| --- | --- | --- | --- |
| Anonymous | `GET /api/v1/payments` | 401 problem details | `ApiIntegrationTests` |
| Auditor | `GET /security/roles` | 200 | `ApiIntegrationTests` |
| Auditor | `POST /payments` | 403 | `ApiIntegrationTests` |
| Operations | `GET /security/users` | 403 | `ApiIntegrationTests` |

### 4.4 Degraded-mode honesty

`/health` is asserted to report the database as connected and to state the Redis fallback explicitly;
publishing an event without a broker returns `isSimulated: true`; `/metrics` returns Prometheus text.
These tests protect the platform's most important cultural property: it does not claim a dependency is
healthy when it is not.

---

## 5. What the frontend tests prove

| Area | Assertions |
| --- | --- |
| API client | Query-string building drops empty values; `ApiError.firstFieldError` surfaces the first field message; `describeError` never leaks object representations; `tokenStore` persists, reads and clears the session |
| Formatting | ms/s fallback, byte units, numbers/percent/currency, uptime durations, relative timestamps |
| Presentation | Status→tone mapping, JSON pretty-printing (including invalid input), initials, title case, class merging |
| Component | `StatCard` renders label/value/hint, shows a delta only for finite values, treats a flat change as flat |

Not covered on the frontend: the login form (Zod messages + submit), permission-based navigation,
the payment form, the refresh/single-flight path, and the reduced-motion hook.

---

## 6. Payment concurrency scenario (designed, **not implemented**)

This is the test a payments platform is expected to have and this build does not: concurrent debits
against one account, and concurrent retries of the same instruction.

**Status: not implemented. No such test exists in the repository, and no result is claimed.**

### 6.1 Concurrent debits against one account

```
Given a debtor account with availableBalance = 1,000.00 NZD
When  10 requests each debit 150.00 NZD are issued concurrently (10 distinct Idempotency-Keys)
Then  the number of Completed payments is exactly 6
  and the 4 remaining requests are rejected with 400 (insufficient available funds)
  and account.Balance >= 0 and account.AvailableBalance >= 0
  and each Completed payment has exactly 2 ledger entries that balance
  and no failed request produced ledger entries
```

What it would protect: the funds check (`ValidateInstruction`) reads `AvailableBalance` and the balance
adjustment happens later in the same unit of work. Under true concurrency two requests can both read the
pre-debit balance, so a **lost-update / double-spend** window exists. On SQLite writes serialise behind a
file lock, which masks the defect; on SQL Server the default read-committed isolation would **not** prevent
two concurrent readers from passing the funds check. The durable fix is a conditional update
(`UPDATE … WHERE AvailableBalance >= @amount`) or an optimistic concurrency token (`rowversion`) on
`Accounts`, plus a handler for the conflict.

This is therefore a genuine correctness risk, not just a coverage gap. See `docs/DECISIONS.md`
(proposed ADR-019).

### 6.2 Concurrent retries of the same instruction

```
Given an Idempotency-Key K and payload P
When  5 identical POST /payments requests with K and P are issued concurrently
Then  exactly one Payments row exists
  and every response has the same id
  and exactly one response has replayed = false
  and the idempotency unique index (Scope, Key) never raises an unhandled 500
```

What it would protect: the read-then-insert sequence on `IdempotencyRecords` is not atomic. Two racing
requests can both see "no record", both write, and one hits the unique index — surfacing as a 500 rather
than a replay or a clean `409`. The fix is to catch the unique-constraint violation and re-read.

### 6.3 How to implement it

- A xUnit test with `Task.WhenAll` over 10 `PaymentService.CreateAsync` calls against a shared SQLite
  connection, asserting the invariants above (this reproduces the serialisation behaviour).
- A true concurrency test against SQL Server in the Compose stack (or k6 at the API level) to expose the
  isolation issue.
- `docs/DECISIONS.md` ADR-019 tracks the schema change needed to close it.

---

## 7. Kafka failure scenario (test design only)

**Status: designed, with no automated test yet.** Kafka itself is implemented and available in the project
(§9 of `docs/ARCHITECTURE.md`); what does not exist is a test that exercises broker-level failure, retry and
replay. The *fallback* behaviour is covered by the integration suite.

| Step | Expected behaviour | Test coverage |
| --- | --- | --- |
| Broker unreachable while publishing | Publish accepted and flagged `isSimulated: true`; `/events/status` reports the in-process bus | ✅ `ApiIntegrationTests` |
| Outbox relay publish failure | `Attempts++`, `NextAttemptAt` set with exponential backoff, row retained, `ProcessedAt` stays null | ❌ not tested |
| Retry succeeds after the broker returns | Row processed, `KafkaEvents` written with `isSimulated: false`, no duplicate business effect | ❌ not tested |
| Poison message / deliberate dead-lettering | `POST /events/{id}/dead-letter` sets status and reason; retry is refused for `DeadLettered` unless replayed | ❌ not tested |
| Replay of a dead-lettered event | `POST /events/{id}/retry` republishes the stored payload, sets `Retried`, decrements lag | ❌ not tested |
| Consumer restart recovery | `AutoOffsetReset.Latest`, commit-on-consume, reconciliation by `(topic, offset)` — no duplicate `KafkaEvents` rows | ❌ not tested (needs a broker) |

To implement: a Compose-based test fixture that starts Kafka, publishes, stops the broker mid-cycle,
restarts it, and asserts outbox `Attempts` and eventual `ProcessedAt`. That is an integration-test
infrastructure project, not a unit test — honest scope statement.

---

## 8. Redis caching tests (gap)

**No caching tests exist, because no business read path uses the cache.** The only cache interaction is
the health probe (`ComponentHealthProbe` does a set/get on `health:probe`), which the integration suite
covers indirectly through `/health`.

If caching is added (`docs/PERFORMANCE.md` §5), the tests that must follow:

| Test | Assertion |
| --- | --- |
| Hit | Second identical request does not re-query the database |
| Miss | First request queries, stores, returns |
| Invalidation | A write through the owning service removes or replaces the key |
| Redis down | Service falls back to memory, never throws, `/health` degrades |
| Malformed cached payload | Deserialisation failure is treated as a miss, not a 500 |
| TTL | Key expires; no stale permission set survives a role change |

---

## 9. Database transaction and rollback tests (partial)

Covered:

- A failed validation writes **nothing** (`PaymentServiceTests`: insufficient funds).
- A simulated processor failure writes the payment and journey but **no** ledger entries.
- Idempotent replay writes no second payment.
- SQLite `DateTimeOffset` round-trip and range predicates (`SqliteDateRoundTripTests`).

Not covered:

| Scenario | Why it matters |
| --- | --- |
| Exception **after** the payment row is added but before `SaveChanges` | Proves the unit of work rolls back atomically (no half-written journey/ledger) |
| Concurrent write conflict behaviour | See §6 |
| Migration applied to a database with existing data | Expand/contract discipline |
| Restore-from-backup verification | See `docs/DISASTER_RECOVERY.md` §9 (no rehearsal yet) |

A rollback test is straightforward to add: inject a failing `SaveChanges` (or a service that throws after
adding entries), assert no rows exist afterwards.

---

## 10. Security testing

Asserted today (integration + unit):

- Anonymous access to protected endpoints → 401.
- Auditor write attempt → 403; operations reading the security directory → 403.
- Seeded users store BCrypt hashes, never the demo password.
- Lockout after five failures; disabled account denied; audit written on failure.
- JWT tampering, expiry, issuer/audience mismatch rejected.

**Not** asserted — the security testing backlog:

| Missing test | Risk it would catch |
| --- | --- |
| Refresh rotation rejects a reused token | Stolen-token replay |
| Revoked session cannot refresh | Incomplete sign-out |
| Rate limiter returns 429 after the configured limit | Brute-force resistance regression |
| Password-policy enforcement on user creation | Weak credentials |
| Privilege escalation attempt (non-admin setting a role) | Authorisation defect |
| Idempotency 409 does not leak another caller's response | Cross-actor information leak |
| Security headers present on error responses | Header regression |
| Integration probe blocks private address ranges | SSRF (`docs/SECURITY.md` §5 A10) |

---

## 11. Performance and failure-recovery testing

- **Performance:** no load test has been run. `docs/PERFORMANCE.md` §9 contains a reproducible k6
  plan with scenarios, thresholds and the result table to fill in. Providers are not asserted.
- **Failure and recovery:** procedures are written and unexercised (`docs/DISASTER_RECOVERY.md`).
  The "Recovery Validation Evidence" table is deliberately empty.

---

## 12. Manual verification performed on this build

Against the running API (`http://localhost:8080`, SQLite) and console (`http://127.0.0.1:5173`):

- Signed in as administrator; dashboard rendered live telemetry.
- Created a payment: `Completed`, five journey steps, `ledgerBalanced = true`.
- Replayed the same `Idempotency-Key`: `replayed = true`, same id.
- Explorer executed `GET /api/v1/integrations` successfully (200, measured latency).
- Auditor received 403 on payment creation while reads succeeded.
- Event publish returned `simulated = true` with no broker.
- `/health` returned `degraded` with the memory-cache and in-process-bus strings.
- Every list endpoint returned 200 (dashboard, integrations, topology, payments, accounts, events, topics,
  consumer groups, observability, incidents, security, system health, explorer catalogue).
- Reseeded the development database and confirmed timestamps render correctly.

Against the **live hosted deployment** (Vercel console → Render API), during the documentation audit on
2026-11-15:

- `POST /api/v1/auth/login` with the seeded operations account returned `200` with tokens.
- The console signed in through the browser and the dashboard rendered live data
  (`/dashboard`, `/system/health`, `/observability/alerts`, `/incidents` all `200`).
- `GET /health` returned `degraded` with `"cache": "In-process memory cache (Redis unreachable)"` and
  `"eventBus": "In-process development bus (Kafka disabled by configuration)"`.
- `GET /api/v1/system/health` (authenticated) reported the database as `Sqlite`, Redis `degraded`, Kafka
  `degraded`, and the Kafka consumer worker idle.
- `GET /metrics` returned Prometheus text; `GET /health/live` returned `{"status":"live"}`.

---

## 13. Not verified in this environment

- `docker compose up --build` end-to-end (the Docker CLI is present and `docker compose config` validates
  with the required variables set, but the stack was not started for this audit).
- SQL Server execution of the migration (script generation succeeds in CI; no SQL Server instance was used
  here).
- Kafka and Redis connectivity — both are disabled in the hosted environment, and the Compose stack was
  not started, so only the fallback paths are verified.
- Playwright end-to-end specs (they need both servers and were not executed).
- Cross-browser, mobile-device and accessibility audits.
- The concurrency, Kafka-failure and caching scenarios in §6–§8.

---

## 14. Extending the suites (prioritised)

1. **Payment concurrency** (§6) — highest value; it addresses a real correctness window.
2. **Refresh rotation and revocation** end-to-end.
3. **Rate limiter** assertions (429 with `code: rate_limited`).
4. **Idempotency race** (§6.2) — unique-constraint handling should not be a 500.
5. **Outbox failure semantics** (§7) — attempt counter, backoff, eventual success.
6. **Rollback test** for the payment unit of work (§9).
7. **Caching tests** if and when caching is wired (§8).
8. **Frontend**: login form, permission-driven navigation, the refresh single-flight path.
9. **Playwright journey**: sign in as auditor → write action disabled → sign in as administrator →
   create payment → open journey.
10. **Explorer execution in the test host**: replace `IApiInvoker` with a loopback implementation so
    `POST /explorer/execute` is assertable without a TCP listener (today it is verified manually).

Coverage percentages remain deliberately unpublished until a coverage run is executed and recorded.
