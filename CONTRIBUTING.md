# Contributing

This is a portfolio project, but it is maintained to the standard of a small production codebase:
boundaries are enforced, tests must pass, and documentation must match the code. These notes describe how
to work in it without breaking those properties.

---

## 1. Before you start

```bash
dotnet --version      # .NET 8 SDK (or 9, which still builds net8.0)
node --version        # 20+ (CI uses 22)
docker --version      # optional, for the full stack
```

Read `docs/ARCHITECTURE.md` §2 (dependency rules) and `docs/DECISIONS.md` before making a structural change.
If you are changing a decision, add or update an ADR rather than silently diverging from one.

---

## 2. Local workflow

### Fast loop (SQLite, no containers)

```bash
# terminal 1
cd backend
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8080 \
  dotnet run --project NexusBank.Api --no-launch-profile

# terminal 2
cd frontend
npm install
npm run dev
```

Or `./scripts/dev.sh` for both. The API creates `backend/NexusBank.Api/nexusbank-dev.db` and seeds it;
delete that file to reseed.

### Full stack (SQL Server + Redis + Kafka)

```bash
cp .env.example .env      # set MSSQL_SA_PASSWORD and JWT_SIGNING_KEY
docker compose up --build
```

Use this when your change touches Redis, Kafka or SQL Server-specific behaviour. The fast loop cannot
prove those paths (they run on the in-process fallbacks).

---

## 3. Before you open a pull request

```bash
# backend
cd backend
dotnet build NexusBank.sln -c Release
dotnet test tests/NexusBank.Tests
dotnet test tests/NexusBank.IntegrationTests

# frontend
cd frontend
npm run build          # tsc -b && vite build
npm test               # vitest run
```

Expected: **36** unit/service, **14** integration, **19** frontend tests pass. If you add or remove tests,
update the counts in `docs/TESTING.md` §1 and the README — the numbers in the documentation are treated as
claims, not decoration.

If you changed the EF model:

```bash
dotnet ef migrations add <DescriptiveName> --project NexusBank.Infrastructure --startup-project NexusBank.Api
dotnet ef migrations script --idempotent --project NexusBank.Infrastructure --startup-project NexusBank.Api -o /tmp/check.sql
test -s /tmp/check.sql
```

CI runs the same script check, so a model change without a migration fails the build.

---

## 4. Backend conventions

| Rule | Why |
| --- | --- |
| **No business logic in controllers.** Validate, delegate to a service, return a DTO | Controllers stay thin; services are unit-testable |
| **Never return entities.** Map to a DTO; mask identifiers with `Masking` | Prevents accidental field disclosure and lazy-loading surprises |
| **`AsNoTracking()` on read paths** | Avoids change-tracking overhead on queries that never write |
| **Money is `decimal`; round explicitly** | Never `double`/`float` for amounts |
| **One `SaveChanges` per use case** | Multi-table writes must be atomic (payment + journey + ledger + outbox + audit + idempotency) |
| **Authorise with a permission policy**, never a role string | `[Authorize(Policy = NexusPermissions.X)]`; the provider resolves any code |
| **Application must not reference** EF Core's provider, ASP.NET Core, Redis, Kafka or BCrypt | Boundaries are enforced by project references; keep them |
| **New background work = a `BackgroundService`** that records a heartbeat | Otherwise the health page reports it as missing |
| **Never log bodies, tokens, passwords or connection strings** | Log correlation ids, references, status codes and durations |
| **Provider-portable LINQ** | The same model runs on SQL Server and SQLite; avoid provider-specific SQL |
| **Configuration over constants** for anything environment-dependent | New keys go in `appsettings.json` **and** `.env.example` (placeholders only) |

### Audit and evidence

Security-relevant writes (payments, integration changes, incident transitions, user/role changes) must
write an `AuditLog` row **in the same transaction**. Authentication failures must be audited **before** the
exception is thrown — evidence has to survive the rejection.

### Resilience honesty

If you add a dependency, decide and document its fallback. The platform's contract is: a missing optional
dependency makes the system `degraded` with an explicit mode string, never silently `healthy`. Do not add a
catch-all that turns a failure into a success.

---

## 5. Frontend conventions

- **Server state belongs to TanStack Query.** Give queries explicit keys; use `placeholderData` for paged
  views so tables do not flash empty.
- **All HTTP goes through `src/lib/api.ts`.** Do not call `fetch` directly in components — the wrapper owns
  auth headers, problem-details parsing, error messages and single-flight refresh.
- **Permissions gate the UI, the API enforces it.** Use `hasPermission(code)` for navigation and disabled
  states; never treat a hidden button as a security control.
- **Design tokens, not hard-coded colours.** Use the CSS variables (`--brand`, `--surface`, `--line`,
  `--ink`, …) so light and dark themes stay consistent.
- **Forms are Zod-validated** through `react-hook-form` resolvers; add the field message to the schema, not
  inline in the component.
- **No new runtime dependency without a reason.** The bundle is already a single chunk (a documented gap);
  adding a library for a small utility should be justified in the PR.

---

## 6. Commit and branch conventions

- Branch from `main`; keep branches short-lived and named for the change (`fix/payment-idempotency-race`,
  `docs/observability-gaps`).
- Commits are imperative and scoped: *"Add SPA fallback rewrite for deep links"*, not *"updates"*.
- One logical change per commit; a commit should build and pass tests on its own.
- Reference the affected document or ADR in the commit body when the change alters documented behaviour.

Branch protection, CODEOWNERS and a PR template are **not** configured in this repository (see
`docs/CI_CD.md` §1) — that is a gap, not a policy.

---

## 7. Pull request checklist

- [ ] Builds and all three suites pass locally (counts updated in documentation if they changed).
- [ ] No secrets, tokens, connection strings or `.env` values in the diff.
- [ ] New configuration keys added to `appsettings.json` and `.env.example` (placeholders only).
- [ ] Migrations added for model changes; script generation verified.
- [ ] Authorisation considered: does the new endpoint carry a permission policy? Is it tested for `403`?
- [ ] Failure behaviour defined: what happens when the database/cache/broker is unavailable?
- [ ] Audit written for any security-relevant mutation.
- [ ] Telemetry not broken: no new endpoint that writes request logs recursively.
- [ ] Documentation updated in the same PR (`README`, `docs/API.md`, `docs/ARCHITECTURE.md`,
      `docs/SECURITY.md` as applicable) — the docs are part of the deliverable.
- [ ] Claims are evidence-based: no invented benchmarks, no "production ready", no compliance language.

---

## 8. Documentation rules

The documentation is treated as a technical artefact with the same standard as the code:

1. **Never document a feature you cannot point at in the source.** If it is partial, say which part exists.
2. **Distinguish** implemented · configured · disabled in a given environment · simulated · proposed.
3. **Redis and Kafka** must always be described as *implemented and available in the project*, *supported in
   local/containerised environments*, and *disabled in the current Render deployment for cost optimisation*.
   Never "not implemented", "unsupported" or "unavailable".
4. **No invented numbers.** No fabricated latencies, availability percentages, coverage figures, CI runs or
   screenshots. Measurement plans are fine; results require a recorded run.
5. **No compliance claims.** No PCI DSS, ISO 27001, SOC 2, GDPR or "bank-grade" language.
6. **Keep documents consistent.** When you change behaviour, grep the other documents for the old claim —
   the same fact appears in the README, `ARCHITECTURE.md`, `SECURITY.md`, `OBSERVABILITY.md`,
   `OPERATIONS_RUNBOOK.md` and `DEPLOYMENT.md`.

---

## 9. Reporting a security issue

Do not open a public issue for a vulnerability. Describe the affected endpoint or component, the impact and
a reproduction step to the repository owner privately. Known industry-standard advisories in dependencies
are tracked in `docs/SECURITY.md` §6 — please report those there rather than duplicating them.
