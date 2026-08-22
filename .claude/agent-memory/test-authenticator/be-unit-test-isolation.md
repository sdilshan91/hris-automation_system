---
name: be-unit-test-isolation
description: How backend xUnit InMemory tests model (and fail to model) tenant isolation in this repo — read before judging any "cross-tenant" unit arm
metadata:
  type: project
---

Backend unit tests build the context via `HRM.Tests/Unit/Helpers/TestDbContextFactory` →
`new AppDbContext(options, tenantContext)` with `UseInMemoryDatabase`.

Two facts that decide whether a "multi-tenant isolation" unit arm is REAL vs theatrical here:

1. **EF InMemory DOES honor global query filters** (they are LINQ-level, applied before provider
   translation). So a read-isolation arm — seed a row under tenant B, query under tenant A's
   `ITenantContext`, assert it is invisible — is genuine on InMemory. Verify the entity actually has a
   `HasQueryFilter(... TenantId == _tenantContext.TenantId)` in `AppDbContext.OnModelCreating` (e.g.
   `UserInvitation` at ~AppDbContext.cs:278); the arm rests entirely on that filter existing.
2. **The factory does NOT wire `TenantInterceptor`** (the SaveChanges write-stamper). So seeded rows keep
   whatever `TenantId` the test sets explicitly (a `tenantOverride` genuinely persists under the other
   tenant — good for read-isolation arms), BUT the **write-stamping** isolation layer is entirely
   unexercised by unit tests. A unit test can never prove auto-stamping; only an integration test can.

**Why:** Judged this correctly for the BUG-294 accept-invitation suite (2026-08-04) — the cross-tenant
arm was AUTHENTIC because of fact 1 + the real filter, not luck.

**How to apply:** When a BE unit "isolation" arm passes, confirm (a) the filter exists in code and (b)
you're not being asked to believe it proves write-stamping. Also: this repo DOES have a real
Testcontainers/WebApplicationFactory harness in `HRM.Tests/Integration/*PostgresTests.cs` (contradicts
CLAUDE.md's "no backend test project" line), so "a Postgres arm was infeasible" is never a valid excuse
for a missing one. See [[verify-code-not-ledger]].
