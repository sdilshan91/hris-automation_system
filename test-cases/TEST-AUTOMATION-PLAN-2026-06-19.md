---
title: HRM SaaS — Automated Test Plan (E2E + Observability)
created: 2026-06-19
status: proposed
owner: QA + Dev agents
scope_decisions:
  coverage: "Smoke first, then module-by-module E2E (bind designed e2e/integration TCs to runners)"
  environment: "Docker Compose full stack"
  observability: "Correlation-ID log assertions — fail test on backend ERROR/WARN"
---

# HRM SaaS — Automated Test Plan

Companion to [BUG-REPORT-2026-06-19.md](./BUG-REPORT-2026-06-19.md) and
[QA-COVERAGE-REPORT-2026-06-19.md](./QA-COVERAGE-REPORT-2026-06-19.md). Those established the
problem: **1,941 IEEE-829 specs, 0 executed; 0 E2E tests; unit suites mock cross-layer auth so a
bug that 403s the entire admin UI shipped green.** This plan closes that gap.

## 1. Objectives
1. **Catch cross-layer defects** unit tests can't (BUG-1..3 = real seeded role vs guard string, persona↔route mismatch).
2. **Run against a production-like Docker stack**, not mocks.
3. **Fail on silent server errors** — every test asserts the backend logged no `ERROR`/`WARN` for its own requests, via a per-test correlation ID.
4. **Bind the designed TCs to real runners** and drive their `status:` from `draft` → `automated` → `passed`/`failed`, giving a true execution-completion number (today: 0%).

## 2. Architecture & key decisions

| Decision | Choice | Why |
|---|---|---|
| Runner | **`@playwright/test`** (standalone project) | Parallel, fixtures, trace/video/screenshot, CI-native. The Playwright **MCP** stays for *interactive debugging only* — it is not a CI suite. |
| Location | new repo-root **`e2e/`** project (own `package.json`, `playwright.config.ts`) | Keeps E2E separate from the Karma unit tests in `src/frontend`. Spans FE+BE+DB, so it lives above both. |
| Target env | **Docker Compose** (`docker-compose.yml` + new `docker-compose.test.yml` override) | Production-like; lets the harness read backend Serilog via `docker logs`. |
| Observability | **Correlation-ID** header → Serilog enrichment → per-test log assertion | Strongest signal; surfaces 500s/warnings the UI swallows. |
| Browsers | chromium first; add firefox/webkit in a later phase | ROI. |
| TC traceability | each spec tagged `@TC-XXX-NNN`; a reporter maps results back to the markdown TCs | Turns the 0%-execution number real. |

## 3. Proposed repository layout
```
e2e/
  package.json
  playwright.config.ts
  docker/
    docker-compose.test.yml        # extends base compose: test seed, backend healthcheck, JSON logs
    seed/                          # deterministic personas + tenants (SQL or seed profile)
  src/
    fixtures/
      personas.ts                  # SystemAdmin, Tenant Admin, HR Officer, Manager, Employee
      auth.fixture.ts              # logged-in context per persona (see §5)
      logSentinel.fixture.ts       # correlation id + post-test backend/console log assertion (§6)
    support/
      dockerLogs.ts                # read & filter `docker compose logs backend` by correlation id
      pageObjects/                 # LoginPage, Sidebar, TenantConsole, EmployeeList, ...
    specs/
      smoke/                       # Phase 1 — persona route-access matrix + nav presence
      regression/                  # Phase 1 — BUG-1..5 pinned
      auth/ core-hr/ leave/ ...    # Phase 2+ — module-by-module, mirrors test-cases/ tree
  reporters/
    tc-status-reporter.ts          # writes execution results back against TC IDs
```

## 4. Environment — `docker-compose.test.yml` (override)
The base `docker-compose.yml` already publishes FE `:4200`, API `:5000`, Postgres `:5432`, Redis `:6379`.
The test override adds what CI needs:

1. **Backend healthcheck** (base compose has none for `backend`) — poll the existing endpoint
   `GET /health` (Program.cs:368, already excluded from tenant resolution). Compose-wait gates the suite on `backend: service_healthy`.
2. **Deterministic seed.** `DbInitializer` already seeds the `platform` tenant + `admin@hrm.local`.
   Add a **test seed** (SQL in `seed/`, or an env-gated seed profile) creating a known tenant
   `acme` with one user per persona: Tenant Admin, HR Officer, Manager, Employee — plus baseline
   departments/job-titles/employees so list pages aren't empty. Seed must be **idempotent** and run before tests.
3. **Structured logs for parsing.** Switch the API's Serilog **console sink to compact JSON**
   (`Serilog.Formatting.Compact.RenderedCompactJsonFormatter`) in the test/Docker profile so the
   harness can parse `@l` (level) + `CorrelationId` reliably. (Packages already present: Serilog.AspNetCore + Sinks.Console.)
4. **Ephemeral DB** — drop the named `pgdata` volume per run (or `docker compose down -v`) for repeatability.

## 5. Auth & session strategy — **the in-memory-token gotcha**
**Verified during investigation:** the JWT access token lives **only in memory** (localStorage/sessionStorage
are empty); the refresh token is an **httpOnly cookie**. Consequences for Playwright:

- Playwright `storageState` persists **cookies** (so the refresh cookie survives) but **not** the in-memory access token.
- The app has a `401 → refresh` interceptor (`core/interceptors/error.interceptor.ts:28`, `core/auth/auth.interceptor.ts`).

**Phase-0 spike (must verify first):** does a cold page load with only the refresh cookie silently
re-mint an access token? 
- **If yes:** use one **global-setup login per persona** → save `storageState` (cookie) → every test reuses it; the app self-refreshes on first call. Fast.
- **If no (no bootstrap refresh):** use a **per-worker fixture that logs in through the UI once** and keeps the page/context alive, or add a tiny app bootstrap-refresh. Recommend the fixture path; do **not** weaken app security to suit tests.

Persona fixtures expose `systemAdminPage`, `tenantAdminPage`, `hrOfficerPage`, `employeePage`.
Dev tenant resolution uses the `X-Tenant-Subdomain` header (frontend `tenantInterceptor`); the harness
sets `platform` for the system admin and `acme` for tenant personas.

## 6. Correlation-ID observability (the fail-on-warning mechanism)
**Flow:** test generates `correlationId` → sent as `X-Correlation-Id` on every request → Serilog enriches
every log line for that request → after the test, the harness greps backend logs for that id and **fails
on any `ERROR`/`WARN`** (plus a small, reviewed allowlist).

**Backend changes (small, mirrors the existing per-request TenantId/TenantSubdomain enrichment):**
1. Middleware: read/generate `X-Correlation-Id`, `LogContext.PushProperty("CorrelationId", id)`, echo it
   back on the response header. Register early (alongside/after tenant resolution).
2. Test profile: compact-JSON console sink (§4.3) so `@l` + `CorrelationId` are machine-readable.

**Harness side (`logSentinel.fixture.ts` + `dockerLogs.ts`):**
1. Per test: mint id, `context.setExtraHTTPHeaders({ 'X-Correlation-Id': id })`.
2. Capture **frontend** signals: `page.on('console')` (fail on `error`), `page.on('pageerror')`, and
   response listeners (fail on unexpected non-2xx/3xx).
3. After test: `docker compose logs backend --since <teststart>` → filter `CorrelationId == id` →
   assert **no level ≥ Warning**. Attach the matched log slice to the Playwright report on failure.
4. Allowlist file for known-benign warnings (reviewed, version-controlled) so the gate stays meaningful.

This is exactly what would have caught the bugs: a SystemAdmin smoke test hitting `/admin/tenants`
sees the 403 + the `/forbidden` redirect and fails immediately.

## 7. Test taxonomy & TC traceability
- Each spec is tagged with the TC id(s) it automates: `test('@TC-ADM-001 provision a tenant', ...)`.
- A custom reporter (`tc-status-reporter.ts`) writes results back to a run summary and (optionally)
  flips the corresponding `test-cases/**/TC-*.md` `status:` to `automated`/`passed`/`failed` — turning
  the **0% execution** number into a tracked, rising metric. (Status writes are a reviewed, opt-in step,
  never used to hide a failure.)
- Map to the **existing designed inventory** rather than inventing: 85 `e2e` + 123 `integration`
  typed TCs are the first automation targets; the 392 isolation-tagged TCs seed the cross-cutting suite.

## 8. Phased roadmap

### Phase 0 — Foundation (no feature coverage yet)
- `e2e/` project + `playwright.config.ts` (traces on first-retry, video/screenshot on failure).
- `docker-compose.test.yml`: backend healthcheck on `/health`, JSON logs, ephemeral DB, test seed.
- Backend: `X-Correlation-Id` middleware + JSON console sink (test profile).
- Auth spike (§5) → persona fixtures. Correlation-ID `logSentinel` fixture.
- GitHub Actions workflow (compose up → wait healthy → `playwright test` → artifacts).
- **Exit criteria:** one trivial green test that proves the whole pipeline (stack up, persona login, log-assert, teardown, artifacts).

### Phase 1 — Smoke + bug-regression (highest ROI; catches today's bugs)
- **Persona route-access matrix:** for each persona, assert every sidebar route either renders (2xx, no
  console/log errors) or is a *deliberate* 403 — encoded as an expected matrix. This single suite catches **BUG-1, BUG-2, BUG-3**.
- **Nav-presence assertions:** the System Admin Console (Tenants/Monitoring/Plans) is reachable for SystemAdmin (BUG-2).
- **Regression pack** pinning BUG-1..5, incl. a **role-string contract test**: the role the backend
  seeds/issues == the role string the guards check (kills the `SystemAdmin` vs `'System Admin'` class of bug).
- Tenant provisioning happy path: SystemAdmin opens `/admin/tenants`, creates a tenant, sees it listed.

### Phases 2..N — Module-by-module E2E (module-priority order)
Per module: automate its designed `e2e`/`integration`/critical `functional` TCs, wire negative + boundary
cases, and a **tenant-isolation** check (Critical Rule #1). Suggested order = the project's module priority:
1. Authentication & Authz → 2. Core HR → 3. Leave → 4. Attendance → 5. Recruitment → 6. Payroll →
7. Performance → 8. Admin Console → 9. Onboarding → 10. Training/Benefits → 11. Reports → 12. Notifications/Audit.
(Front-load **Admin Console** if tenant provisioning is release-critical.)

### Cross-cutting — Tenant Isolation suite (runs every phase)
Drive the 392 isolation-tagged designed cases: persona in tenant A can never read/write tenant B data —
asserted at the API boundary *and* the UI. This is the platform's #1 rule and deserves its own gate.

## 9. CI integration (GitHub Actions)
`.github/workflows/e2e.yml`: checkout → `docker compose -f docker-compose.yml -f e2e/docker/docker-compose.test.yml up -d --build`
→ wait `backend` healthy → `npx playwright test` (sharded across runners) → upload traces/videos/**filtered backend log slices** as artifacts → `docker compose down -v`. Publish the HTML report + the TC-status summary.

## 10. Backend/code changes this plan requires (flag — outside pure QA scope)
1. `X-Correlation-Id` middleware + JSON console sink (test profile). *(small)*
2. `/health`: **already exists** — just add the compose healthcheck. *(trivial)*
3. Deterministic test seed (personas + baseline data), idempotent, env-gated. *(small-med)*
4. **Bug fixes** so the smoke suite can pass: BUG-1 role-string alignment (one word), then BUG-2/3 persona/nav, BUG-4 nav permission strings — per the bug report's fix order. *(these are the point)*

## 11. Effort & sequencing (rough)
- Phase 0: ~2–3 dev-days (most of it the auth spike + correlation plumbing).
- Phase 1: ~2–3 dev-days; **immediately catches BUG-1/2/3** and prevents regressions.
- Phases 2..N: ~3–6 dev-days per module depending on surface; parallelizable across agents per module (non-overlapping `e2e/src/specs/<module>/` paths).

## 12. Immediate next steps
1. Approve stack/scope (done: module-by-module, Docker, correlation-ID).
2. Build **Phase 0** foundation + the **Phase 1 smoke/regression** suite (these two prove value fastest).
3. Decide whether to fix **BUG-1** first (recommended — one-word change) so the very first smoke run goes green and demonstrates the gate working end-to-end.
