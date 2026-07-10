# RLS Implementation Design — Increments 2 & 3 (2026-07-10)

Companion to `README.md`. Decision context: [[ADR-2026-07-10-tenant-isolation-model]] (shared-DB + RLS).
Increment 1 (AsyncLocal `AmbientTenant` + cache-prefix) is merged (PR #230).

## Increment framing
- **Increment 2 = non-breaking landing.** Dormant policies in the DB, roles created in every env,
  privileged-connection plumbing + routing, jobs audited/routed, isolation tests written — but **no
  table has RLS enforced** and `Rls:Enabled=false`. Auto-apply on startup stays provably safe.
- **Increment 3 = the flip.** Enforce (ENABLE + FORCE via a flag-gated startup reconciler), point the
  app connection at `hrm_app`, set `Rls:Enabled=true`, prove isolation on real Postgres, wire CI.

## 1. Policy table set (108)
Rule: an entity gets a `tenant_isolation` policy iff its table has a `TenantId` column AND a global
query filter referencing `TenantId`. 109 `HasQueryFilter` − `Tenant` (filters `!IsDeleted` only) = **108
tables**. `users` has no filter (excluded automatically). Only **`roles`** has a nullable `TenantId`.

Generate the list by REFLECTION over the built model (do NOT hand-maintain 108 names):
```
foreach et in Model.GetEntityTypes():
  if et.FindProperty("TenantId") is null: continue
  if et.GetQueryFilter() is null: continue
  if table in {"tenants","users"}: continue        // belt-and-suspenders
  emit policy(et.GetSchemaQualifiedTableName(), et.FindProperty("TenantId").IsNullable)
```
Policy forms:
- Strict (107): `USING (tenant_id = current_setting('app.current_tenant', true)::uuid)` + same `WITH CHECK`.
- `roles` (nullable): `USING (tenant_id IS NULL OR tenant_id = current_setting('app.current_tenant', true)::uuid)`
  but **strict `WITH CHECK (tenant_id = current_setting(...)::uuid)`** — an `hrm_app` session must never mint a NULL/foreign-tenant (system) role.

`current_setting(..., true)` (missing_ok) → unset GUC = SQL NULL → `tenant_id = NULL` is not-true → **fail-closed (see nothing)**. This inverts the EF filter's unresolved=see-all.

**R1 — coverage gap:** `audit_log`, `plan_limit_override`, `tenant_scheduled_job`, `tenant_lifecycle_event`
carry `TenantId` but have NO query filter → the rule gives them NO policy. `audit_log` (nullable, shared
with system rows) stays isolated by app-code `.Where(TenantId==)` only. **Decision needed** (see below).

## 2. Auto-apply hazard + safe split
- `CREATE POLICY` alone is INERT until the table has `ENABLE ROW LEVEL SECURITY`. So a policies-only
  migration is enforcement-neutral even though `DbInitializer.MigrateAsync` auto-applies it. ✅ safe.
- `ENABLE` enforces for all non-owner, non-BYPASSRLS roles; `FORCE` also enforces the owner. Putting
  `ENABLE/FORCE` in the auto-applied migration chain = **next restart breaks the app** if any path is on
  `hrm_app` without the GUC / with a blank `PrivilegedConnection`. ❌ never do this.

**Recommended split:**
- **Inc-2 migration `Platform_RlsPolicies_Dormant`** — generated `CREATE POLICY` for all 108 (+roles form).
  Dormant → non-breaking. Kept in the CI-migration-tested chain. `Down` drops policies.
- **Inc-3 enforcement = a `Rls:Enabled`-gated idempotent startup reconciler in `DbInitializer`** (runs as
  `hrm_owner`): `Rls:Enabled=true` → `ENABLE + FORCE` all 108 (idempotent); `false` → `DISABLE` (so a
  config rollback actively rolls enforcement off). Couples enforcement to the SAME flag that gates
  `TenantTransactionBehavior` — both turn on together; reversible by config+restart, no down-migration.

## 3. Connection routing (two-connection)
Single `AddDbContext` today. **Recommend: keep it + route at connection-open via an
`IDbConnectionInterceptor`** driven by a scoped role selector:
`UsePrivileged = !(tenant.IsResolved && !tenant.IsSystemContext)` → resolved-non-system ⇒ `hrm_app`;
unresolved/startup/system/no-context-job ⇒ `hrm_owner`. `ConnectionOpeningAsync` sets the connection
string accordingly; **blank `PrivilegedConnection` → fall back to `DefaultConnection`** (behavior-neutral
until wired). Fallback approach if Npgsql string-mutation is shaky (**R4**): a privileged
`IDbContextFactory<PrivilegedAppDbContext>` used explicitly by DbInitializer/system/cross-tenant jobs.

**R2 — Hangfire:** its storage (`Program.cs:243`) bootstraps its own schema (DDL) and its tables have no
policy. On `hrm_app` it can't own/create that schema. **Point Hangfire storage at `PrivilegedConnection`.**

Files: `DependencyInjection.cs` (interceptor + `IDbRole`), new `Persistence/Interceptors/ConnectionRoutingInterceptor.cs`,
`Program.cs` (Hangfire→privileged), `appsettings*.json` (populate `PrivilegedConnection`).

## 4. Per-job GUC gap
Jobs don't flow through MediatR → `TenantTransactionBehavior` never sets their GUC. Under RLS a per-tenant
job on `hrm_app` sees nothing.
- **Per-tenant jobs → stay on `hrm_app`, set the GUC** via a shared `ITenantJobRunner.RunForTenantAsync(tenantId, work)`
  that mirrors the behavior (scope → SetTenant → BeginTransaction → `set_config('app.current_tenant', id, true)` →
  work → commit). Keeps the RLS backstop for jobs. One-line wrap per per-tenant job body.
- **No-context / cross-tenant jobs → privileged, no GUC** (the selector already routes them there; their
  `IgnoreQueryFilters()` + explicit predicates work under BYPASSRLS as today).
- **Jobs touching a policy table by payload id** (`SendEmailJob`, `SendLockoutNotificationJob`) → prefer
  `RunForTenantAsync(payload.TenantId, …)` so they stay inside the backstop.
- **Audit all ~49 jobs** into a routing table (privileged vs GUC) — a hard go/no-go gate before the flip.
  The cache whitelist tables (holiday/leave_types/departments/job_titles/shift/statutory_rule/
  custom_field_definitions/locations) are all policy-bearing; confirm no no-context job reads them per-tenant.

## 5. Roles for dev / CI / Testcontainers
`roles.sql` = DBA bootstrap (`hrm_app` NOBYPASSRLS + `hrm_owner` BYPASSRLS + grants + default privileges).
- **R3 — dev `developer` is a superuser → superusers ALWAYS bypass RLS.** So dev enforces nothing unless
  `DefaultConnection` is repointed at `hrm_app` (+ `PrivilegedConnection`=`hrm_owner`) in
  `appsettings.Development.json` after running `roles.sql` on `hris_dev_db`. Add a startup WARN when
  `Rls:Enabled=true` but the connected role is `rolsuper`/`rolbypassrls`.
- **Testcontainers (`ApiTestFactory`):** container `postgres` user is superuser → RLS tests must NOT run as
  it. After `StartAsync`, run `roles.sql`, inject `DefaultConnection=hrm_app` + `PrivilegedConnection=hrm_owner`.
- **CI (`ci-gate.yml`, postgres:16):** add an RLS test job (service container, not Testcontainers) that
  creates the roles via psql, sets `PrivilegedConnection`, runs the isolation suite. Existing `backend`
  job stays EF-InMemory.

## 6. Isolation tests (real Postgres, connect as `hrm_app`, policies ENABLED+FORCED)
1. GUC-set ⇒ own tenant only (raw SELECT over `employees` + `roles`).
2. GUC-unset on `hrm_app` ⇒ 0 rows (fail-closed).
3. `IgnoreQueryFilters()` STILL isolated under RLS (the headline backstop).
4. `WITH CHECK` rejects a mismatched-tenant INSERT (incl. a `roles` NULL/foreign case).
5. `hrm_owner` (privileged, no GUC) spans tenants + migrations/seeding succeed.
6. No cross-tenant bleed across a pooled connection (tx1 GUC=A → commit → tx2 GUC=B → no-tx sees nothing)
   — validates `is_local` reset.
7. HTTP e2e via `ApiTestFactory` on `hrm_app`: login A → only A's data; unresolved path → privileged works.
8. **Coverage guard test:** reflect the §1 set, assert `pg_policies` has a policy per table (fails CI when a
   new tenant entity forgets its policy).
9. Per-job GUC test: `RunForTenantAsync(A)` sees only A; a cross-tenant job on `hrm_owner` spans tenants.

## 7. Rollout + gates

> **Status (3a landed):** the flag-gated ENABLE/FORCE-or-DISABLE reconciler is live as
> `DbInitializer.ReconcileRowLevelSecurityAsync` (called after migrate+seed), proven end-to-end by
> `RlsReconcilerPostgresTests` (real reconciler + real `TenantTransactionBehavior` on `hrm_app`, incl. the
> reversible DISABLE path), and `SendEmailJob` is restructured to read→send(no tx)→persist(own committed
> unit)→rethrow so its retry state survives a send failure under RLS. `Rls:Enabled` stays **false** (no-op
> everywhere). Remaining for **3b**: the CI RLS service-container job + long-running-by-id-job GUC
> granularity (MED) + service-body DI-scope audit (LOW). See `README.md` for the enablement runbook + checklist.
**Inc-2 (non-breaking, `Rls:Enabled` stays false):** (1) roles provisioning in dev/CI/Testcontainers;
(2) dormant policies migration (verify via `pg_policies`); (3) routing interceptor + `IDbRole` (blank-priv
fallback); (4) Hangfire→privileged (blank fallback); (5) `ITenantJobRunner` + job audit; (6) flag-gated
ENABLE/FORCE reconciler (no-op while flag false); (7) isolation + coverage tests + CI RLS job.

**Gate before Inc-3 (non-prod first):** roles exist + `hrm_app` non-super/non-owner; `PrivilegedConnection`
populated + `DefaultConnection`→`hrm_app`; dormant policies on all 108 (assertion green); routing verified;
every job classified + GUC-wrapped-or-privileged; full isolation suite green; rollback rehearsed.

**Inc-3 (flip):** deploy with the repointed connections + Hangfire→privileged → set `Rls:Enabled=true`
→ reconciler ENABLE/FORCEs all 108 and `TenantTransactionBehavior` starts setting the GUC (together) →
run isolation suite → CI RLS job required → rollback = flip flag false + restart (reconciler DISABLEs).

## Risks
- **R1** un-filtered tenant tables (`audit_log`, `plan_limit_override`, `tenant_scheduled_job`,
  `tenant_lifecycle_event`) get no policy → decide policy-vs-EF-only.
- **R2** Hangfire storage must move to `PrivilegedConnection` (schema DDL/privileges).
- **R3** dev superuser silently bypasses RLS — repoint dev to `hrm_app` or dev proves nothing.
- **R4** connection-string mutation in the interceptor needs EF10/Npgsql validation; factory fallback ready.
- **R5** any per-tenant job left on `hrm_app` without the GUC helper breaks at the flip (job audit = mitigation).
- **R6** `roles` WITH CHECK must be strict, or tenants could mint system roles.
- **R7** the reconciler MUST actively `DISABLE` on `Rls:Enabled=false` or a rollback leaves tables enforced
  while the GUC stops.

## Decisions (confirm before coding)
- **#A** policies via migration + enforcement via flag-gated startup reconciler *(recommended)* vs enable-migration.
- **#B** connection-routing interceptor *(recommended, least churn)* vs privileged `IDbContextFactory`.
- **#C** does DEV enforce RLS (repoint `DefaultConnection`→`hrm_app` in Development) or only Testcontainers/CI/staging?
- **R1** give `audit_log` (+ siblings) a nullable-form policy, or leave EF-only (documented gap)?
