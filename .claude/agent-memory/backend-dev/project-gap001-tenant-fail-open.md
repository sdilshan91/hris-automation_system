---
name: project-gap001-tenant-fail-open
description: GAP-001 — tenant isolation is fail-OPEN at every layer except ~494 hand-written service guards; seven middleware/filter layers short-circuit on !IsResolved
metadata:
  type: project
---

GAP-001 (audited 2026-09-01, rated HIGH): with no resolvable tenant, nothing structural stops a
cross-tenant read. Verified again 2026-09-02 while building the HTTP detector.

The `!IsResolved` short-circuit is a repo-wide **convention**, and it is fail-open in *seven* places,
not the four the original audit named:
`TenantResolutionMiddleware` (passes through on empty/reserved subdomain),
the `AppDbContext` global filters (`!_tenantContext.IsResolved || x.TenantId == ...` — a tautology
when unresolved, so every row matches), `TenantAccessGuardMiddleware`,
`TenantStatusEnforcementMiddleware`, `ModuleEntitlementMiddleware`, `ScimEntitlementMiddleware`,
`ApiCallCounterMiddleware`. `ConnectionRoutingInterceptor` (RLS) is the only correctly-inverted layer
and is inert in shipped Development config (`PrivilegedConnection` blank, `Rls:Enabled=false`).

Authentication and authorization are entirely tenant-independent (permissions are JWT claims, not a
DB lookup), so an authenticated caller reaches the controller with `IsResolved == false`. The **first
and only** thing that rejects is the service-layer guard, returning 400 "Tenant context is not
resolved."

**Why it matters:** the whole of Critical Rule #1 rests on ~494 hand-written guards across ~104
service files. One new read path shipped without its guard is a full cross-tenant leak and nothing
else fails.

**How to apply:** when adding ANY tenant-scoped read path, the `!_tenantContext.IsResolved` guard is
load-bearing, not boilerplate — the EF global filter will NOT save you. Do not "simplify" it away.
Fixing the layers themselves (inverting the filters / rejecting in middleware) is an architecture
change that was explicitly held out of scope; it is decision-gated, not forgotten.

The detector is `src/backend/HRM.Tests/Integration/Http/UnresolvedTenantFailClosedApiTests.cs`
(see [[feedback-tenant-isolation-test-invariant]] for why it asserts what it does). Its known blind
spots: parameterized `{id}` routes, non-GET methods, and a brand-new entity with no other tenant's
rows in the shared test DB.
