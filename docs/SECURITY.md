# Security

Security is treated as a product requirement: authentication, authorization, auditing and safe
failure are implemented in the API, not simulated in the console.

## 1. Authentication

### Sign-in

```
POST /api/v1/auth/login  { email, password }
  → validate shape (FluentValidation)
  → rate limit (12 requests/minute/IP)
  → find user by lower-cased email (unique index)
  → account disabled?            → 401 account_disabled  + audit(Denied)
  → lockout active?              → 401 account_locked    + audit(Denied)
  → BCrypt.Verify(password, hash)
       mismatch → FailedLoginAttempts++ ; 5 → lockout 15 min ; audit(Failure) ; 401
  → issue access token (JWT, 30 min) + refresh token (random, hashed, 7 days)
  → create RefreshToken row (hash, IP, user-agent) ; audit(Success) ; return profile
```

Unknown accounts and wrong passwords return the same message and code, so the endpoint cannot be
used to enumerate users. Every branch writes an audit row **before** the exception is thrown — the
failed-login evidence survives the rejection (covered by `AuthServiceTests`).

### Tokens

| Token | Lifetime | Storage | Notes |
| --- | --- | --- | --- |
| Access (JWT HS256) | 30 minutes | Console `localStorage` | Claims: `sub`, `email`, `role`, permission claims, `iat`, `exp`; validated `iss`, `aud`, signature, lifetime (30s skew). No `nbf`: the token is valid the moment it is issued |
| Refresh | 7 days, rotating | Console `localStorage`; server stores only SHA-256 hash | Single use: refresh revokes the presented token and issues a new pair |

Access tokens deliberately omit `nbf`. A not-before claim ties validity to the *issuing* host's
clock: an instance running ahead produced tokens that a correctly-clocked instance rejected with
`IDX10222` (not yet valid). Issue time is still recorded in `iat`, expiry is exact, and signature,
issuer, audience and lifetime validation are unchanged. Authentication failures are logged with the
rejection reason (`token_expired`, `invalid_signature`, `invalid_issuer`, `invalid_audience`, …) so a
401 is diagnosable from production logs without logging the token.

Revoking a session (`DELETE /auth/sessions/{id}`) invalidates that refresh token immediately; the
access token expires naturally within 30 minutes. In a bank deployment access-token lifetime would be
shorter (5–10 minutes) with a token-binding or proof-of-possession scheme.

### Password storage

BCrypt, work factor 12, per-password salt (`BcryptPasswordHasher`). The hash column is 256 chars and
is never serialised. Passwords are never logged — Serilog request logging excludes bodies, and the
audit metadata for authentication contains only outcomes and reasons.

## 2. Authorization

Permissions are strings (`payments:write`, `audit:read`, …). A role is a named set of permissions;
a user has exactly one role plus optional direct grants. Enforcement is a custom
`IAuthorizationPolicyProvider` + `IAuthorizationHandler`:

```csharp
[Authorize(Policy = NexusPermissions.PaymentsWrite)]
public async Task<ActionResult<PaymentCreatedDto>> Create(...)
```

Any policy name is resolved to a permission requirement, so adding a permission never requires new
policy plumbing. Controllers contain no role comparisons.

### Verified boundaries

These are asserted by the integration suite against the real host:

| Actor | Action | Result |
| --- | --- | --- |
| Anonymous | `GET /api/v1/payments` | `401` problem details |
| Auditor | `GET /api/v1/security/roles` | `200` |
| Auditor | `POST /api/v1/payments` | `403` |
| Operations engineer | `GET /api/v1/security/users` | `403` |
| Developer | `GET /api/v1/explorer/catalogue` | `200` |
| Administrator | any | allowed by role composition |

The console hides navigation and disables actions for missing permissions, but that is convenience:
the API decision is authoritative, and the API explorer exposes that reality by running with the
caller's own token.

Role composition lives in `NexusBankSeeder` and is returned by `GET /api/v1/security/roles`, so the
Security screen always shows the live grant set rather than a hard-coded matrix.

## 3. Transport and HTTP hardening

- Security headers middleware on every response: `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`,
  `Permissions-Policy`, `Content-Security-Policy` for API responses, HSTS outside Development.
- CORS is an explicit origin allow-list (`Cors:AllowedOrigins`), not `*`; credentials are not
  enabled because tokens travel in the `Authorization` header.
- Rate limiting: 600 requests/minute/IP globally; 12/minute/IP for authentication and explorer
  execution. Rejections return `429` with `code: rate_limited`.
- Kestrel request body limits and FluentValidation length limits bound payload size (event payloads
  max 64 KB, descriptions 280 chars, etc.).
- Correlation ids are returned and logged; error responses never contain stack traces, SQL, or
  configuration values.
- Swagger is enabled in this build for demonstration. In production it should be disabled or placed
  behind the same authentication and network restrictions as the API.

## 4. Audit

`AuditLogs` is append-only and written for:

- authentication outcomes (success, failure, denied, lockout, refresh, logout),
- payment creation, refund and reversal,
- integration create/update/enable/disable/delete/probe,
- incident creation, status transitions and assignment,
- user creation and role/credential changes,
- security-sensitive reads where required by policy (for example denied actions).

Each row records actor id/email, action, entity type/id, outcome, severity, IP, correlation id and a
metadata note. The Security screen's audit tab is paged, filterable and opens the raw metadata. The
database user used by the API would be granted `INSERT`/`SELECT` on this table only in a hardened
deployment.

## 5. Secrets

- Nothing sensitive is committed. `.env` is ignored; `.env.example` contains placeholders only.
- Outside Development the host **refuses to start** without `Jwt:SigningKey`, rather than silently
  falling back to a known key.
- Integration credentials are stored as a `SecretReference` (for example
  `secret://integrations/fraud-detection`) — a pointer into a secret store, never the value. The API
  never returns it.
- In Azure the recommended stores are Key Vault references for Container Apps secrets; locally,
  `.env` or .NET user-secrets.

## 6. Data protection

- The platform holds **synthetic** customers, accounts and payments only.
- Account numbers are masked in list payloads (`12-••••-1313`).
- Logs contain method, path, status, duration and correlation id; request bodies and tokens are not
  logged.
- Refresh tokens and password hashes are the only credential material at rest.

## 7. Threat model (abridged)

| Threat | Control | Residual risk |
| --- | --- | --- |
| Credential stuffing | BCrypt cost 12, per-IP auth rate limit, lockout, audit | Distributed low-rate attacks need a WAF/bot defence in front |
| Token theft via XSS | Strict CSP on the API; React escapes output; no `dangerouslySetInnerHTML` | Token in `localStorage` is readable by any successful XSS — see §8 |
| Replay of payment creation | `Idempotency-Key` + request hash + stored response | Clients must actually send the key |
| Forged tokens | HS256 with ≥32-byte secret, `iss`/`aud`/lifetime validation | Key rotation requires a restart or a key ring; prefer RS256 + JWKS at scale |
| SQL injection | EF Core parameterised queries, no string concatenation of user input | Dynamic sorting is an allow-list switch, not raw SQL |
| Privilege escalation | Permission policies on every action, verified by tests | Misconfigured role grants are a governance issue, not a code one |
| Sensitive data in logs | Structured logging without bodies; masked identifiers | Custom log statements must keep the same discipline |
| Dependency risk | Pinned versions, CI restore/build on every push | `OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0` reports a moderate advisory (`NU1902`); upgrade when a compatible patch is available |

## 8. Known limitations (and how to close them)

1. **Tokens in `localStorage`.** Simplest for a demo, weakest against XSS. Production: HttpOnly,
   `Secure`, `SameSite=Strict` refresh cookie with a double-submit CSRF token, and keep the access
   token in memory only.
2. **Symmetric JWT signing.** HS256 with one shared key is fine for a single service. For multiple
   services use RS256/ES256 with a published JWKS and rotation.
3. **No mTLS between services.** Integration authentication supports API key, OAuth2 client
   credentials, JWT bearer and mTLS *configuration*, but the demo does not provision client
   certificates.
4. **No WAF, DDoS protection or bot management.** Those belong to the platform edge (Azure Front
   Door / Application Gateway), not the application.
5. **Swagger is public** in this build (see §3).
6. **No penetration test or formal threat-model review** has been performed; this document is the
   engineering baseline, not an assurance certificate.

## 9. Hardening checklist for a real deployment

- [ ] Replace HS256 with RS256 + JWKS; rotate signing keys through Key Vault.
- [ ] Move refresh tokens to HttpOnly cookies and add CSRF protection.
- [ ] Put the API behind Front Door/WAF with a rate limit at the edge as well as in the app.
- [ ] Disable Swagger, or protect it with an internal-only ingress.
- [ ] Use a least-privilege SQL login (no `db_owner`), separate credentials per environment.
- [ ] Enable Azure SQL auditing, Defender for SQL, and Key Vault diagnostic logs.
- [ ] Send Serilog to a central sink (Log Analytics / Seq) with PII scrubbing.
- [ ] Run `dotnet list package --vulnerable` and container scanning in CI.
- [ ] Add a secrets rotation runbook and an incident response procedure.
