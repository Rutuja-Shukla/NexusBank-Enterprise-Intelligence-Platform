# Disaster recovery and business continuity

This document describes how the platform can be recovered after failure, what would actually be lost,
and which recovery procedures have been **exercised** versus which are **written but untested**.

> **Read this first.** Recovery objectives (RPO/RTO) below are *derived from the current
> configuration*, not commitments. No backup schedule is configured for the hosted deployment, no
> restore has been rehearsed, and no recovery test has been performed. `§9 Recovery Validation
> Evidence` is intentionally empty.

---

## 1. What state exists, and where

| State | Store | Hosted (Vercel + Render) | Compose (local) | Recoverable today? |
| --- | --- | --- | --- | --- |
| Users, roles, permissions | Database | SQLite file on the Render instance | `sqlserver-data` volume | No backup in hosted; volume persists in Compose |
| Payments, journey, ledger | Database | same | same | No backup in hosted |
| Audit log (append-only) | Database | same | same | No backup in hosted |
| Outbox / idempotency records | Database | same | same | No backup in hosted |
| Request telemetry (`ApiRequestLogs`) | Database | same | same | Regenerated; retained 7 days |
| Kafka events (`KafkaEvents`) | Database **and** broker | Database only (broker disabled) | Database + `kafka-data` volume | Events exist in the DB even without a broker |
| Cache | Redis / memory | Not in use (memory) | `redis-data` volume (AOF) | Disposable by design |
| JWT signing key | Render env var | Render dashboard | `.env` | Yes — operator-held secret; **must be preserved** |
| Integration credentials | `secret://` references only | none provisioned | none provisioned | Nothing sensitive stored in the DB |

The critical asymmetry: **the database holds every business record, and the hosted deployment has no
backup of it.** Everything else is either derivable, disposable, or held by the platform.

---

## 2. Recovery objectives

| Scenario | RPO (data loss window) | RTO (time to serve) | Basis |
| --- | --- | --- | --- |
| Render instance restart / redeploy | **Everything since the instance started** — the SQLite file is on an ephemeral filesystem and is recreated on boot | 1–3 minutes (cold start + `EnsureCreated` + seed) | Verified behaviour of the free tier; seed is deterministic |
| Render platform incident (region) | Same — no replica, no backup | Until Render restores service, then as above | No redundancy provisioned |
| Compose stack restart (not `down -v`) | 0 — named volumes persist | Seconds to ~1 minute | Compose configuration |
| Compose `down -v` | All data (volumes deleted) | Full reseed on next start | Destructive by command, not by failure |
| Vercel frontend failure | 0 — stateless static assets | Instant rollback to previous deployment | Vercel deployment history |
| Managed SQL Server target (Compose/Azure) | Depends on backup policy configured by the operator | Depends on restore size | **Not configured in this repository** |

These are the honest numbers for a portfolio deployment: **RPO is effectively "since last boot"** and
**RTO is "one cold start"**. Do not present this build as having a continuity capability it does not
have.

---

## 3. Database backup strategy

### 3.1 Current hosted position (Render)

- Provider: **SQLite**, file database, no persistent disk attached.
- Backups: **none configured**.
- Consequence: a redeploy, restart, or platform migration resets the estate to the seeded synthetic
  data. Because the data is synthetic and deterministic, this is acceptable for the demo — but it means
  any state created during a session (payments, incidents, notes) is not durable.

If durable demo data were required, the minimum change is to attach a Render **persistent disk** and
point `ConnectionStrings__Sqlite` at it, or move to a managed database provider. Neither is done here.

### 3.2 Docker Compose (SQL Server)

Back up to the container's filesystem, then copy out:

```bash
# Logical backup (safe while running)
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "BACKUP DATABASE [NexusBank] TO DISK = N'/var/opt/mssql/backup/NexusBank-full.bak' WITH INIT, COMPRESSION, CHECKSUM"

docker compose cp sqlserver:/var/opt/mssql/backup/NexusBank-full.bak ./backup/NexusBank-full.bak
```

Because `/var/opt/mssql` is the mounted `sqlserver-data` volume, the `.bak` also survives container
restarts until the volume is removed. Copy it outside the volume anyway — a backup living only on the
disk it protects is not a backup.

Retention, encryption and offsite copying are **operator policy**, not implemented here. In a real
deployment: short-term full + differential backups, a transaction-log backup cadence that satisfies the
business RPO, `WITH ENCRYPTION` using a certificate stored separately, and copies in a second region.

### 3.3 Azure SQL (if the Azure target is used)

Azure SQL Database provides automated backups and point-in-time restore by default. Configure:

- backup storage redundancy (`Local` for dev, `Geo` for production),
- a long-term retention policy if regulation requires it,
- an explicit restore rehearsal — an untested backup is a hypothesis.

`docs/DEPLOYMENT.md` §7 describes the Azure target; **nothing is deployed on Azure.**

---

## 4. Restoration

### 4.1 Restore SQL Server from a backup (Compose)

```bash
# 1. Stop the API so nothing writes during restore
docker compose stop api

# 2. Restore with REPLACE (this is destructive — confirm the file name)
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE DATABASE [NexusBank] FROM DISK = N'/var/opt/mssql/backup/NexusBank-full.bak' WITH REPLACE, RECOVERY"

# 3. Start the API and verify
docker compose start api
curl -s http://localhost:8080/health
```

### 4.2 Rebuild a fresh database from migrations

Preferred when the data is synthetic and a clean state is acceptable:

```bash
cd backend
export PATH="$PATH:$HOME/.dotnet/tools"

# generate an idempotent, reviewable script (this is what CI verifies)
dotnet ef migrations script --idempotent \
  --project NexusBank.Infrastructure --startup-project NexusBank.Api -o migration.sql
```

Then either run `Database__AutoMigrate=true` on a single starting instance, or have a DBA execute
`migration.sql` through the release process. `Database__AutoSeed=true` repopulates the synthetic estate
when the database is empty.

**Never** "recover" a shared environment by deleting the database and letting auto-migrate recreate it.
That pattern is for local development only.

### 4.3 Restore the hosted deployment to a known-good state

Because the hosted database is ephemeral, recovery is a redeploy, not a restore:

1. Render → the `nexusbank-api` service → **Deploys** → roll back to the last deploy known to be good.
2. Wait for the service to report healthy: `curl -s <api>/health/live`.
3. The service re-creates and reseeds its SQLite database on boot (`Database__AutoMigrate`,
   `Database__AutoSeed` default to `true`).
4. Sign in and confirm the console renders; expect the seeded estate, not the previous session's data.

---

## 5. Application recovery

| Failure | Detection | Recovery |
| --- | --- | --- |
| API process crash | `/health/live` fails; Render restarts the service automatically | Verify `/health`; confirm startup log has no `Database initialisation failed` |
| Bad configuration pushed | Container exits on start (missing `Jwt__SigningKey` outside Development throws by design) | Fix the environment variable and redeploy; see `Program.cs` startup guard |
| Bad application release | `/health` errors, 5xx rate rises | Roll back the Render deploy (or Vercel deployment for the console) |
| Frontend broken release | Console shows an error page or fails to boot | Vercel → Deployments → roll back to the previous production deployment (instant, no rebuild) |
| Frontend cannot reach API | Console shows network errors; `/api/*` returns 5xx | Check the rewrite target in `frontend/vercel.json` and the Render service health |

The static console is stateless: rolling it back never touches data.

---

## 6. Redis recovery

- **Role:** cache only. No business record exists solely in Redis.
- **Hosted:** not in use (`Redis__Enabled` is off / unreachable); in-process memory cache serves reads.
- **Compose:** Redis 7 with `appendonly yes`, `maxmemory 256mb`, `allkeys-lru`, volume `redis-data`.
- **Loss impact:** none on correctness. Warm-up cost only.
- **Recovery:** restart the container. With AOF, recently written keys are replayed; with
  `allkeys-lru` and a bounded memory budget, eviction is normal operation, not data loss.

There is no Redis backup procedure in this repository because there is nothing worth backing up. If a
future change caches authoritative state, this section must be rewritten.

---

## 7. Kafka recovery

- **Hosted:** disabled by configuration. Events published through `IEventBus` are accepted by the
  in-process fallback and flagged `isSimulated = true`.
- **Compose:** single-node `bitnami/kafka:3.7` in KRaft mode, 3 partitions, auto-topic-creation, volume
  `kafka-data`.
- **Durability today:** every published event is *also* written to the `KafkaEvents` table with its
  payload, headers, partition, offset and correlation id. That row set — not the broker — is the
  platform's event record.

**Broker loss / volume reset:**

1. Restart Kafka; the API reconnects automatically (`KafkaEventBus` re-probes every 30 s after a
   failure).
2. Events already persisted in `KafkaEvents` remain visible in the event stream workspace.
3. Replay is manual and per event: `POST /api/v1/events/{id}/retry` re-publishes the stored payload.
   This is the documented replay path and works with or without a broker.

**What is genuinely lost with the memory fallback:** nothing that was persisted, because the outbox and
`KafkaEvents` are in the database. What is lost is *broker-level* guarantees — no cross-instance fan-out,
no consumer-group rebalancing, no real retention policy.

---

## 8. Failure scenarios and expected behaviour

| Scenario | Expected behaviour | What to check | Known weakness |
| --- | --- | --- | --- |
| Database unreachable | Host still starts; `/health` reports `unhealthy` (database down); requests return 500 with correlation id | Render logs for `Database initialisation failed` | Requests still reach the DB layer and fail there — no circuit breaker in front of the database |
| Redis unreachable | Falls back to in-process memory; `/health` `degraded`; alert `cache-fallback` (info) | `/health` `cache` string | Cache state not shared across instances (moot today: single instance) |
| Kafka unreachable | Publishing accepted and flagged simulated; outbox retries with backoff | `/api/v1/events/status`, outbox `Attempts` | Events deliver late; 5 s poll ceiling |
| Kafka consumer down | Lag grows in `ConsumerGroups`; worker heartbeat `degraded`/`down` | `/api/v1/system/health` workers | Lag is platform data, not broker truth (`OBSERVABILITY.md` §10) |
| Outbox backlog | Payments complete but events lag | `OutboxMessages` where `ProcessedAt IS NULL` | No alert rule on outbox depth yet |
| JWT signing key rotated | All access and refresh tokens invalidated → users signed out | 401 `invalid_signature` in logs | Rotation is a full sign-out; no key ring / overlapping keys |
| Accidental `docker compose down -v` | Volumes deleted; next start reseeds | `docker volume ls` | Unrecoverable; no backup unless one was taken |
| Disk full in container | SQLite write failures; 500s | container logs, `df -h` | No disk-space alerting |

---

## 9. Recovery Validation Evidence

**No recovery test has been executed.** This table exists so that the first real rehearsal is recorded
with enough detail to be credible, rather than asserted.

| Date | Scenario | Environment | Backup/artefact used | Data loss observed | Time to serve | Restored by | Result |
| --- | --- | --- | --- | --- | --- | --- | --- |
| — | *not performed* | — | — | — | — | — | — |

Until this table has rows, treat every recovery claim in this document as **designed, not proven**.

---

## 10. Recovery checklist (practical, ordered)

**During an incident**

1. Confirm blast radius: `curl -s <api>/health/live` then `<api>/health`.
2. Capture evidence before changing anything: platform logs for the last 30 minutes, the failing
   correlation id, `/api/v1/observability/errors?hours=1`, `/metrics`.
3. Identify the last known-good deploy (Render → Deploys; Vercel → Deployments).
4. Decide: roll back code, or restore data. Do not do both at once — you will not know which fixed it.
5. Roll back code first when the failure started at a deploy.
6. Restore data only when corruption or deletion is proven, and only from a verified artefact.
7. Communicate state: what is degraded, what is being done, when the next update is due.

**After service is restored**

8. Verify: `/health` status, sign-in, one payment create + idempotent replay, one event publish.
9. Verify financial integrity (§11) — do not close an incident touching payments without it.
10. Rotate any credential that may have been exposed.
11. Record the timeline, root cause and corrective actions in the Incidents workspace, and add a row to
    §9 if recovery procedures were exercised.

---

## 11. Data integrity verification

Run these after any restore, migration or payment-path incident. They mirror the invariants documented
in `docs/DATABASE.md` §4. `LedgerEntries.EntryType` is stored as an integer enum (`Debit = 0`,
`Credit = 1`).

**1. Every payment's ledger must balance**

```sql
SELECT p.Reference,
       SUM(CASE WHEN l.EntryType = 0 THEN l.Amount ELSE 0 END) AS Debits,
       SUM(CASE WHEN l.EntryType = 1 THEN l.Amount ELSE 0 END) AS Credits
FROM Payments p
JOIN LedgerEntries l ON l.PaymentId = p.Id
GROUP BY p.Reference
HAVING SUM(CASE WHEN l.EntryType = 0 THEN l.Amount ELSE 0 END)
    <> SUM(CASE WHEN l.EntryType = 1 THEN l.Amount ELSE 0 END);
-- Expect zero rows.
```

**2. Completed payments must have postings; failed payments must not**

```sql
SELECT p.Reference, p.Status, COUNT(l.Id) AS Postings
FROM Payments p
LEFT JOIN LedgerEntries l ON l.PaymentId = p.Id
WHERE (p.Status = 4 AND l.Id IS NULL)      -- Completed (enum ordinal) with no postings
   OR (p.Status = 5 AND l.Id IS NOT NULL)  -- Failed with postings
GROUP BY p.Reference, p.Status;
-- Expect zero rows.
```

`PaymentStatus` ordinals for reference: `Initiated = 0`, `Validating = 1`, `Processing = 2`,
`Pending = 3`, `Completed = 4`, `Failed = 5`, `Reversed = 6`. Re-verify against
`NexusBank.Domain/Enums/DomainEnums.cs` before relying on these in a live environment — enum order is
part of the persisted contract and must not be reordered.

**3. No duplicate idempotency records**

```sql
SELECT Scope, [Key], COUNT(*) FROM IdempotencyRecords
GROUP BY Scope, [Key] HAVING COUNT(*) > 1;
-- Expect zero rows (a unique index enforces this; a non-zero result means the index is missing).
```

**4. Outbox backlog**

```sql
SELECT COUNT(*) AS Pending, MIN(OccurredAt) AS OldestPending
FROM OutboxMessages WHERE ProcessedAt IS NULL;
```

**5. Accounts not negative** (overdraft is not modelled)

```sql
SELECT AccountNumber, Balance, AvailableBalance FROM Accounts
WHERE Balance < 0 OR AvailableBalance < 0;
-- Expect zero rows.
```

Application-level equivalents: `GET /api/v1/payments/{id}` exposes `ledgerBalanced`, and the payment
unit/integration suites assert balanced postings on every create.

---

## 12. Rollback procedures

| Layer | Rollback | Notes |
| --- | --- | --- |
| Frontend (Vercel) | Promote the previous deployment | Instant; static assets only |
| Backend (Render) | Roll back to the previous deploy | Cold start; ephemeral DB reseeds if it was lost |
| Container image | Redeploy the previous image tag | Prefer immutable tags/digests over `latest` |
| Database schema | `dotnet ef database update <PreviousMigration>` | Use expand/contract so the previous app version still works |
| Configuration | Revert the environment variable, redeploy | Missing `Jwt__SigningKey` fails startup by design |
| Cache | Flush Redis / restart the container | Safe: nothing authoritative is cached |

**Migration rule:** never run a destructive schema change in the same release as the code that depends
on it. Add the column, backfill, switch reads, then remove the old column a release later. This is what
makes the rollback row above actually usable.
