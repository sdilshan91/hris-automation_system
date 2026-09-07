---
name: rls-withcheck-blocks-null-tenant-writes
description: A tenant-scoped request CANNOT insert a TenantId=null audit row under RLS — WITH CHECK is always strict; this is why ISSUE-062 half (a) is blocked
metadata:
  type: project
---

A tenant-scoped request **cannot** write a system-scoped (`TenantId = null`) row to any nullable-tenant
table once `Rls:Enabled` is on. Attempting it inside the login transaction turns the lockout control into
a **500 on `POST /api/v1/auth/login`**.

**Why:** `Platform_RlsPolicies_Dormant` builds an asymmetric policy for nullable-`tenant_id` tables:

- `USING`      → `tenant_id IS NULL OR tenant_id = current_tenant` — system rows are **readable**
- `WITH CHECK` → `tenant_id = current_tenant` — **always strict**

The migration states the intent outright: *"a tenant must never mint a NULL/system row"*. So the READ side
of the null-tenant convention is supported and the WRITE side is deliberately forbidden. `AuditLog.cs`'s
"no second audit table" doc and the `AppDbContext` `TenantId == null` filter arm describe the **read**
convention only — they are not permission to write one from a tenant request.

`CrossTenantScope.Enter()` is the sanctioned escape hatch (routes to `hrm_owner`, BYPASSRLS), **but** it
documents that it will NOT re-route an **already-open connection**. `account_locked` is written inside
`AuthService`'s `ExecutionStrategy` + `BeginTransactionAsync` block, so the scope cannot help there and a
42501 poisons the whole transaction (catching it does not save you — the tx is already aborted). A correct
implementation must write the platform copy **after commit**, out of band, and fail-soft.

`PlatformMonitoringService` gets away with `TenantId = null` because it runs in **system/admin context**,
not a tenant-scoped request. Do not cite it as precedent for a tenant-request writer.

**How to apply:** before adding any `TenantId = null` write, ask which context it runs in. Prove it with a
real-Postgres RLS test — `RlsOnApiTestFactory` (non-bypass role, real HTTP) is the harness; an InMemory
test has no RLS and will pass while production 500s. Reproducer preserved at
`scratchpad/i062-RlsOnLockoutAuditApiTests.repro.cs`.

Related: [[reference-rls-increment-2a]], [[reference-fresh-scope-rls-writes]],
[[feedback-integration-tests-inmemory]], [[reference-payroll-audit-bug080]]
