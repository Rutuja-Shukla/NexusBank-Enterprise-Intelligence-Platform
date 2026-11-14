# API reference

Base URL: `http://localhost:8080` (development) · All resource routes live under `/api/v1`.
Interactive documentation: `/swagger` (OpenAPI 3, bearer auth wired into "Authorize").

## 1. Conventions

- **Auth**: `Authorization: Bearer <accessToken>`. Access tokens last 30 minutes; refresh tokens 7
  days and rotate on use.
- **Content type**: `application/json`. Enums are serialised as strings (`"Completed"`, `"Sev2"`).
- **Correlation**: every response carries `X-Correlation-ID`; the same value appears in logs and
  audit records.
- **Paging**: list endpoints accept `page` (1-based) and `pageSize` (default 20, max 100) and return

  ```json
  {
    "items": [],
    "page": 1,
    "pageSize": 20,
    "totalCount": 240,
    "totalPages": 12,
    "hasNext": true,
    "hasPrevious": false
  }
  ```

- **Search / sort**: `search` is a case-insensitive contains filter over the documented columns;
  `sort` accepts the documented keys (for example `-created`, `amount`, `reference`).
- **Idempotency**: `POST /payments` accepts an `Idempotency-Key` header. Replaying the same key and
  payload returns the stored response with `"replayed": true`; reusing the key with a different
  payload returns `409`.

### Error format

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "code": "validation_failed",
  "correlationId": "0HN7...",
  "errors": {
    "CreditorName": ["Beneficiary name is required."],
    "Amount": ["Amount must be between 0.01 and 1,000,000."]
  }
}
```

| Status | Meaning |
| --- | --- |
| `400` | Validation failure (field errors in `errors`) |
| `401` | Missing, expired or invalid token (`code: unauthorised`) |
| `403` | Authenticated but the role lacks the permission (`code: forbidden`) |
| `404` | Resource not found |
| `409` | Conflict — duplicate key, idempotency mismatch, invalid state transition |
| `429` | Rate limit exceeded (`code: rate_limited`) |
| `500` | Unexpected failure; details are logged, never returned |

## 2. Authentication

| Method | Path | Permission | Notes |
| --- | --- | --- | --- |
| POST | `/api/v1/auth/login` | anonymous | `{ email, password }` → `{ accessToken, refreshToken, expiresAt, user }`; 5 failures lock the account for 15 minutes |
| POST | `/api/v1/auth/refresh` | anonymous | `{ refreshToken }` → rotated pair + user |
| POST | `/api/v1/auth/logout` | authenticated | revokes the presented refresh token |
| GET | `/api/v1/auth/me` | authenticated | current profile and permissions |
| GET | `/api/v1/auth/sessions` | authenticated | own sessions with `isCurrent` |
| DELETE | `/api/v1/auth/sessions/{id}` | authenticated | revoke one session |
| GET | `/api/v1/auth/access-history?days=14` | authenticated | daily sign-ins vs failures |

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@nexusbank.dev","password":"NexusBank!2026"}'
```

## 3. Command centre

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/dashboard` | `dashboard:read` |

Returns `summary` (integration counts, request volume, payment volume, success rate, latency,
incidents, availability), `traffic`, `volume`, `recentPayments`, `activity`, `activeIncidents`,
`services`, `topologyNodes`, `topologyEdges` and `generatedAt`. Values are aggregated from
persisted payments, request logs and incidents.

## 4. Integrations

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/integrations?search=&status=&category=&page=&pageSize=` | `integrations:read` |
| GET | `/api/v1/integrations/lookup` | `integrations:read` |
| GET | `/api/v1/integrations/topology` | `integrations:read` |
| GET | `/api/v1/integrations/{id}` | `integrations:read` |
| POST | `/api/v1/integrations` | `integrations:write` |
| PUT | `/api/v1/integrations/{id}` | `integrations:write` |
| PATCH | `/api/v1/integrations/{id}/enabled` | `integrations:write` |
| POST | `/api/v1/integrations/{id}/probe` | `integrations:write` |
| DELETE | `/api/v1/integrations/{id}` | `integrations:write` |

Create body:

```json
{
  "name": "Fraud Detection Service",
  "key": "fraud-detection",
  "description": "Real-time scoring for card and account events",
  "category": "Risk",
  "baseUrl": "https://fraud.internal.example/api",
  "authType": "OAuth2ClientCredentials",
  "ownerTeam": "Financial Crime",
  "version": "v2",
  "timeoutSeconds": 8,
  "healthEndpointPath": "/health",
  "retryPolicy": {
    "maxAttempts": 3,
    "baseDelayMs": 200,
    "maxDelayMs": 5000,
    "circuitBreakerFailureThreshold": 5,
    "circuitBreakerBreakSeconds": 30
  }
}
```

`PATCH /{id}/enabled` takes `{ "enabled": false }`. `POST /{id}/probe` performs a real probe and
returns latency, status and the telemetry source (`live probe` or `simulated fixture`). Disabling or
deleting an integration writes an audit entry; it never deletes payment history.

## 5. Payments

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/payments?search=&status=&type=&from=&to=&minAmount=&sort=&page=` | `payments:read` |
| GET | `/api/v1/payments/stats?from=` | `payments:read` |
| GET | `/api/v1/payments/accounts?search=` | `payments:read` |
| GET | `/api/v1/payments/{id}` | `payments:read` |
| POST | `/api/v1/payments` | `payments:write` |
| POST | `/api/v1/payments/{id}/refund` | `payments:write` |
| POST | `/api/v1/payments/{id}/reverse` | `payments:write` |

Create:

```bash
curl -s -X POST http://localhost:8080/api/v1/payments \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: 8f7c1a52-9b0e-4b2f-8f3a-6a1d2c9e7b11' \
  -d '{
    "debtorAccountId": "<account guid>",
    "creditorName": "Harbour Consulting Ltd",
    "creditorAccountNumber": "99-8888-0777777-01",
    "creditorBic": "NEXUNZ2A",
    "amount": 250.75,
    "currency": "NZD",
    "type": "Transfer",
    "description": "Invoice 2026-114"
  }'
```

Response `201`:

```json
{
  "id": "…",
  "reference": "PAY-261113-433865",
  "status": "Completed",
  "amount": 250.75,
  "currency": "NZD",
  "createdAt": "2026-11-13T09:41:02.110+00:00",
  "replayed": false,
  "correlationId": "…"
}
```

`GET /payments/{id}` adds `journey` (ordered steps with service, duration, status, error),
`ledgerEntries`, `ledgerDebitTotal`, `ledgerCreditTotal`, `ledgerBalanced`, `failureCode` and
`failureReason`.

Refund: `{ "amount": 50.00, "reason": "Partial return" }` (amount optional, cannot exceed the
original). Reverse: `{ "reason": "Duplicate instruction" }` (completed payments only).

## 6. Events

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/events?topicId=&status=&search=&deadLettersOnly=&page=` | `events:read` |
| GET | `/api/v1/events/{id}` | `events:read` |
| GET | `/api/v1/events/topics` | `events:read` |
| GET | `/api/v1/events/consumer-groups` | `events:read` |
| GET | `/api/v1/events/status` | `events:read` |
| POST | `/api/v1/events/publish` | `events:write` |
| POST | `/api/v1/events/{id}/retry` | `events:write` |
| POST | `/api/v1/events/{id}/dead-letter` | `events:write` |

`GET /events/status`:

```json
{
  "mode": "In-process development bus (Kafka disabled by configuration)",
  "brokerConnected": false,
  "topicCount": 6,
  "eventsLastHour": 290,
  "deadLetterCount": 7,
  "totalLag": 42,
  "detail": "Kafka is disabled by configuration. Events are accepted by the in-process bus and flagged as simulated."
}
```

Publish: `{ "topic": "payments.events", "eventType": "payment.reconciliation.requested", "eventKey": "…", "payload": "{…}" }`.
The response includes `isSimulated` — `true` means the in-process fallback accepted it, never that
Kafka did.

## 7. Observability

| Method | Path | Query |
| --- | --- | --- |
| GET | `/api/v1/observability/overview` | `hours` (1–168) |
| GET | `/api/v1/observability/traffic` | `hours` |
| GET | `/api/v1/observability/latency` | `hours` |
| GET | `/api/v1/observability/errors` | `hours`, `limit` |
| GET | `/api/v1/observability/traces` | `limit` |
| GET | `/api/v1/observability/logs` | `limit`, `level` |
| GET | `/api/v1/observability/alerts` | — |
| GET | `/api/v1/observability/services` | — |

Overview returns request volume, average and p95 latency, 5xx error rate, availability, services up
and down, dependencies and uptime. Everything is computed from `ApiRequestLogs` and live probes.

## 8. Incidents

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/incidents?search=&severity=&status=&openOnly=&page=` | `incidents:read` |
| GET | `/api/v1/incidents/stats` | `incidents:read` |
| GET | `/api/v1/incidents/{id}` | `incidents:read` |
| POST | `/api/v1/incidents` | `incidents:write` |
| PATCH | `/api/v1/incidents/{id}` | `incidents:write` |
| POST | `/api/v1/incidents/{id}/notes` | `incidents:write` |

Create: `{ title, description, severity, category, team, affectedService?, impactSummary? }`
(severity `Sev1`–`Sev4`; category `Integration|Payment|Security|Performance|DataQuality|Infrastructure`).

Update (any subset): `{ status, severity, team, assignee, rootCause, correctiveActions, note }`.
Valid transitions are `Open → Acknowledged → Investigating → Mitigated → Resolved`; each writes an
activity entry. Notes: `{ "detail": "Correlated with the 14:20 routing deployment." }`.

## 9. Security administration

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/security/users?search=&role=&page=` | `security:read` |
| POST | `/api/v1/security/users` | `security:write` |
| PUT | `/api/v1/security/users/{id}` | `security:write` |
| GET | `/api/v1/security/roles` | `security:read` |
| GET | `/api/v1/security/permissions` | `security:read` |
| GET | `/api/v1/security/events?limit=` | `security:read` |
| GET | `/api/v1/security/audit-logs?search=&outcome=&page=` | `audit:read` |
| GET | `/api/v1/security/access-history?days=` | `audit:read` |

Create user: `{ email, displayName, password, roleSlug }` — password ≥ 12 characters with upper,
lower and digit. Update: `{ displayName, roleSlug, isActive, newPassword? }`. Passwords are hashed
before persistence and never returned.

## 10. System

| Method | Path | Auth |
| --- | --- | --- |
| GET | `/health` | anonymous |
| GET | `/health/live` | anonymous |
| GET | `/metrics` | anonymous (Prometheus text) |
| GET | `/api/v1/system/health` | `health:read` |
| GET | `/api/v1/system/metrics` | `health:read` |

`/health` is intentionally compact:

```json
{
  "status": "degraded",
  "database": "connected",
  "cache": "In-process memory cache (Redis unreachable)",
  "eventBus": "In-process development bus (Kafka disabled by configuration)",
  "availabilityPct": 98.45,
  "uptimeSeconds": 27,
  "checkedAt": "2026-11-13T18:23:12.5+00:00"
}
```

`/api/v1/system/health` adds every component with latency and mode, worker heartbeats, recent
failures and dead-letter depth.

## 11. Explorer

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/explorer/catalogue` | `explorer:execute` |
| POST | `/api/v1/explorer/execute` | `explorer:execute` (rate-limited) |

Execute: `{ "method": "GET", "path": "/api/v1/integrations?page=1&pageSize=5", "body": null, "idempotencyKey": null }`.

The response mirrors the inner call: `statusCode`, `reasonPhrase`, `durationMs`, `body`,
`bodyIsJson`, `sizeBytes`, `headers`, `error`, `executedAt`. The call runs with **your** identity and
permissions against this API; only catalogue paths are executable.

## 12. Core banking

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/v1/customers?search=&page=` | `payments:read` |
| GET | `/api/v1/accounts?search=&page=` | `payments:read` |

Used by the explorer and payment screens to resolve synthetic debtor accounts. Account numbers are
masked in list views (`12-••••-1313`).
