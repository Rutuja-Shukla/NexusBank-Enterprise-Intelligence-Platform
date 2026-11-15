# Screenshots

Genuine captures of the **live hosted deployment**, not mockups. Every value visible on screen is
rendered from the running API (see `docs/OBSERVABILITY.md` §4 — the console has no seeded dashboard
figures).

| File | Screen | What it demonstrates |
| --- | --- | --- |
| `01-command-centre.png` | Command centre | Live aggregated telemetry: availability, volume, latency, integration topology with dependency detail, active incidents |
| `02-payment-operations.png` | Payment operations | Paged, filterable payment register with status, masked account numbers, risk score and processing time |
| `03-payment-detail.png` | Payment detail | Five-step journey timeline, `LEDGER BALANCED` badge, masked instruction panel, correlation id |
| `04-integration-hub.png` | Integration hub | Registered service estate with health, latency, failure rate, category and **FIXTURE** labelling for simulated telemetry |
| `05-event-streaming.png` | Event stream | Topic registry, dead-letter depth, consumer lag, and the honest broker banner: *"In-process development bus (Kafka disabled by configuration)"* with **FALLBACK ACTIVE** |
| `06-security-centre.png` | Security centre | Operator directory with roles, sessions and lockout state; tabs for roles/permissions, audit trail and access history |

## Provenance

- **Environment:** `https://nexus-bank-sand.vercel.app` (Vercel console) → `https://nexusbank-api-w53j.onrender.com`
  (Render API); SQLite database; Redis and Kafka disabled in that deployment.
- **Captured:** 2026-11-15, desktop viewport 1440×900.
- **Data:** the seeded synthetic estate only. No real customer, account or payment data appears.
- **Account:** `admin@nexusbank.dev` (Administrator) so that the Security centre is reachable.

## Known artefact in these captures

The browser panel used by the documentation tooling renders a small **"Freebuff"** indicator in the
top-left corner of the viewport. It overlays part of the NexusBank wordmark and is **not** part of the
application. If clean product shots are required for a public README or CV:

```bash
# any browser at 1440x900, signed in as admin
https://nexus-bank-sand.vercel.app/            # 01
https://nexus-bank-sand.vercel.app/payments    # 02  (then open a Completed payment for 03)
https://nexus-bank-sand.vercel.app/integrations# 04
https://nexus-bank-sand.vercel.app/events      # 05
https://nexus-bank-sand.vercel.app/security    # 06
```

Capture the full page (`Ctrl+Shift+P` → "Capture full size screenshot" in Chrome DevTools) so the
below-the-fold ledger, audit trail and dead-letter tables are included.

## What is deliberately not shown

- No fabricated dashboards, charts or monitoring screens.
- No data that would imply real customers or live banking traffic.
- No screenshots of a CI run, cloud console or uptime dashboard — those artefacts do not exist for this
  project and are not claimed.
