---
id: US-PLT-002
module: Platform
priority: Should Have
persona: Security / Platform Engineer
status: draft
created: 2026-06-15
sprint: backlog
acceptance_criteria_count: 6
---

# US-PLT-002: PostgreSQL Row-Level Security as Defense-in-Depth Tenant Isolation

## 1. Description
**As a** platform/security engineer,
**I want** PostgreSQL Row-Level Security (RLS) policies enforced on all tenant-scoped tables, keyed off a per-request session variable,
**So that** tenant isolation survives application-layer bugs (raw SQL, a misused `IgnoreQueryFilters()`, an untenanted background job) — the database itself refuses to return another tenant's rows.

## 2. Background / Problem Statement
Tenant isolation today is **single-layer**: EF Core global query filters (reads) + `TenantInterceptor` (write stamping). This is correct only while application code is correct. It silently fails for raw SQL, forgotten/misused `IgnoreQueryFilters()`, or Hangfire/seed paths running without a resolved `ITenantContext`. Multiple stories' NFRs literally ask for "PostgreSQL RLS policy ... enforced as defense-in-depth" (e.g. US-REC-001 AC-4/NFR-2); none have implemented it. EF filters SHALL remain (belt and suspenders); RLS is the second layer.

## 3. Acceptance Criteria (IEEE 830 S3.2)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | RLS is enabled on a tenant-scoped table | A query runs with `app.current_tenant` set to Tenant A | Only Tenant A rows are returned, even via raw SQL and even with EF query filters disabled (`IgnoreQueryFilters()`) |
| AC-2 | A request is authenticated for Tenant A | The per-request DB connection is used | The session variable `app.current_tenant` is set to Tenant A's id before any query executes, and is correctly scoped/reset for pooled connections |
| AC-3 | A write is attempted with a `tenant_id` not matching the session tenant | `SaveChanges` runs | The RLS `WITH CHECK` policy rejects the insert/update (no cross-tenant writes) |
| AC-4 | A system/admin context operation runs (migrations, `DbInitializer` seeding, tenant resolution lookup, system-level Hangfire jobs) | The operation executes | It uses a role/path that can legitimately bypass RLS (`BYPASSRLS` role or explicit system context), without leaking tenant data to normal requests |
| AC-5 | The migration adding RLS is generated | It is reviewed | It is produced by `dotnet ef migrations add` with the `CREATE POLICY` / `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` statements added via `migrationBuilder.Sql(...)` (the snapshot is NOT hand-edited) |
| AC-6 | RLS is active across the platform | The full integration test suite runs (Testcontainers, real Postgres) | All existing isolation tests pass plus new tests proving RLS blocks cross-tenant access even when the app layer is deliberately bypassed |

## 4. Functional Requirements
- FR-1: Enable RLS (`ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY`) on every table carrying `tenant_id` (i.e. every `BaseEntity` table). Maintain a single source of truth for that table list.
- FR-2: Create a `USING (tenant_id = current_setting('app.current_tenant', true)::uuid)` policy and a matching `WITH CHECK` policy on each.
- FR-3 (**MANDATED MECHANISM** — test-friendly + pooling-safe): Set the tenant GUC with **`SET LOCAL app.current_tenant = '<id>'` inside an ambient per-request transaction**, NOT a session-level `SET`. Implement an ambient unit-of-work (middleware or a MediatR `TransactionBehavior`) that, for every tenant-scoped request, opens a transaction → issues `SET LOCAL` from `ITenantContext` → runs the handler → commits/rolls back. Rationale: `SET LOCAL` is transaction-scoped and auto-resets on commit/rollback, so the tenant variable **cannot leak across pooled connections** — eliminating the #1 RLS-with-pooling hazard. (Note: `SET LOCAL` is a no-op outside a transaction and does not carry between EF autocommit statements, which is exactly why the explicit per-request transaction is required.) Do NOT use session-level `SET` on pooled connections.
- FR-4: The application SHALL connect as a **dedicated login role WITHOUT `BYPASSRLS`** (so RLS always applies, even to raw SQL). Provide a **separate privileged connection/role** (with `BYPASSRLS` or table ownership) used ONLY by migrations, `DbInitializer` seeding, the tenant-resolution lookup, and system-level Hangfire jobs — never by normal request flow. Keep this bypass surface narrow and auditable.
- FR-5: Keep EF Core global query filters in place (defense-in-depth, not replacement).

## 5. Non-Functional Requirements
- NFR-1: Per-request overhead limited to one `SET LOCAL` statement + the ambient transaction wrapper; no measurable regression to P95 latency targets (reads now run inside a transaction — verify this is acceptable under load).
- NFR-2: Connection pooling correctness — no session-variable bleed between requests sharing a pooled connection.
- NFR-3: Rollback path — the migration is reversible (`Down` drops policies and disables RLS).

## 6. Risks & Constraints
- **Mechanism is decided** (FR-3): `SET LOCAL` + ambient per-request transaction + non-`BYPASSRLS` app role. This is the test-friendly AND production-suitable choice because it is leak-proof under pooling by construction. The rejected alternative — session-level `SET` on pooled connections — is a prod data-leak hazard and a source of order-dependent flaky tests; do not use it.
- Ambient transaction cost: reads now run inside an explicit transaction. Confirm acceptable under load; ensure long-running/streaming endpoints aren't harmed.
- Background jobs / seeding run without a tenant → must use the privileged (bypass) connection explicitly.
- Scope decision: implement across ALL tenant-scoped tables (recommended) vs. high-sensitivity tables first (payroll, PII) — confirm before build.

## 7. Test Hints
- With RLS on: open a connection as Tenant A, run raw SQL `SELECT * FROM vacancy` → only A's rows; switch `app.current_tenant` to B → only B's rows.
- Run an EF query with `IgnoreQueryFilters()` and confirm RLS STILL constrains results to the session tenant.
- Attempt an insert with a mismatched `tenant_id` → rejected by `WITH CHECK`.
- Hammer pooled connections across two tenants concurrently → assert no cross-tenant bleed.
- Confirm migrations + `DbInitializer` still run (bypass path works).

## 8. Notes
- Cross-cutting platform work — schedule deliberately, not inside a feature-loop story. Confidence that this is the right end-state for a PII/payroll SaaS: high; main execution risk is pooling + the system-bypass path.
