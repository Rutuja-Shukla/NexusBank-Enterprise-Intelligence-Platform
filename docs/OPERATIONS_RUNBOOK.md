# Operations runbook

Procedures for the failures this platform can actually produce. Every command targets the real
deployment shape: **Vercel** (static console) → **Render** (ASP.NET Core API, SQLite, Redis and Kafka
disabled) or the **Docker Compose** stack (SQL Server + Redis + Kafka + API + console).

Conventions used below:

```bash
API=https://nexusbank-api-w53j.onrender.com     # hosted backend
CONSOLE=https://nexus-bank-sand.vercel.app      # hosted frontend
LOCAL=http://localhost:8080                     # Compose / dotnet run
```

Sign-in for diagnostics (demo credentials — synthetic data only):

```bash
TOKEN=$(curl -s -X POST $API/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@nexusbank.dev","password":"NexusBank!2026"}' | jq -r .accessToken)
```

---

## 1. Backend API unavailable

**Symptoms.** Console shows network errors and "Unable to reach the platform"; `curl $API/health/live`
times out or returns 5xx; Render shows the service as down.

**Possible causes.**

| Cause | Discriminator |
| --- | --- |
| Free-instance cold start (idle spin-down) | First request slow (tens of seconds), later ones fast |
| Process crash on startup (bad config) | Render logs end with an exception; `/health/live` never answers |
| Missing `Jwt__SigningKey` in a non-Development environment | Log: *"Jwt:SigningKey is not configured…"* — startup is refused by design |
| Platform incident at Render | Status page; other services affected |

**Diagnose.**

```bash
curl -s -o /dev/null -w 'live=%{http_code} total=%{time_total}s\n' $API/health/live
curl -s $API/health
```

Read Render → service → **Logs** for the first error after the last deploy, not the last line.

**Recover.**

1. Cold start: request `/health/live` twice; the second should be fast. Nothing to fix.
2. Startup failure: correct the environment variable (Render → Environment) and redeploy. Confirm the
   process is listening: Render logs contain `Now listening on: http://0.0.0.0:10000`.
3. Bad release: Render → **Deploys** → roll back to the last good deploy.
4. Nothing recoverable: redeploy the last commit. The SQLite database is recreated and reseeded.

**Validate.** `/health/live` returns `{"status":"live"}`; `/health` returns `healthy` or an explicitly
explained `degraded`; a login succeeds.

---

## 2. Frontend cannot reach the backend

**Symptoms.** The console loads (HTML/CSS/JS fine) but every data request fails; sign-in shows "Unable
to reach the platform". Browser network tab shows requests to `https://<console>/api/...` failing.

**Possible causes.**

| Cause | Discriminator |
| --- | --- |
| API down | Section 1 reproduces |
| Vercel rewrite missing or pointing at a dead host | `/api/v1/auth/me` returns Vercel's 404/502 HTML, not a problem-details JSON |
| CORS rejection (only if the console calls the API origin directly) | Browser console: "blocked by CORS policy"; API responds 200 to curl |
| Deep-link 404, not an API problem | The *page* 404s, not the fetch |

**Diagnose.**

```bash
# Does the proxy work at all?
curl -s -o /dev/null -w '%{http_code}\n' $CONSOLE/api/v1/auth/me        # expect 401, not 404/502

# Confirm the rewrite target resolves
cat frontend/vercel.json
curl -s -o /dev/null -w '%{http_code}\n' $API/health/live
```

**Recover.**

1. Rewrite target wrong → update `frontend/vercel.json` (`/api/:path*` → the live Render origin) and
   redeploy the Vercel project. `vercel.json` changes only take effect on a new deployment.
2. CORS → add the console origin to `Cors__AllowedOrigins__n` on the API and restart. The hosted API
   already allows `https://nexus-bank-sand.vercel.app` (verified in the response's
   `Access-Control-Allow-Origin`).
3. Page-level 404 on `/login` or any in-app route → the SPA fallback rewrite is missing; see
   `docs/DEPLOYMENT.md` §2.4.

**Validate.** Sign in through the browser; the dashboard renders live values; no console errors.

---

## 3. Database connection failures

**Symptoms.** `/health` reports `"database": "unavailable"` and status `unhealthy`; every endpoint
returns 500 with a correlation id; logs show EF Core connection exceptions.

**Diagnose.**

```bash
curl -s $API/health
# hosted: the process may still be up with a broken database — that is by design
```

Check the provider actually in use: `GET /api/v1/system/health` → `components[type=Database].mode`
should read `Microsoft.EntityFrameworkCore.Sqlite` (hosted) or `…SqlServer`.

Compose:

```bash
docker compose ps
docker compose logs sqlserver | tail -40
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1"
```

**Possible causes.** SQL Server still starting (healthcheck not green); wrong
`ConnectionStrings__SqlServer`; weak `MSSQL_SA_PASSWORD` rejected by SQL Server policy; container out of
memory; SQLite file unwritable or locked.

**Recover.**

1. Compose: wait for `sqlserver` to report healthy (`docker compose ps`), then restart just the API.
2. Wrong connection string: fix the environment variable, `docker compose up -d api`.
3. Corrupt/unwritable SQLite (hosted): redeploy — the database is recreated and reseeded.
4. Password policy rejection: set a strong `MSSQL_SA_PASSWORD` (upper, lower, digit, symbol, 8+) and
   recreate the SQL Server container.

**Validate.** `/health` shows `database: connected`; `/api/v1/dashboard` returns 200 with a token;
`/api/v1/system/health` database latency is small.

---

## 4. Redis unavailable

**Symptoms.** `/health` → `"cache": "In-process memory cache (Redis unreachable)"`; status `degraded`;
alert `cache-fallback` (info) in `/api/v1/observability/alerts`; the health page shows Redis `DEGRADED`.

**This is expected in the current Render deployment** (`Redis__Enabled` is off / no Redis instance) and
in `dotnet run` (Development sets `Redis:Enabled=false`). Do not treat it as an incident there.

**Diagnose (Compose, where Redis is expected).**

```bash
docker compose ps redis
docker compose exec redis redis-cli ping        # expect PONG
docker compose exec redis redis-cli info memory | head -5
docker compose logs redis | tail -20
```

**Recover.**

1. Restart the container: `docker compose restart redis`.
2. Confirm the API's `Redis__Configuration` matches the service (`redis:6379` in Compose).
3. If Redis is intentionally absent, set `Redis__Enabled=false` so the reported mode is unambiguous
   rather than a connection attempt.
4. Nothing to restore: the cache holds no authoritative data.

**Validate.** `/health` `cache` reads `Redis (redis:6379)`; Redis component is `healthy`.

**Note on impact.** Redis is not currently used by any business read path (see
`docs/PERFORMANCE.md` §5) — so its absence does not slow the platform, it only changes the reported
status and removes cross-instance cache sharing.

---

## 5. Kafka unavailable

**Symptoms.** `/health` `eventBus` reads *"…Kafka unreachable at localhost:9092"* or *"…disabled by
configuration"*; event publishing returns `isSimulated: true`; alert `Kafka broker unreachable` (info).

**Expected in the hosted deployment** (disabled for cost) and locally unless Compose is running.

**Diagnose (Compose).**

```bash
docker compose ps kafka
docker compose logs kafka | tail -40        # ~30 s to elect the KRaft controller
docker compose exec kafka kafka-topics.sh --bootstrap-server localhost:9092 --list
```

**Recover.**

1. Kafka is slow to start: wait for the healthcheck, then restart the API so it re-probes.
2. Broker intentionally absent: keep `Kafka__Enabled=false`. The outbox still records every event and
   the in-process bus still accepts publishes, flagged as simulated.
3. Broker present but the API cannot see it: verify `Kafka__BootstrapServers` (`kafka:9092` inside the
   Compose network, not `localhost`).

**Validate.** `/api/v1/events/status` → `brokerConnected: true`, `isFallback: false`; a publish returns
`isSimulated: false` with partition and offset.

---

## 6. Kafka consumer lag

**Symptoms.** `GET /api/v1/events/consumer-groups` shows rising `lag`; the event stream header shows
total lag; the Kafka consumer heartbeat in `/api/v1/system/health` is `degraded`/`down`.

**Diagnose.**

```bash
curl -s "$API/api/v1/events/consumer-groups" -H "Authorization: Bearer $TOKEN" | jq '.[] | {groupId,topic,lag,state}'
curl -s "$API/api/v1/system/health" -H "Authorization: Bearer $TOKEN" | jq '.workers[] | select(.name=="Kafka consumer")'
```

Check whether consumers are alive (`state`, `lastHeartbeatAt`) and whether the *producer* side is
spiking (`/api/v1/events?page=1` volume).

**Recover.**

1. Consumer stopped: restart the API (the consumer is a hosted service inside it).
2. Consumer erroring: read the heartbeat detail (`consumer unavailable: …`) and the logs; a broker
   `ConsumeException` is usually a topic/permission mismatch.
3. Genuine backlog: scale consumer capacity (currently one consumer per API instance) or reduce publish
   rate. There is no autoscaling.
4. Remember: `ConsumerGroups.lag` in this build is platform-maintained data, not broker truth — confirm
   with `kafka-consumer-groups.sh --describe` before declaring victory.

**Validate.** Lag decreases across two readings; `lastHeartbeatAt` is recent; the worker shows `healthy`.

---

## 7. Dead-letter queue messages

**Symptoms.** Alert *"N message(s) in the dead-letter queue"*; `deadLetterCount > 0` in
`/api/v1/events/status`; the Events screen's dead-letters tab is non-empty.

**Diagnose.**

```bash
curl -s "$API/api/v1/events?deadLettersOnly=true&pageSize=50" -H "Authorization: Bearer $TOKEN" \
  | jq '.items[] | {id,eventType,errorMessage,retryCount,producedAt}'
```

Group by `errorMessage` — one poison payload pattern is a code defect; many distinct errors is usually a
downstream/broker problem.

**Recover.**

1. Fix the cause (consumer defect, schema change, downstream outage).
2. Replay one event: `POST /api/v1/events/{id}/retry`. On success the status becomes `Retried` and the
   group's lag decrements.
3. Replay is per event; there is no bulk replay. For a large set, script the calls and audit each one.
4. Park an event deliberately: `POST /api/v1/events/{id}/dead-letter` with `{ "reason": "…" }`.

**Validate.** `deadLetterCount` returns to 0; the replayed event shows `status: Retried` with
`errorMessage: null`; no new dead letters appear over the next publish cycle.

---

## 8. Failed payment processing

**Symptoms.** A payment shows `Failed` with `failureCode` `PROCESSOR_REJECTED` or `GATEWAY_TIMEOUT`;
success rate drops on the dashboard; the journey timeline stops at a red step.

**Diagnose.**

```bash
curl -s "$API/api/v1/payments/$PAYMENT_ID" -H "Authorization: Bearer $TOKEN" \
  | jq '{status,failureCode,failureReason,journey:[.journey[]|{sequence,step,status,errorDetail}],ledgerBalanced}'
```

Important: a **failed** payment is a valid business outcome. It is persisted with `Failed`, a code, a
reason and **no ledger postings** — that is correct behaviour, verified by
`PaymentServiceTests`. Do not "fix" it by editing rows.

Common causes: `simulateFailure` was set on the request (synthetic failure requested); the instruction
routed to the external gateway (a `creditorBic` was supplied) and the fixture gateway is `Down`;
insufficient funds or currency mismatch (these return `400` at creation, not a `Failed` payment).

**Recover.**

1. Simulated failure in a demo: nothing to repair; create a new instruction without the flag.
2. Real routing failure: correct the beneficiary details and re-issue. A failed payment cannot be
   refunded or reversed (both require `Completed`) — a new instruction is the correct action.
3. Genuine processing defect: raise an incident, attach the correlation id, and follow §12 for the
   code path.

**Validate.** A new payment completes: `status: Completed`, ≥5 journey steps, `ledgerBalanced: true`.

---

## 9. Duplicate payment requests

**Symptoms.** A client times out, retries, and either gets the same payment back (`replayed: true`) or a
`409` with `code: idempotency_key_reuse`; operations report "the same payment twice".

**Diagnose.**

```bash
# Same key, same payload → replay (expected)
curl -s -X POST $API/api/v1/payments -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: <key>' -d @payment.json | jq '{id,replayed}'

# Was the key reused with a different body?
curl -s -X POST $API/api/v1/payments -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: <key>' -d @different.json | jq '.code'
```

Check for genuine duplicates (two `Payments` rows with the same business content and no shared key):

```sql
SELECT CreditorAccountNumber, Amount, Currency, CreatedAt, Reference, IdempotencyKey
FROM Payments
WHERE CreatedAt >= DATEADD(hour, -24, SYSUTCDATETIME())
ORDER BY CreatedAt DESC;
```

**Interpretation.**

| Observation | Meaning | Action |
| --- | --- | --- |
| Same `id`, `replayed: true` | Idempotency working as designed | None |
| `409 idempotency_key_reuse` | Client reused a key with a different payload | Client defect — generate a new key per instruction |
| Two rows, both with empty `IdempotencyKey` | The client did not send the header, so no protection applied | Fix the client; then reverse the duplicate (§9.1) |
| Two rows, different keys, same content | Genuine double submission | Reverse one |

**9.1 Correcting a confirmed duplicate.** Reverse the second payment:
`POST /api/v1/payments/{id}/reverse` with `{ "reason": "Duplicate of PAY-…" }`. This posts the reversing
ledger entries, restores the debtor balance and emits `payment.reversed`. Reversal requires
`payments:write` and is audited.

**Validate.** Exactly one `Completed` payment remains for the instruction; the reversed one shows
`Reversed` with a `Reversal` journey step; ledger totals still balance (`docs/DISASTER_RECOVERY.md` §11).

---

## 10. Authentication failures

**Symptoms.** Sign-in fails; 401 on every call; users are suddenly signed out.

**Distinguish the 401 by `code`/`detail`:**

| Response | Cause | Fix |
| --- | --- | --- |
| `401` generic (*"Invalid email or password"* semantics) | Wrong credentials, or unknown account (deliberately identical) | Correct the credentials |
| `code: account_locked` | 5 failed attempts → 15-minute lockout | Wait, or have an administrator clear the lockout |
| `code: account_disabled` | `IsActive = false` | Administrator re-enables the user |
| `401 unauthorised` on every call with a token | Token expired (30 min), signature/issuer/audience mismatch, or the signing key changed | Sign in again; if it recurs immediately, check `Jwt__SigningKey` |
| `429 rate_limited` | >12 auth requests/minute from one IP | Wait one minute; see the caveat below |
| All users signed out simultaneously | JWT signing key rotated, or the database was reseeded (new user ids) | Expected after either; sign in again |

**Diagnose.**

```bash
# Was it a rate limit rather than a bad password?
curl -s -i -X POST $API/api/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"ops@nexusbank.dev","password":"NexusBank!2026"}' | head -20

# What does the API say about the caller's token?
curl -s $API/api/v1/auth/me -H "Authorization: Bearer $TOKEN" | jq
```

Server-side, JWT rejections are logged with a reason — `token_expired`, `invalid_signature`,
`invalid_issuer`, `invalid_audience`, `malformed_token` — without logging the token itself. Check the
platform logs for `JWT authentication failed`.

**Unlock an account** (administrator, API):

```bash
curl -s -X PUT $API/api/v1/security/users/<userId> -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' -d '{"displayName":"Daniel Whitfield","roleSlug":"operations-engineer","isActive":true}'
```

That re-enables the account; the lockout itself is time-based (15 minutes) and clears on the next
successful sign-in after it expires.

**Caveat that matters in the hosted deployment.** Rate limiting partitions on
`HttpContext.Connection.RemoteIpAddress`. Behind the Vercel proxy that is a proxy address, not the end
user, so the 12/min auth budget is shared by whoever arrives through the same egress address. If a
legitimate user is rate-limited, that is why — the durable fix is trusted forwarded-header handling
(`docs/SECURITY.md` §3), not raising the limit.

**Validate.** `POST /auth/login` returns a token; `GET /auth/me` returns the profile with permissions;
the console lands on the dashboard.

---

## 11. High API latency

**Symptoms.** Dashboard tiles slow; `/api/v1/observability/overview` p95 rises; users report the
console "freezing".

**Diagnose.**

```bash
curl -s $API/metrics | grep -E 'requests_last_hour|errors_last_hour|latency_avg_ms'
curl -s "$API/api/v1/observability/latency?hours=1" -H "Authorization: Bearer $TOKEN" | jq
curl -s "$API/api/v1/observability/errors?hours=1&limit=10" -H "Authorization: Bearer $TOKEN" | jq
```

Look at the distribution, not the mean: a fat `>1 s` band with a normal median is a few slow endpoints
(often observability aggregates); a shift across all bands is the platform (database, cold start,
CPU).

**Common causes in this build.**

| Cause | Signature |
| --- | --- |
| Render cold start | First requests after idle are multi-second, then normal |
| SQLite writer contention | Latency spikes on writes (payment create) while reads stay flat |
| Observability aggregation over a big window | `/observability/*` slow, everything else fine |
| Unanchored search (`LIKE '%term%'`) | `/payments?search=…` and `/events?search=…` slow with large tables |
| Telemetry table growth | All endpoints drift up; `ApiRequestLogs` row count large |
| BCrypt login burst | `/auth/login` slow only; rate limit usually triggers first |

**Recover.**

1. Cold start: none needed.
2. Sustained: reduce pressure — close idle console tabs (each polls every 25 s), shorten observability
   windows, avoid wide searches.
3. Confirm retention is running: `/api/v1/system/health` → `Telemetry retention` heartbeat detail lists
   purged rows.
4. Structural fixes are in `docs/PERFORMANCE.md` §11 (cache dashboard/observability, batch telemetry,
   rollups).

**Validate.** `/metrics` `latency_avg_ms` falls; p95 band shifts left; the specific slow endpoint is
identifiable rather than "everything".

---

## 12. Background worker failures

**Symptoms.** `/api/v1/system/health` → `workers[]` with `status: degraded`/`down`, or *"No worker
heartbeat has been recorded yet"*; events stop flowing (outbox), integration statuses go stale, or
telemetry stops being trimmed.

**Diagnose.**

```bash
curl -s "$API/api/v1/system/health" -H "Authorization: Bearer $TOKEN" \
  | jq '.workers[] | {name,status,detail}'
```

| Worker | Healthy detail looks like | Stale means |
| --- | --- | --- |
| Outbox publisher | `N message(s) relayed…` every ~5 s | Events are not reaching the broker/fallback; `OutboxMessages.ProcessedAt` stops advancing |
| Integration health monitor | `8 integration(s) probed` every ~60 s | Integration statuses on the dashboard are frozen (they are last-probe values) |
| Telemetry retention | `purged N request log(s)…` every 6 h | `ApiRequestLogs` grows unbounded |
| Kafka consumer | `subscribed to N topic(s)` / `last consumed …` | Lag grows (§6) |

Status thresholds: `down` after 3 consecutive failures; `degraded` when the last heartbeat is older than
180 s.

**Recover.**

1. One worker failing: read its heartbeat detail — it contains the exception message from the failed
   cycle.
2. Database-related failures across all workers: fix the database (§3); workers recover on their next
   cycle without a restart.
3. Worker wedged: restart the API (all workers are hosted services inside it).
4. Remember the restart caveat: after a restart, heartbeats are empty for one cycle, so `degraded` is
   expected briefly and is not a fault.

**Validate.** Each worker reports `healthy` with a recent `last cycle`; outbox pending count is 0 or
falling; integration `UpdatedAt` timestamps advance.

---

## 13. Escalation notes

- Every error response carries `X-Correlation-ID`; ask the reporter for it before investigating.
- Never paste access or refresh tokens, `Jwt__SigningKey`, `MSSQL_SA_PASSWORD` or a full connection
  string into a ticket, chat or log. Correlation ids and payment references are the safe handles.
- The platform holds synthetic data only. There is no customer PII to protect in this build, but the
  discipline (no secrets in tickets, no bodies in logs) is the same one a real deployment needs.
