---
name: reference-rls-increment-2a
description: RLS increment 2a (dormant policies migration + Testcontainers hrm_app fixture) — reusable facts + two gotchas for inc 2b/2c/3
metadata:
  type: reference
---

RLS US-PLT-002 increment 2a shipped on `fix/rls-2a-policies-tests`: migration
`20260710120000_Platform_RlsPolicies_Dormant` + `HRM.Tests/Integration/RlsIsolationPostgresTests.cs`.
Design authority: `src/backend/HRM.Infrastructure/Persistence/Rls/IMPLEMENTATION-DESIGN.md`.

**Migration shape:** self-maintaining PL/pgSQL `DO` block that discovers tables from
`information_schema.columns` where `column_name='tenant_id'` AND `table_type='BASE TABLE'` in `public`,
`NOT IN ('users','tenants')`. Creates `CREATE POLICY tenant_isolation` (idempotent via `pg_policies`
guard). DORMANT — NO `ENABLE ROW LEVEL SECURITY` (that's the inc-3 flag-gated reconciler). Result on a
fresh migrated DB: **112 policies** (108 query-filtered + 4 un-filtered tenant tables: `audit_logs`,
`plan_limit_overrides`, `tenant_scheduled_jobs`, `tenant_lifecycle_events`), 0 tables RLS-enabled.

**GOTCHA 1 — `NULLIF(current_setting('app.current_tenant', true), '')::uuid`, NOT the bare form.** A
`set_config(..., is_local => true)` GUC reverts to the **empty string** (not NULL) after its tx commits.
On a POOLED connection a later unscoped statement then hits `''::uuid` → SQLSTATE **22P02** (error, not
fail-closed-0). `NULLIF(...,'')` collapses both unset(NULL) and reset('') to NULL ⇒ clean fail-closed.
IMPLEMENTATION-DESIGN §1 and `TenantTransactionBehavior` still show the bare `current_setting(...)::uuid`
form — the policy must use NULLIF; the inc-3 reconciler must too if it re-derives the expression.

**GOTCHA 2 — `users` has an orphan/shadow `tenant_id`.** The global `User` entity (login spans tenants,
NO query filter) has a nullable `TenantId` SHADOW property → physical `users.tenant_id` column (from
InitialCreate). So "policy every tenant_id column" naively = 113, not the expected 112. `users` is
EXCLUDED (a strict `WITH CHECK` there would block user creation on `hrm_app`; enforcement is pointless as
all its tenant_ids are NULL). Reflection coverage guard uses `FindProperty("TenantId") != null` and must
exclude `"users"` by table name.

**Testcontainers RLS fixture pattern (must connect as non-superuser):** container default `postgres` is a
SUPERUSER → ALWAYS bypasses RLS → proves nothing. After `StartAsync`: (1) as superuser `CREATE ROLE hrm_app
LOGIN NOBYPASSRLS` + `hrm_owner LOGIN BYPASSRLS`; (2) `MigrateAsync()` (NOT EnsureCreated — that skips
policies); (3) GRANT usage/CRUD/sequences to both roles (owner has BYPASSRLS but still needs GRANTs as it's
not the table owner); (4) `ENABLE + FORCE ROW LEVEL SECURITY` on tenant tables to simulate inc-3; (5) seed
as superuser (bypasses WITH CHECK). Build `hrm_app`/`hrm_owner` conn strings via `NpgsqlConnectionStringBuilder`
swapping Username/Password. Set the GUC on the SAME connection EF uses via an explicit tx +
`ExecuteSqlRawAsync("SELECT set_config('app.current_tenant', {0}, true)", id)`. RLS WITH CHECK violation =
`PostgresException.SqlState == PostgresErrorCodes.InsufficientPrivilege` (42501).

Untouched by 2a (later increments): `Rls:Enabled` (still false), app connection routing/interceptor,
`DbInitializer`, Hangfire storage, per-job GUC (`ITenantJobRunner`).
