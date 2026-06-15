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

## What is DEFERRED (Phase 4 — the switch-on; needs a Docker/Postgres environment)

This step changes production behavior and must be staged to a non-prod env first. It was
deferred because RLS cannot be verified on this dev machine (no Docker, no local Postgres;
the EF InMemory provider does not implement RLS).

1. **Enable-RLS migration** — `dotnet ef migrations add Platform_RowLevelSecurity`, then add
   `migrationBuilder.Sql(...)` per tenant-scoped table (every entity with a `TenantId`
   query filter in `AppDbContext.OnModelCreating`; **exclude** `tenants` and `users`):
   `ALTER TABLE x ENABLE ROW LEVEL SECURITY; ALTER TABLE x FORCE ROW LEVEL SECURITY;
   CREATE POLICY tenant_isolation ON x USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
   WITH CHECK (...);`
   For nullable-tenant tables (`roles`): `USING (tenant_id IS NULL OR tenant_id = ...)`.
   `Down` drops the policies and disables RLS.
2. **Route system/admin paths to `PrivilegedConnection`** — `DbInitializer`, the tenant
   lookup, system-context requests, and cross-tenant Hangfire jobs must use the BYPASSRLS
   connection (RLS treats an unset GUC as *see nothing*, the inverse of the EF filters'
   *unresolved = see all*).
3. **CI test job** — run RLS integration tests against the existing **postgres:16 service
   container** in `.github/workflows/ci-gate.yml` (NOT Testcontainers — CI has no Docker
   daemon for it but does provide a service container). Prove: per-GUC raw-SQL isolation;
   isolation holds even with `IgnoreQueryFilters()`; `WITH CHECK` rejects mismatched-tenant
   inserts; no cross-tenant bleed across pooled connections; migrations/seeding work on the
   privileged path.
4. **Flip `Rls:Enabled` to true** and point the app connection at the `hrm_app` role.
