# Testing

The suites target the behaviour that would hurt most if it broke: authentication and lockout,
payment atomicity and idempotency, authorization boundaries, validation, and the contracts the
console depends on.

## 1. Layout

| Suite | Project / folder | Count | Runs against |
| --- | --- | --- | --- |
| Backend unit and service tests | `backend/tests/NexusBank.Tests` | 28 | SQLite in-memory `NexusBankDbContext`, real BCrypt, real services, fakes for clock/identity/token issuance |
| Backend integration tests | `backend/tests/NexusBank.IntegrationTests` | 14 | The real `Program` host via `WebApplicationFactory`, isolated SQLite file, full middleware/auth/seeding |
| Frontend unit tests | `frontend/tests/unit` | 16 | Vitest, jsdom |
| Frontend component tests | `frontend/tests/components` | 3 | Vitest + React Testing Library |
| End-to-end (optional) | `frontend/playwright.config.ts` | — | A running API and console |

## 2. Commands

```bash
# backend
cd backend
dotnet build NexusBank.sln
dotnet test tests/NexusBank.Tests
dotnet test tests/NexusBank.IntegrationTests

# frontend
cd frontend
npm run build          # tsc -b + vite build
npm test               # vitest run
npm run test:coverage  # if @vitest/coverage is installed
npx playwright test    # requires servers on :8080 and :5173
```

CI runs the same commands (`.github/workflows/ci.yml`) plus a Release build, a migration-script
generation check, and both container builds.

## 3. What the backend unit tests prove

`NexusBank.Tests` — 28 tests:

- **Password hashing** (`PasswordHasherTests`): unique salts, correct verification, rejection of
  wrong passwords and malformed stored hashes.
- **Authentication** (`AuthServiceTests`): unknown email rejected *and audited*; valid login issues
  tokens, resets counters and persists only the hashed refresh token; wrong password increments the
  counter; five failures lock the account and the next attempt is reported as locked; disabled
  accounts are denied.
- **Payments** (`PaymentServiceTests`): completion produces balanced debit/credit postings, a
  journey of at least five steps, an outbox row and a reduced debtor balance; the same idempotency
  key replays without a duplicate row; the same key with a different payload is a conflict; a
  simulated processor rejection persists a failure with a code and **no** ledger postings; a BIC
  routes through the external gateway step; insufficient funds are rejected and write nothing.
- **Validation** (`ValidatorTests`): amount bounds, unsupported currency, malformed BIC, missing
  debtor, weak password policy, invalid explorer method, `mock://` accepted and relative URLs
  rejected.
- **SQLite date mapping** (`SqliteDateRoundTripTests`): `DateTimeOffset` round-trips through the
  SQLite provider and range predicates work — the regression guard for the dev-database issue found
  while running the app.

## 4. What the integration tests prove

`NexusBank.IntegrationTests` — 14 tests against the real host:

- `/health` is anonymous, reports the database as connected, and states the Redis fallback honestly;
  `/metrics` returns Prometheus text.
- Protected endpoints return problem-details `401` without a token; unknown routes `404`.
- Login rejects bad passwords; valid login returns a token whose profile carries role and permissions.
- The auditor can read security roles but cannot create payments (`403`); an operations engineer
  cannot read the security directory (`403`).
- Payment creation then replay with the same `Idempotency-Key` returns the same id with
  `replayed: true`; the detail payload has `ledgerBalanced: true` and at least five journey steps.
- Reusing a key with a different amount returns `409`.
- Invalid payment payloads return `400` with field errors.
- The explorer catalogue lists authenticated endpoints with their permission codes.
- Seeded users store BCrypt hashes, never the demo password.
- Publishing an event without a broker returns `isSimulated: true` and health reports the in-process
  bus.

### Deliberate gap

`POST /explorer/execute` is **not** asserted in the integration suite: the explorer calls back over
HTTP to the API's own base URL, and `WebApplicationFactory` exposes no TCP listener. It is verified
manually against the running server (see §6). A future improvement is to inject a loopback
`IApiInvoker` for that test, or to run the API under Kestrel in a fixture.

## 5. What the frontend tests prove

`frontend/tests` — 19 tests:

- Formatting: milliseconds/second fallback, byte units, number/percent/currency formatting, uptime
  durations, relative timestamps.
- Presentation: status→tone mapping, JSON pretty-printing (including invalid input), initials,
  title case, Tailwind class merging.
- API client: query-string building drops empty values, `ApiError.firstFieldError` surfaces the first
  validation message, `describeError` never leaks object representations, `tokenStore` persists and
  clears the session.
- Component: `StatCard` renders label/value/hint, only shows a delta for finite values, and treats a
  flat change as flat.

## 6. Manual verification performed on this build

Against the running API (`http://localhost:8080`, SQLite) and console (`http://127.0.0.1:5173`):

- Signed in as administrator; dashboard rendered live telemetry.
- Created a payment; response `Completed`, five journey steps, `ledgerBalanced = true`.
- Replayed the same `Idempotency-Key`; response returned `replayed = true` with the same id.
- Explorer executed `GET /api/v1/integrations` successfully (`200`, measured latency).
- Auditor received `403` on payment creation while reads succeeded.
- Event publish returned `simulated = true` without a broker.
- `/health` returned `degraded` with the memory-cache and in-process-bus strings.
- Every list endpoint returned `200` (dashboard, integrations, topology, payments, accounts, events,
  topics, consumer groups, observability, incidents, security, system health, explorer catalogue).
- Reseeded the development database and confirmed timestamps render correctly.

## 7. Not verified in this environment

- `docker compose up --build` and both container images (Docker engine was not running).
- SQL Server execution of the migration (script generation succeeds; no SQL Server instance here).
- Kafka and Redis connectivity (both disabled locally; the fallbacks are what is verified).
- Playwright end-to-end specs (they need both servers, and were not executed in this session).
- Cross-browser, mobile-device and accessibility audits.

## 8. Extending the suites

- **More integration coverage**: integration CRUD and probe with a stubbed `IHttpClientFactory`;
  incident state machine transitions; refresh-token rotation and revocation.
- **Explorer execution**: replace `IApiInvoker` with a loopback implementation in the test host.
- **Frontend**: add tests for the payment form (Zod messages + submit), permission-based navigation,
  and the reduced-motion hook using `matchMedia` mocks.
- **End-to-end**: a Playwright journey — login as auditor, assert a write action is disabled, login as
  administrator, create a payment, open its journey.
- **Performance**: k6 or NBomber against `/api/v1/payments` and `/api/v1/dashboard`, with a documented
  baseline rather than an unmeasured claim.

Coverage percentages are deliberately not quoted: no coverage run has been executed and published
here, and an invented number would be worse than none.
