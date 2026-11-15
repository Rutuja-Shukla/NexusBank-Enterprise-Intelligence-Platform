# Security

Security in NexusBank is implemented in the API, not asserted in the console. This document describes
what the code actually does, maps it to the OWASP Top 10, records the threat model and residual risk,
and lists the dependency advisories that are open **today**.

Everything here is written to be checkable against the repository. Where a control is absent or partial,
that is stated rather than implied.

> **Not a compliance claim.** NexusBank holds synthetic data only. Nothing here constitutes PCI DSS,
> SOC 2, ISO 27001, GDPR or banking-regulatory compliance, and no certification, audit or penetration
> test has been performed.

---

## 1. Authentication

### 1.1 Sign-in flow (`POST /api/v1/auth/login`)

```
FluentValidation on { email, password }
  → rate limit "auth" (12 requests/minute per connection IP)

  → user lookup by lower-cased email (unique index on Users.Email)
  → not found            → audit(Failure) → 401 (identical message to wrong password)
  → IsActive = false     → audit(Denied)  → 401 code=account_disabled
  → LockoutEnd > now     → audit(Denied)  → 401 code=account_locked
  → BCrypt.Verify(password, hash)
       mismatch → FailedLoginAttempts++ ; at 5 → LockoutEnd = now + 15 min, counter reset
                → audit(Failure) → 401
  → success → reset counters, LastLoginAt = now
            → permissions = role grants ∪ direct grants
            → access token (JWT, 30 min) + refresh token (random, hashed, 7 days)
            → RefreshToken row (hash, IP, user agent)
            → audit(Success)
            → one SaveChanges
```

Two properties are deliberate and test-covered:

- **No user enumeration.** Unknown account and wrong password are indistinguishable to the caller.
- **Evidence survives rejection.** `WriteAuditAsync` commits the audit row *before* the exception is
  thrown, so a failed sign-in is recorded even though the request fails
  (`AuthServiceTests`: unknown email rejected *and audited*).

### 1.2 Tokens

| Token | Format | Lifetime | Server state | Client storage |
| --- | --- | --- | --- | --- |
| Access | JWT, **HS256** | 30 minutes (`Jwt:AccessTokenMinutes`) | None — validated cryptographically | `localStorage` (`nexusbank.accessToken`) |
| Refresh | 48 random bytes, base64 | 7 days | SHA-256 hex hash only (`TokenHash`, 128-char column), plus IP and user agent | `localStorage` (`nexusbank.refreshToken`) |

Access-token claims: `iat`, `sub` (user id), `email`, `jti`, `nameidentifier`, `name`, `role` (slug),
`role_name`, and one `permission` claim per granted permission.

Validation: signature, `iss`, `aud`, lifetime with 30 s clock skew. **No `nbf` claim is emitted** —
deliberately. A not-before claim tied token validity to the *issuing* host's clock and produced
`IDX10222` rejections when an instance ran ahead; `iat` and `exp` are still exact and all other
validations are unchanged (`JwtTokenServiceTests` asserts a token minted by a host 38 minutes fast is
accepted by a correctly-clocked validator).

Rejections are logged with a reason — `token_expired`, `invalid_signature`, `invalid_issuer`,
`invalid_audience`, `malformed_token` — never with the token itself. A 401 is therefore diagnosable from
production logs (`JwtBearerEvents.OnAuthenticationFailed`).

### 1.3 Refresh-token rotation

`POST /api/v1/auth/refresh`:

1. Hash the presented token (SHA-256, hex) and look it up — no plaintext token is ever stored.
2. Reject if the row is inactive, revoked, expired, or its user is inactive (`session_expired`).
3. **Rotate:** revoke the presented row (`RevokedAt`, `RevokedReason = "rotated"`), issue a new pair.
4. Return the rotated pair plus the profile.

Rotation makes replay detectable: a stolen token that is used first invalidates the legitimate holder's
next refresh, and the presented token is single-use by construction (the old hash is revoked inside the
same transaction).

Sessions are visible and revocable per device: `GET /auth/sessions` (marks `isCurrent`),
`DELETE /auth/sessions/{id}`. Revoking a session invalidates the refresh token immediately; the access
token remains valid until it expires (≤ 30 minutes) — a documented consequence of stateless access
tokens.

### 1.4 Password storage

- **BCrypt**, work factor **12**, per-password salt (`BcryptPasswordHasher`).
- `PasswordHash` is 256 chars, never serialised, never logged, never returned by any endpoint.
- Verification returns `false` (not an exception) for empty/whitespace/malformed stored hashes
  (`PasswordHasherTests`).
- Password policy on user creation: ≥ 12 characters with upper, lower and digit
  (`createUserSchema`, `NexusValidators`).

BCrypt cost 12 costs roughly 100–300 ms of CPU per verification. That is the intended trade-off: it is
the most expensive operation on an anonymous endpoint, and it is why the auth endpoint has its own
stricter rate limit.

### 1.5 Login protection

| Control | Implementation | Limit |
| --- | --- | --- |
| Per-account lockout | `FailedLoginAttempts`, `LockoutEnd` | 5 failures → 15 minutes |
| Auth rate limit | `RateLimiting:AuthPermitLimit`, policy `"auth"` | 12/minute per connection IP |
| Global rate limit | `RateLimiting:PermitLimit` | 600/minute per connection IP |
| Uniform failure response | Same status/message for unknown account and wrong password | — |

**Caveat that matters in the hosted deployment:** partition keys are
`HttpContext.Connection.RemoteIpAddress`, and the application does **not** call `UseForwardedHeaders`.
Behind Vercel → Render the remote address is a proxy address, so the 12/min budget is shared by traffic
arriving through that proxy rather than isolated per end user. The same address is what lands in
`AuditLogs.IpAddress` and `ApiRequestLogs.ClientIp`. Fixing this correctly means configuring
`ForwardedHeadersOptions` with the known proxy networks (not blanket-accepting `X-Forwarded-For`, which
would let any client spoof its address and evade the limiter).

### 1.6 What authentication does not do

- No MFA, step-up authentication or device trust.
- No breached-password / common-password screening at sign-in.
- No CAPTCHA or bot challenge.
- No token binding: a refresh token is not bound to the IP or user agent that created it (both are
  recorded for investigation, not enforced).
- No concurrent-session limit.

---

## 2. Authorisation

### 2.1 Model

A **permission** is a string code (`payments:write`, `audit:read`, …). A **role** is a named bundle of
permissions. A user has exactly one role plus optional direct grants (`UserPermissions`), and the
effective set is `role grants ∪ direct grants`.

Enforcement uses a custom provider and handler, so a controller declares intent and nothing else:

```csharp
[Authorize(Policy = NexusPermissions.PaymentsWrite)]
public async Task<ActionResult<PaymentCreatedDto>> Create(...)
```

- `PermissionPolicyProvider` builds a policy on demand for any policy name containing `:` — adding a
  permission never requires new authorisation plumbing.
- `PermissionAuthorizationHandler` succeeds when the caller has a matching `permission` claim.
- Controllers contain **no role comparisons**.

### 2.2 Effective grants (seeded)

| Role | Capability |
| --- | --- |
| Administrator | Everything, including security administration |
| Operations Engineer | Dashboard, integrations, payments, events, incidents, observability, health |
| Developer | Explorer, integrations, observability, events |
| Read-Only Auditor | Read access plus audit trail; no mutations |

The live composition is returned by `GET /api/v1/security/roles`, so the Security screen shows the
actual grants rather than a hard-coded matrix.

### 2.3 Verified boundaries

Asserted by the integration suite against the real host (14 tests, all passing as of 2026-11-15):

| Actor | Action | Result |
| --- | --- | --- |
| Anonymous | `GET /api/v1/payments` | `401` problem details |
| Auditor | `GET /api/v1/security/roles` | `200` |
| Auditor | `POST /api/v1/payments` | `403` |
| Operations engineer | `GET /api/v1/security/users` | `403` |
| Administrator | any | allowed by role composition |

The console hides navigation and disables actions for missing permissions, but that is convenience only:
the API decision is authoritative, and the API explorer runs with the caller's own token to demonstrate
it.

### 2.4 Privilege escalation paths

| Path | Status | Note |
| --- | --- | --- |
| Role checks in controllers | Eliminated | Permission policies only |
| Client-side hiding treated as security | Rejected | API tests assert 403 without the UI |
| Self-service role change | Not possible | `PUT /security/users/{id}` requires `security:write`; there is no "my profile" endpoint that accepts a role |
| Permission claim forgery | Requires the signing key | HS256 shared secret; key is never in the repo or in client code |
| Administrator bypass | **Present by design** | The handler grants access when the role claim is `administrator`, regardless of individual permission claims. This is a deliberate composition shortcut, but it means a mis-granted administrator role is total compromise — treat `administrator` as a privileged entitlement |
| Cross-tenant data access | Not applicable / **not modelled** | All operators of sufficient permission see all records. There is no tenant, branch or portfolio scoping. A real deployment would need row-level ownership checks |

### 2.5 Maker–checker

**Not implemented.** Payments, refunds and reversals are single-actor: whoever holds `payments:write` can
create and reverse without a second approver. There is no approval workflow, no four-eyes control and no
dual-control state on any entity. The audit trail records *who* did it, which supports after-the-fact
review but not prevention.

For a payments platform this is the most significant control gap in the build, and it is listed as such
in `docs/DECISIONS.md` (proposed ADR-018).

---

## 3. Data protection

### 3.1 Classification

| Class | Examples in this build | Handling |
| --- | --- | --- |
| Credentials | Password hashes, refresh-token hashes, JWT signing key | Hashed at rest (passwords, refresh tokens); signing key only in configuration; never logged |
| Sensitive identifiers | Account numbers, BICs, customer names/emails | Synthetic only; account numbers masked in all API responses |
| Financial records | Payments, ledger entries, amounts | Synthetic; immutable history; no PII coupling |
| Security evidence | Audit logs, failed sign-ins, session metadata | Append-only by convention; readable only with `security:read`/`audit:read` |
| Operational telemetry | Request logs, traces, health | No bodies, no tokens; correlation ids only |
| Public | Integration catalogue, topics, stored value | Available to any authenticated operator |

### 3.2 PII handling

All customers, accounts and payments are **synthetic** (`NexusBankSeeder`). There is no real personal or
banking data in the repository, the seed or the hosted deployment. The platform is nonetheless built as
though there were: identifiers are masked, bodies are not logged, and error responses never echo request
content.

### 3.3 Identifier masking

`Masking.AccountNumber` returns `12-••••-1300` style values (bank prefix + last four digits) and is
applied on every payment list, payment detail, ledger entry and account option payload. Values shorter
than six digits are fully masked. Raw account numbers exist in the database but are not returned by the
API.

### 3.4 Encryption in transit

- The hosted console and API are served over HTTPS by their platforms (Vercel, Render). TLS terminates at
  the platform edge.
- **The application does not enforce HTTPS itself**: there is no `UseHttpsRedirection` and no
  `UseHsts`. In the hosted deployment an `Strict-Transport-Security` header *is* returned (added by the
  hosting edge — observed in the live response), but that is a platform behaviour, not an application
  control, and it would not follow the API to a different host that lacks it.
- Integration probes can target `https://` endpoints; the `HttpClient` uses platform TLS defaults.

### 3.5 Encryption at rest

- **Not implemented.** SQLite stores the database as a plain file; SQL Server in Compose runs without
  TDE; no field-level encryption is applied.
- Secrets at rest rely on the hosting platform: Render environment variables, GitHub Actions secrets,
  Key Vault if the Azure target is used.
- This is a documented gap, not an oversight: backup encryption and database TDE are production
  prerequisites, listed in §7.

### 3.6 Secret management

| Secret | Where it lives | Rotation |
| --- | --- | --- |
| `Jwt__SigningKey` | Environment/configuration; **required outside Development** or startup throws | Manual; rotating it signs every user out |
| `ConnectionStrings__*` | Environment; passwords in connection strings | Manual |
| `MSSQL_SA_PASSWORD` | `.env` (Compose only), git-ignored | Manual |
| Integration credentials | **Not stored.** Only a `SecretReference` pointer (`secret://integrations/…`) | N/A — the value is never in the database and never returned by the API |
| Demo user passwords | Seeded as BCrypt hashes; the plaintext is documented in the README and shown in the UI by design | N/A — demonstration limitation |

`.env` is git-ignored; `.env.example` contains placeholders only. Outside `Development` the host
**refuses to start** without a signing key rather than falling back to a known one — a deliberate
fail-closed choice.

### 3.7 Retention

| Data | Retention | Mechanism |
| --- | --- | --- |
| `ApiRequestLogs` | 7 days | `TelemetryRetentionWorker` |
| Processed `OutboxMessages` | 7 days | `TelemetryRetentionWorker` |
| Expired `IdempotencyRecords` | 24 hours after creation | Expiry column + retention worker |
| `RefreshTokens` | 7 days (expiry); deleted 30 days after expiry or revocation | Retention worker |
| `AuditLogs` | **Indefinite** | Never deleted by the application |
| Payments / ledger / incidents | Indefinite | Never deleted |

### 3.8 Secure logging

- Serilog request logging records method, path, status, duration and correlation id — **no bodies, no
  headers, no tokens**.
- Authentication failures log a reason code, not the token or the attempted password.
- Domain warnings log the exception code and message; unexpected exceptions log the full stack server-side
  and return a generic 500 with a correlation id.
- `AuditLogs.Metadata` is capped at 2000 characters and contains business detail (reference, amount,
  outcome), not credentials.

---

## 4. HTTP transport and header hardening

### 4.1 Headers actually emitted by `SecurityHeadersMiddleware`

| Header | Value | Purpose |
| --- | --- | --- |
| `X-Content-Type-Options` | `nosniff` | Stop MIME sniffs |
| `X-Frame-Options` | `DENY` | No framing (clickjacking) |
| `Referrer-Policy` | `no-referrer` | Never leak URL context |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | Disable device APIs |
| `Cross-Origin-Resource-Policy` | `same-origin` | Block cross-origin embedding of API responses |
| `Cache-Control` | `no-store` | Sensitive JSON must not be cached by proxies |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'` | Applies to **API responses**, not the SPA |

Headers are set in `OnStarting`, so they apply to error responses as well.

The SPA is served differently: on Docker/nginx, `frontend/nginx.conf` adds `X-Content-Type-Options`,
`X-Frame-Options`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` and
`server_tokens off` — but **no CSP for the HTML document**. On Vercel the static document is served with
platform defaults. A production console should carry its own CSP (`script-src 'self'`, no inline
scripts) — recorded as a gap in §7.

### 4.2 CORS

Explicit allow-list, never `*`:

```csharp
policy.WithOrigins(allowedOrigins)      // Cors:AllowedOrigins
      .AllowAnyHeader()
      .AllowAnyMethod()
      .WithExposedHeaders("X-Correlation-ID")
```

Credentials are **not** enabled: tokens travel in the `Authorization` header, so cookie-based CORS is
neither needed nor allowed. The hosted API allows the Vercel console origin — verified in the live
response (`Access-Control-Allow-Origin: https://nexus-bank-sand.vercel.app`, `Vary: Origin`).

Note that the console reaches the API through a same-origin proxy rewrite, so CORS is not exercised in
normal use; the allow-list matters only for direct cross-origin calls.

### 4.3 Rate limiting

| Policy | Limit | Applies to |
| --- | --- | --- |
| Global | 600/minute/IP | Every request |
| `auth` | 12/minute/IP | `/auth/login`, `/auth/refresh`, `/explorer/execute` |

Rejections return `429` with `code: rate_limited`. Fixed-window, in-memory partitioning, per process —
so limits are per instance and do not survive a restart or coordinate across replicas.

### 4.4 Input bounds

- FluentValidation on every request DTO: lengths, ranges, enums, patterns (e.g. BIC 8 or 11 chars,
  amount ≤ 1,000,000, event payload ≤ 64 KB).
- Database column lengths match validation (`HasMaxLength`) so a bypass cannot widen storage.
- ASP.NET Core request body limits are the framework defaults.

### 4.5 Swagger exposure

`UseSwagger()` and `UseSwaggerUI()` are registered unconditionally, so the API's full surface is publicly
enumerable on the hosted service:

- `GET /swagger/v1/swagger.json` → `200` (verified 2026-11-15)
- `GET /swagger/index.html` → Swagger UI

This is convenient for a portfolio demonstration and a genuine information-disclosure exposure in a real
bank. Production: disable it, or place it behind internal ingress and authentication.

---

## 5. OWASP Top 10 (2021) mapping

Reference: <https://owasp.org/www-project-top-ten/>. This is a self-assessment against the categories,
**not** a certification or an audit.

### A01 — Broken Access Control

- **Threat.** An authenticated operator performs an action their role does not permit; or a caller
  reaches data belonging to another party.
- **Mitigation.** Permission policies on every action; roles as compositions; API-side enforcement;
  integration tests assert 403 for auditor writes and operations-directory reads.
- **Location.** `PermissionAuthorization.cs`, `[Authorize(Policy = …)]` on all controllers,
  `tests/NexusBank.IntegrationTests`.
- **Gap.** Administrator bypass by role claim (§2.4); no data scoping between operators; no maker-checker
  (§2.5).

### A02 — Cryptographic Failures

- **Threat.** Credentials or tokens recoverable from storage, transit or logs; weak hashing.
- **Mitigation.** BCrypt cost 12 with per-password salt; refresh tokens stored only as SHA-256 hashes;
  HS256 with a ≥ 32-byte key enforced at startup; `no-store` on API responses; tokens/passwords never
  logged; edge TLS with HSTS at the hosting platform.
- **Location.** `BcryptPasswordHasher`, `JwtTokenService`, `SecurityHeadersMiddleware`, `Program.cs`
  startup guard.
- **Gap.** Symmetric signing key with no rotation/key-ring; tokens in `localStorage`; no encryption at
  rest (§3.5); no application-level HSTS or HTTPS redirect (§3.4).

### A03 — Injection

- **Threat.** SQL/NoSQL/command injection through filters, search, sort or the explorer.
- **Mitigation.** EF Core parameterised LINQ throughout; **no string-concatenated SQL**; sorting is an
  allow-list `switch` over fixed column names; paging is numeric; FluentValidation bounds every string;
  the explorer executes only catalogue-declared paths.
- **Location.** `PaymentService`, `EventStreamService`, `ObservabilityService`, `ApiExplorerService`.
- **Gap.** Search uses unanchored `LIKE '%term%'` — safe from injection, but an unindexed scan
  (`docs/PERFORMANCE.md` §3.2). No raw-SQL escaping policy is needed today because there is no raw SQL
  outside migrations.

### A04 — Insecure Design

- **Threat.** Missing business guard rails: double payment on retry, unbalanced postings, silent
  failure, single-actor money movement.
- **Mitigation.** `Idempotency-Key` with request hash and stored response; a single `SaveChanges` for
  payment + journey + ledger + outbox + audit + idempotency; refunds capped at the original amount and
  only from `Completed`; reversals only from `Completed`; failed payments persist with a code and no
  ledger postings; balanced-books invariant exposed as `ledgerBalanced`.
- **Location.** `PaymentService`, `IdempotencyRecords` (unique `(Scope, Key)`), `PaymentServiceTests`.
- **Gap.** No maker-checker; no per-transaction limits or velocity rules; no four-eyes on reversals.

### A05 — Security Misconfiguration

- **Threat.** Debug surfaces, permissive CORS, default credentials, verbose errors, missing headers.
- **Mitigation.** Fail-closed startup without a signing key; explicit CORS allow-list; security headers
  on every response; generic 500s with correlation ids only; HSTS supplied by the hosting edge; Swagger
  is the only intentionally public surface.
- **Location.** `Program.cs`, `SecurityHeadersMiddleware`, `ExceptionHandlingMiddleware`.
- **Gap.** **Swagger public**; `/health` discloses infrastructure modes (cache/broker strings) to
  anonymous callers; seeded demo credentials displayed in the login UI; rate limiting keyed on a proxy
  address; no CSP on the SPA document; no `UseHttpsRedirection`.

### A06 — Vulnerable and Outdated Components

- **Threat.** A known-vulnerable dependency in the runtime or the test toolchain.
- **Mitigation.** Pinned NuGet and npm versions with committed lockfiles; CI restore/build/test on every
  push; `dotnet list package --vulnerable` and `npm audit` are available and were run for this audit.
- **Location.** `*.csproj`, `package.json`, `package-lock.json`, `.github/workflows/ci.yml`.
- **Gap.** No automated scanning gate, no Dependabot, no container scanning. Four npm advisories and
  three NuGet advisories are open — see §6, which records them rather than suppressing them.

### A07 — Identification and Authentication Failures

- **Threat.** Credential stuffing, brute force, session theft, weak password policy.
- **Mitigation.** BCrypt cost 12, per-IP auth rate limit, 5-attempt lockout, uniform failure response,
  rotating single-use refresh tokens, revocable sessions, audited authentication outcomes, 12-character
  password policy.
- **Location.** `AuthService`, `AuthController`, `AuthServiceTests`.
- **Gap.** No MFA, no breached-password check, no CAPTCHA, no token binding, no anomaly detection.

### A08 — Software and Data Integrity Failures

- **Threat.** A message published for a rolled-back transaction; a replayed instruction; a tampered
  release artefact.
- **Mitigation.** Transactional outbox (business write + event in one transaction); idempotency with
  request hashing; append-only audit trail; immutable payment journey and ledger history.
- **Location.** `OutboxPublisherWorker`, `IdempotencyRecords`, `AuditLogs`.
- **Gap.** Events are not signed or hash-chained, so broker-level tampering is detectable only by
  reconciliation; no artefact signing, SBOM or digest-pinned deploy; CI does not verify package
  integrity beyond lockfiles.

### A09 — Security Logging and Monitoring Failures

- **Threat.** An attack succeeds and nobody can tell.
- **Mitigation.** Authentication outcomes (success/failure/denied/lockout/refresh/logout) audited before
  the response; security-relevant mutations audited; failed JWT validations logged with a reason;
  correlation ids on every request, response and audit row; per-request telemetry feeding error groups
  and alerts.
- **Location.** `AuthService.WriteAuditAsync`, `JwtBearerEvents`, `RequestTelemetryMiddleware`,
  `ObservabilityService.GetAlertsAsync`.
- **Gap.** No central log sink or SIEM forwarding; audit table is not tamper-evident (no hash chain,
  no WORM storage) and the API's database user could update it; alerts do not page anyone
  (`docs/OBSERVABILITY.md` §9).

### A10 — Server-Side Request Forgery

- **Threat.** The server is induced to call an attacker-chosen destination (cloud metadata, internal
  services, loopback admin ports).
- **Mitigation.** The API explorer executes **only** paths declared in its static catalogue, against the
  API's own base URL, with an authenticated caller; response bodies are truncated at 200 KB.
- **Location.** `ApiExplorerService` (catalogue + path-template matching), `SelfApiInvoker`.
- **Gap.** Integration probes fetch **operator-supplied URLs** (`POST /integrations`, then
  `POST /integrations/{id}/probe`). Anyone with `integrations:write` can make the API issue requests to
  arbitrary hosts, including internal ones. There is no private-address blocklist, no egress policy and
  no DNS-rebinding defence. Acceptable in a demo with synthetic fixtures; a real deployment must add an
  outbound allow-list and network egress controls.

---

## 6. Vulnerability management

### 6.1 Current dependency findings (verified 2026-11-15)

Backend — `dotnet list NexusBank.Api package --vulnerable --include-transitive`:

| Package | Resolved | Severity | Scope | Advisory |
| --- | --- | --- | --- | --- |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.9.0 | Moderate | Direct (API) | GHSA-4625-4j76-fww9 |
| `OpenTelemetry.Api` | 1.9.0 | Moderate | Transitive | GHSA-g94r-2vxg-569j |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.6 | **High** | Transitive (SQLite provider) | GHSA-2m69-gcr7-jv3q |

Frontend — `npm audit`:

| Package | Severity | Scope | Notes |
| --- | --- | --- | --- |
| `tinypool` (via `vitest`) | **Critical** (2 advisories) | **Development only** — test runner | Prototype-pollution → RCE in worker options; never shipped to the browser bundle |
| `zod` < 4.6.6 | High | **Production** — form validation in the console | Memory exhaustion validating oversized constrained collections |
| `@vitest/mocker` (via `vitest`) | Moderate | Development only | Path traversal / arbitrary file read, code injection via manual mock registration |

`zod` is the only finding on a runtime path, and it is client-side: an attacker can only exhaust their own
browser tab. The native SQLite advisory is the most consequential backend item because the hosted
deployment **runs SQLite**.

These warnings are **not suppressed**. The NuGet advisory appears as `NU1902` on every build, and
`Directory.Build.props` deliberately leaves `TreatWarningsAsErrors=false`, so CI stays green with the
advisory visible. That is a conscious trade-off to be revisited (§7).

### 6.2 Remediation plan

| Finding | Action | Status |
| --- | --- | --- |
| `OpenTelemetry.*` 1.9.0 | Upgrade to the current 1.x line and re-run the suites | **Not done** — planned |
| `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 | Upgrade the `Microsoft.EntityFrameworkCore.Sqlite` family so the bundled native SQLite moves past the advisory | **Not done** — planned |
| `zod` 3.x | Upgrade to the patched line (currently a breaking major) | **Not done** — planned; form schemas are small and migration is contained |
| `vitest`/`tinypool`/`@vitest/mocker` | Upgrade the test runner; dev-only, so lower urgency | **Not done** — planned |

An upgrade is only "done" when `dotnet test` (36 + 14) and `npm test` (19) still pass — the counts are
recorded in `docs/TESTING.md`.

### 6.3 Severity classification

| Severity | Response expectation | Example in this repo |
| --- | --- | --- |
| Critical | Fix or mitigate before the next deploy; disable the affected feature if necessary | `tinypool` (dev-only → no production exposure, documented exception) |
| High | Fix within the current iteration; document any temporary exception with an owner and expiry | `SQLitePCLRaw`, `zod` |
| Moderate | Fix in the next maintenance window | `OpenTelemetry.*`, `@vitest/mocker` |
| Low | Batch with other work | — |

### 6.4 Exception management

Where a finding is knowingly accepted, the exception must record: package, advisory id, severity,
why it is acceptable (scope), compensating control, owner, expiry date. The two acceptable exceptions
today are the **development-only** test-runner findings, because the affected code never reaches the
browser bundle or the API image. Everything else is an open remediation item, not an accepted risk.

### 6.5 Tooling status

| Control | Status |
| --- | --- |
| `dotnet list package --vulnerable` | Available and run manually; **not in CI** |
| `npm audit` | Available and run manually; **not in CI** |
| GitHub Dependabot | **Not configured** (no `.github/dependabot.yml`) |
| GitHub secret scanning / push protection | **Not configured** in the repository settings (no code change can enable it) |
| Dependency review action on PRs | **Not configured** |
| Container image scanning (Trivy/Grype) | **Not configured** |
| SBOM generation | **Not configured** |
| Lockfiles committed | Yes — `package-lock.json`; NuGet uses exact versions in `*.csproj` |

See `docs/CI_CD.md` §7 for the concrete CI additions proposed.

---

## 7. Production security checklist

Status legend: **Implemented** · **Partial** · **Recommended** (not present).

| Control | Status | Evidence / note |
| --- | --- | --- |
| Password hashing with a slow KDF | Implemented | BCrypt cost 12, per-password salt |
| Rotating, revocable refresh tokens | Implemented | SHA-256 hash at rest, single-use rotation, per-session revoke |
| Short-lived access tokens | Implemented | 30 min. Bank target is 5–10 min plus proof-of-possession |
| Permission-based authorisation | Implemented | Policy provider + handler on every action |
| Account lockout | Implemented | 5 attempts / 15 minutes |
| Auth rate limiting | Partial | 12/min per connection IP; not per-user behind a proxy |
| Audit trail for security events | Implemented | Append-only by convention |
| Security headers on API responses | Implemented | See §4.1 |
| HSTS | **Partial** | Not emitted by the application; supplied by the hosting edge only |
| HTTPS redirect in app | **Recommended** | No `UseHttpsRedirection` |
| CSP for the SPA document | **Recommended** | nginx/Vercel add no document CSP |
| Asymmetric JWT signing (RS256/ES256 + JWKS) | **Recommended** | HS256 shared secret today |
| Signing-key rotation without mass sign-out | **Recommended** | No key ring / overlapping keys |
| MFA / step-up authentication | **Recommended** | Not implemented |
| Maker–checker on payments | **Recommended** | Not implemented (§2.5) |
| Refresh token in HttpOnly cookie + CSRF | **Recommended** | `localStorage` today (§8) |
| Database least-privilege user | **Recommended** | Compose uses `sa`; hosted uses a SQLite file account |
| Encryption at rest / TDE | **Recommended** | Not implemented |
| Secret rotation runbook | **Recommended** | Manual, no schedule |
| Swagger disabled in production | **Recommended** | Publicly reachable today |
| WAF / DDoS / bot management | **Recommended** | Platform-edge responsibility; none configured |
| Central log sink / SIEM + PII scrubbing | **Recommended** | stdout only |
| SIEM alerting on auth anomalies | **Recommended** | Rules exist in-app, no notification channel |
| Container/image scanning + SBOM | **Recommended** | Not in CI |
| Penetration test / threat-model review | **Recommended** | Never performed |
| Incident response procedure | **Partial** | In-app incident workflow; no external paging/on-call process |
| Dependency vulnerabilities remediated | **Partial** | 7 open advisories (§6.1) |

---

## 8. Known limitations (and how to close them)

1. **Tokens in `localStorage`.** `frontend/src/lib/api.ts` persists access and refresh tokens in
   `localStorage`, which any successful XSS can read. Production: access token in memory only, refresh
   token in an HttpOnly `Secure` `SameSite=Strict` cookie with a double-submit CSRF token, plus a strict
   CSP on the console.
2. **Symmetric JWT signing.** HS256 with one shared key suits a single service. Multiple services need
   RS256/ES256 with a published JWKS and overlapping key rotation.
3. **No mTLS between services.** Integration records support an `AuthType` of mutual TLS *as
   configuration*, but no client certificates are provisioned and the probe client does not present one.
4. **No WAF, DDoS protection or bot management.** Those belong at the platform edge (Azure Front Door,
   Cloudflare, or the Vercel edge), not in application code.
5. **Swagger is public** in every documented environment.
6. **Demo credentials are published.** `ops@nexusbank.dev` / `NexusBank!2026` is seeded, documented in the
   README and pre-filled on the login screen. That is the point of a demonstration, and it means the
   hosted deployment must never be pointed at non-synthetic data.
7. **SSRF surface on integration probes.** Operator-supplied URLs are fetched server-side (§5 A10).
8. **No penetration test, formal threat-model review or compliance assessment** has been performed. This
   document is the engineering baseline, not an assurance certificate.

---

## 9. Hardening sequence for a real deployment

Ordered so that each step reduces risk independently.

1. Disable Swagger outside Development, or put it behind internal ingress.
2. Move refresh tokens to HttpOnly cookies and add CSRF protection; keep access tokens in memory.
3. Add a CSP to the console document and keep `no-referrer`; verify with a report-only phase first.
4. Replace HS256 with RS256/ES256 and publish JWKS; rotate keys with an overlap window.
5. Configure `ForwardedHeadersOptions` with the known proxy networks so rate limiting and audit IPs are
   real end-user addresses.
6. Add maker–checker for payment creation above a threshold, and for every reversal.
7. Use a least-privilege database login; revoke `db_owner`; grant `INSERT`/`SELECT` on `AuditLogs` and
   nothing else to the application account.
8. Enable encryption at rest (TDE / encrypted volume) and encrypt backups.
9. Ship Serilog to a central sink with retention and access control; add alert routing.
10. Add CI gates: `dotnet list package --vulnerable`, `npm audit --audit-level=high`, Dependabot,
    dependency review, image scanning, SBOM.
11. Remediate the advisories in §6.1 and re-run the suites.
12. Commission an external penetration test and a threat-model workshop; record findings in this file.
