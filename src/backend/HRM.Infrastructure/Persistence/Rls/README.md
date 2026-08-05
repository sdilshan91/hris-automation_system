# Row-Level Security (US-PLT-002)

Defense-in-depth tenant isolation: PostgreSQL RLS enforces `tenant_id` scoping at the
database engine, so isolation survives application bugs (raw SQL, a misused
`IgnoreQueryFilters()`, an untenanted job). EF Core global query filters remain the
first layer; RLS is the backstop. Full spec: `docs/BA/platform/US-PLT-002.md`.

## What has landed (Phases 1–3 — plumbing, INERT by default)

- **`Rls:Enabled` flag** (`appsettings.json`, default **false**) — the master switch.
- **`ConnectionStrings:PrivilegedConnection`** (blank by default) — placeholder for the
  `hrm_owner` (BYPASSRLS) connection used by migrations / seeding / system + admin paths.
- **`TenantGucConnectionInterceptor`** (`HRM.Infrastructure/Persistence/Interceptors`) — sets the tenant
  GUC via `set_config('app.current_tenant', <tenantId>, false)` on **ConnectionOpened** (session scope),
  NOT a request-wide transaction. It is a **no-op** unless `Rls:Enabled` is true, a non-system tenant is
  resolved, and the provider is relational — so it does nothing today and never runs in the EF-InMemory
  test suite. (⚠ This REPLACED the original per-request `TenantTransactionBehavior`, which was **deleted**
  after ISSUE-277: its request-wide tx broke every request under prod `EnableRetryOnFailure` and nested
  with the ~5 own-transaction handlers. Job-path GUC is set via `ITenantJobRunner` in its own retry-safe
  tx. See the RE-VALIDATED GO note below + `FLIP-VALIDATION-2026-07-11.md`.)
- **`roles.sql`** — ops bootstrap for the `hrm_app` (NOBYPASSRLS) and `hrm_owner`
  (BYPASSRLS) roles. Run once by a DBA; not executed by the app.

## What has landed (increments 2a–3a — plumbing + policies + reconciler, still INERT)

- **Dormant policies migration** `20260710120000_Platform_RlsPolicies_Dormant` — a self-maintaining
  PL/pgSQL DO-block that `CREATE POLICY tenant_isolation` on every `public` base table carrying a
  `tenant_id` column (excl. `users`/`tenants`), strict `WITH CHECK`, nullable-tenant `USING` for
  `roles`. Dormant (no `ENABLE`) ⇒ enforcement-neutral even though it auto-applies on startup.
- **`ConnectionRoutingInterceptor`** — routes resolved-non-system tenants to `hrm_app` and
  startup/system/unresolved/job paths to `hrm_owner`; blank `PrivilegedConnection` ⇒ always
  `DefaultConnection` (non-breaking today).
- **`TenantJobRunner`** (`ITenantJobRunner.RunForTenantAsync`) — the job-side GUC helper so per-tenant
  Hangfire jobs stay inside the RLS backstop.
- **RLS reconciler** — `DbInitializer.ReconcileRowLevelSecurityAsync`, run on startup AFTER migrate +
  seed. Flag-gated + idempotent: `Rls:Enabled=true` ⇒ `ENABLE + FORCE` the policy-bearing set;
  `Rls:Enabled=false` ⇒ `NO FORCE + DISABLE` the same set (so a config rollback ACTIVELY rolls
  enforcement off — no down-migration). Warns if `Rls:Enabled=true` but the connected role bypasses RLS
  (superuser/BYPASSRLS). **No-op on every current environment (flag false everywhere).**
- **End-to-end proof** — `RlsReconcilerPostgresTests` drives the real reconciler + real
  `TenantTransactionBehavior` on a Postgres Testcontainer (roles from `roles.sql`, migrate/reconcile as
  `hrm_owner`, request path as `hrm_app`): ENABLE→isolated request→privileged-spans→DISABLE→reversible.

## Enablement runbook (the switch-on — per environment, deliberate)

> ✅ **RE-VALIDATED GO (2026-07-11).** The initial validation found **ISSUE-277** (a per-request GUC-transaction
> design bug that 500'd every request under RLS-on); it is now **RESOLVED** — the tenant GUC is set via
> `TenantGucConnectionInterceptor` (session-scope `set_config` on connection open, no request-wide tx). A second live
> end-to-end run under `Rls:Enabled=true` confirmed login + tenant CRUD + the previously-failing own-tx handlers
> (training enrol, etc.) all return 2xx, plus a pipeline-under-retry regression test. `roles.sql` password-interpolation
> bug also fixed. Full record: [`FLIP-VALIDATION-2026-07-11.md`](FLIP-VALIDATION-2026-07-11.md). This runbook is now
> safe to follow (mind the greenfield Hangfire grant in step 4 and the deferred long-tx items above).

Turning RLS on is a config action, not a deploy: nothing in source flips it. Per environment:

**Dev (`hris_dev_db`)** — the default `developer` role is a superuser and superusers ALWAYS bypass RLS,
so dev proves nothing until repointed:
1. `psql -h localhost -U postgres -d hris_dev_db -v hrm_app_password=… -v hrm_owner_password=… -f roles.sql`
2. Make `hrm_owner` the schema/table owner (it must own the tables so the reconciler's `ALTER TABLE …
   ENABLE ROW LEVEL SECURITY` succeeds) — e.g. run migrations as `hrm_owner`, or `REASSIGN OWNED BY
   developer TO hrm_owner` on the existing schema.
3. In `appsettings.Development.json`: `ConnectionStrings:DefaultConnection` → the `hrm_app` conn string,
   `ConnectionStrings:PrivilegedConnection` → the `hrm_owner` conn string, `Rls:Enabled` → `true`.
4. **Greenfield/fresh DB only (ISSUE-278):** `GRANT CREATE ON DATABASE <db> TO hrm_owner;` (or pre-provision the
   `hangfire` schema owned by `hrm_owner`) so Hangfire can bootstrap its own schema — otherwise startup crashes with
   `42501 permission denied for database`. An existing (already-migrated) DB already has the `hangfire` schema and
   needs no grant.
5. Restart the API → the reconciler `ENABLE + FORCE`s all tenant tables; requests set the GUC on `hrm_app`.
   (Note: the startup `hrm_owner bypasses RLS` warning is expected — the reconciler runs on the privileged
   connection — see ISSUE-279; it does not mean `DefaultConnection` is misconfigured.)

**Staging/Prod** — the ops equivalent: run `roles.sql` (as DBA/superuser), ensure `hrm_owner` owns the
schema, repoint `DefaultConnection`→`hrm_app` + `PrivilegedConnection`→`hrm_owner` + Hangfire storage →
`PrivilegedConnection`, set `Rls:Enabled=true`, deploy, restart. **Rollback = set `Rls:Enabled=false` +
restart** (the reconciler `DISABLE`s enforcement; the app runs pre-RLS on whatever connection is set).

> **DF-enc-rls-grant — verify `hrm_app` grants on tables created outside the default-privilege window.** `roles.sql`'s
> `ALTER DEFAULT PRIVILEGES FOR ROLE hrm_owner` auto-grants `hrm_app` on any table a **later** `hrm_owner` migration
> creates — so on a normal flip the `encryption_key_activation` system table (and every future table) is covered
> automatically (verified on the dev flip: `hrm_app` holds SELECT/INSERT/UPDATE/DELETE). The one edge to check on an
> **already-provisioned** prod DB: a table created *before* `roles.sql`'s `ALTER DEFAULT PRIVILEGES` ran won't have
> been auto-granted. Quick check after the flip (as superuser):
> `SELECT table_name, string_agg(privilege_type,',') FROM information_schema.role_table_grants WHERE grantee='hrm_app' GROUP BY 1 ORDER BY 1;`
> — any `tenant_id`/system table the app writes on `DefaultConnection` (hrm_app) that is missing needs a one-off
> `GRANT SELECT,INSERT,UPDATE,DELETE ON <table> TO hrm_app;`.

## Pre-flip checklist (carried from 2c — a hard go/no-go before any non-dev flip)

- [ ] `roles.sql` run; `hrm_app` is `NOBYPASSRLS`/non-owner; `hrm_owner` owns the schema tables.
- [ ] `DefaultConnection`→`hrm_app`, `PrivilegedConnection`→`hrm_owner`, Hangfire storage→`hrm_owner`.
- [ ] Dormant policies present on every tenant table (coverage-guard test green).
- [ ] Every Hangfire job classified privileged-vs-GUC and wrapped/routed accordingly.
- [ ] Full isolation + reconciler suites green on the target Postgres; rollback rehearsed.
- [x] **[MED — BLOCKER, ISSUE-268 — RESOLVED PR #244 2026-07-11]** notification/session persistence writes
      on a FRESH DI scope routed to `hrm_app` with NO GUC → fail-closed under RLS-on (notification INSERT →
      42501; `refresh_tokens` UPDATE → silent 0 rows). FIXED by wrapping the 3 fresh-scope writes
      (SignalRNotificationService, RealNotificationDispatcher, SessionActivityMiddleware) in
      `ITenantJobRunner.RunForTenantAsync` (no-ops when RLS off). Proven by `NotificationRlsPostgresTests`
      (real hrm_app RLS-on). See `docs/QA/TEST-FINDINGS.md#ISSUE-268`.
- [x] **[MED — ISSUE-269 — RESOLVED PR #246 2026-07-11]** payslip render/email jobs held ONE GUC tx per
      batch (idle-in-tx through PDF render / SMTP). FIXED: `GeneratePayslipsJob` + `SendPayslipEmailsJob`
      split into read-tx → work-outside-tx → per-chunk/per-send write-tx; proven on real Postgres RLS-on
      (`PayslipJobRlsPostgresTests`). `ProcessPayrollRunJob` intentionally LEFT ATOMIC (run aggregates +
      destructive replace); `DataExportGeneration`/`HrReportExport` low-frequency → still deferred (below).
      See `TEST-FINDINGS.md#ISSUE-269`.
- [ ] **[LOW — remaining]** `DataExportGeneration` + `HrReportExport` still hold one GUC tx per run (build
      outside tx if dumps/reports grow); low frequency (export rate-limited, only ≥1000-row reports go async)
      → acceptable to defer past the initial flip.
- [x] **[LOW — 3b, DONE 2026-07-11]** service-body DI-scope audit: the five wrapped per-tenant services are
      clean (use the injected scoped DbContext); the only fresh-scope hazard is the notification writers →
      folded into ISSUE-268 above.
- [~] **[3b — was WRONGLY marked RESOLVED 2026-07-11; corrected 2026-08-05]** CI RLS coverage. The original
      entry was right that `ci-gate.yml`'s `backend` job runs the full unfiltered `dotnet test` (so it would
      execute the RLS isolation + reconciler Testcontainers suites, Docker being available on
      `ubuntu-latest`), and right that no separate postgres-service-container job is needed. **But it
      parenthesised the fact that killed it:** ci-gate triggered on PRs into `main` **only**, while the
      de-facto trunk is `test/local-subdomains`. `main` is stale. So the gate had not run on a merged PR
      since **2026-07-01** — and the RLS suites landed **2026-07-10/11**, i.e. *these suites had never run in
      CI even once*. Citing them as pre-flip evidence was citing a green checkbox nobody earned.
      **Fixed:** `ci-gate.yml` now triggers on `[main, test/local-subdomains]`.
      **Still open:** this stays `[~]` and NOT `[x]` until a run on the real trunk has actually completed
      green — the whole lesson of this item is that "the workflow exists" is not "the workflow ran".

These follow-ups are tracked for **increment 3b**; 3a leaves RLS **ready, proven, and reversible** with
`Rls:Enabled` committed **false**. **The 2026-07-11 readiness audit resolved the CI + DI-scope items and
surfaced ISSUE-268 as a real flip-blocker** — do ISSUE-268 (and ideally ISSUE-269) as a dedicated flip-prep
story before setting `Rls:Enabled=true`.
