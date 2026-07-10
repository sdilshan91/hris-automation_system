# Row-Level Security (US-PLT-002)

Defense-in-depth tenant isolation: PostgreSQL RLS enforces `tenant_id` scoping at the
database engine, so isolation survives application bugs (raw SQL, a misused
`IgnoreQueryFilters()`, an untenanted job). EF Core global query filters remain the
first layer; RLS is the backstop. Full spec: `user-stories/platform/US-PLT-002.md`.

## What has landed (Phases 1–3 — plumbing, INERT by default)

- **`Rls:Enabled` flag** (`appsettings.json`, default **false**) — the master switch.
- **`ConnectionStrings:PrivilegedConnection`** (blank by default) — placeholder for the
  `hrm_owner` (BYPASSRLS) connection used by migrations / seeding / system + admin paths.
- **`TenantTransactionBehavior`** (`HRM.Infrastructure/Behaviors`) — a MediatR pipeline
  behavior that opens a per-request transaction and runs
  `set_config('app.current_tenant', <tenantId>, is_local => true)` (the parameterised,
  pooling-safe equivalent of `SET LOCAL`). It is a **no-op** unless `Rls:Enabled` is true,
  a non-system tenant is resolved, and the provider is relational — so it does nothing
  today and never runs in the EF-InMemory test suite.
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

Turning RLS on is a config action, not a deploy: nothing in source flips it. Per environment:

**Dev (`hris_dev_db`)** — the default `developer` role is a superuser and superusers ALWAYS bypass RLS,
so dev proves nothing until repointed:
1. `psql -h localhost -U postgres -d hris_dev_db -v hrm_app_password=… -v hrm_owner_password=… -f roles.sql`
2. Make `hrm_owner` the schema/table owner (it must own the tables so the reconciler's `ALTER TABLE …
   ENABLE ROW LEVEL SECURITY` succeeds) — e.g. run migrations as `hrm_owner`, or `REASSIGN OWNED BY
   developer TO hrm_owner` on the existing schema.
3. In `appsettings.Development.json`: `ConnectionStrings:DefaultConnection` → the `hrm_app` conn string,
   `ConnectionStrings:PrivilegedConnection` → the `hrm_owner` conn string, `Rls:Enabled` → `true`.
4. Restart the API → the reconciler `ENABLE + FORCE`s all tenant tables; requests set the GUC on `hrm_app`.

**Staging/Prod** — the ops equivalent: run `roles.sql` (as DBA/superuser), ensure `hrm_owner` owns the
schema, repoint `DefaultConnection`→`hrm_app` + `PrivilegedConnection`→`hrm_owner` + Hangfire storage →
`PrivilegedConnection`, set `Rls:Enabled=true`, deploy, restart. **Rollback = set `Rls:Enabled=false` +
restart** (the reconciler `DISABLE`s enforcement; the app runs pre-RLS on whatever connection is set).

## Pre-flip checklist (carried from 2c — a hard go/no-go before any non-dev flip)

- [ ] `roles.sql` run; `hrm_app` is `NOBYPASSRLS`/non-owner; `hrm_owner` owns the schema tables.
- [ ] `DefaultConnection`→`hrm_app`, `PrivilegedConnection`→`hrm_owner`, Hangfire storage→`hrm_owner`.
- [ ] Dormant policies present on every tenant table (coverage-guard test green).
- [ ] Every Hangfire job classified privileged-vs-GUC and wrapped/routed accordingly.
- [ ] Full isolation + reconciler suites green on the target Postgres; rollback rehearsed.
- [ ] **[MED — follow-up 3b]** long-running by-id jobs (payslips / data-exports / payroll runs) hold ONE
      transaction for the whole job under RLS via `RunForTenantAsync`; for very long runs consider a
      set-GUC-per-short-unit variant so a single transaction doesn't span the entire batch.
- [ ] **[LOW — follow-up 3b]** service-body DI-scope audit: confirm no per-tenant service resolves a
      DbContext outside the request/job scope that sets the GUC.
- [ ] **[3b]** CI RLS job wired (postgres service container, not Testcontainers).

These follow-ups are tracked for **increment 3b** (the CI RLS job + long-job GUC granularity); 3a leaves
RLS **ready, proven, and reversible** with `Rls:Enabled` committed **false**.
