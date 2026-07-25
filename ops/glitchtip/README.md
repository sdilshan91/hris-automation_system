# Self-hosted GlitchTip — runbook

Error tracking for the HRM SaaS (Sentry-API-compatible), self-hosted so PII-bearing
exceptions never leave our trust boundary. Decision: [ADR-2026-07-08](../../docs/vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md);
SDK wiring: **US-PLT-006** (PR #448).

## Run it (detached)

A foreground `up` dies when you close the terminal — always use `-d`:

```bash
docker compose -f ops/glitchtip/docker-compose.yml --env-file ops/glitchtip/.env up -d
docker compose -f ops/glitchtip/docker-compose.yml ps           # status
docker compose -f ops/glitchtip/docker-compose.yml logs -f web  # logs
docker compose -f ops/glitchtip/docker-compose.yml down         # stop (keeps data)
docker compose -f ops/glitchtip/docker-compose.yml down -v      # ⚠️ stop + WIPE all data
```

- Web UI: **http://localhost:8000**. `restart: unless-stopped` → survives reboot.
- `migrate` is a one-shot that exits (won't appear in `docker stats`/`ps`). Normal.
- `.env` (gitignored) holds `GT_SECRET_KEY` / `GT_DATABASE_URL` / `GT_PG_PASSWORD`.

## First-time setup + DSN wiring

1. Open http://localhost:8000 → **register the first user** (becomes superuser).
2. Create an **Organization** → a **Project** per app component (best practice):
   - `.NET / ASP.NET Core` project for the **backend**.
   - `Angular / Browser` project for the **frontend** (optional slice).
3. Copy each project's **DSN** (`http://<key>@localhost:8000/<projectId>`).

### SaaS model: one project per component, NOT per tenant

All tenants' errors flow into the one backend project — every event is tagged
`tenant_id` + `tenant_subdomain`, so you **segment per customer by filtering on those
tags** in GlitchTip. Do not create a DSN per tenant.

### Set the DSNs (never commit real values — Critical Rule #6)

```bash
# Backend (config key GlitchTip:Dsn) — via user-secrets (dev) or env GlitchTip__Dsn
cd src/backend/HRM.Api && dotnet user-secrets set "GlitchTip:Dsn" "http://<key>@localhost:8000/<backendProjectId>"

# Frontend — src/frontend/src/environments/environment.ts  (a browser DSN is public-by-design,
# but the localhost value only works locally — do NOT commit it; environment.prod.ts stays blank
# and the real DSN is injected at deploy time)
```

Blank DSN ⇒ the SDK is **inert** (no init, no network) — safe by default.

## Verify

Send a test envelope (proves DSN + ingest without booting the app):

```bash
curl -s -X POST "http://localhost:8000/api/<projectId>/envelope/" \
  -H "X-Sentry-Auth: Sentry sentry_version=7, sentry_key=<key>" \
  --data-binary $'{"event_id":"'"$(openssl rand -hex 16)"'"}\n{"type":"event"}\n{"level":"error","message":{"formatted":"smoke test"},"tags":{"tenant_subdomain":"smoketest"}}'
# → HTTP 200 + {"id":"…"} means the DSN is valid and GlitchTip is ingesting.
```

Then check the project in the UI. To exercise the in-app PII scrub + tenant tagging,
run the API with the DSN set and trigger a real exception.

## Before production

- Set **`GLITCHTIP_DOMAIN`** to the real, publicly-reachable URL (the DSN host is
  derived from it; browsers must be able to reach it for the FE slice).
- **Container networking:** if `HRM.Api` runs in *its* compose, `localhost:8000` won't
  reach the GlitchTip container — use the host IP or a shared docker network.
- Harden `.env`: strong `GT_SECRET_KEY` / `GT_PG_PASSWORD`, set
  **`ENABLE_OPEN_USER_REGISTRATION=false`** after creating the superuser, and a real
  `EMAIL_URL` (currently `consolemail://`, so alert emails go nowhere).
- Back up the `gt-pgdata` volume — see [`../backup/`](../backup/) (ISSUE-330).
