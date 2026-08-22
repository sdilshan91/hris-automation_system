---
name: reference-fresh-scope-rls-writes
description: ISSUE-268 pattern — fresh-DI-scope DB writes must route through ITenantJobRunner under RLS; + the hrm_app-hardwire test gotcha
metadata:
  type: reference
---

Any code that opens its OWN DI scope and `SaveChanges` on a fresh `AppDbContext` (new pooled connection)
fails-closed under RLS-on: the fresh connection routes to `hrm_app` (NOBYPASSRLS) but the
`app.current_tenant` GUC is set only on the CALLER's DbContext (by `TenantTransactionBehavior` on HTTP /
`TenantJobRunner` on jobs), so the strict `WITH CHECK tenant_isolation` policy rejects the INSERT (SQLSTATE
**42501**). Fix = wrap the fresh-scope DB read+write in `ITenantJobRunner.RunForTenantAsync(tenantId, ...)`,
resolving the runner + `AppDbContext` from the **same** scope (both scoped → same instance, so the GUC the
runner sets applies to the db you write). Non-DB work (SignalR push, Hangfire enqueue) stays OUTSIDE the
runner block. Runner no-ops under `Rls:Enabled=false` / InMemory → behaviour-neutral today. Reference impl:
`SendEmailJob.cs` (read→send→persist 3-unit split); ISSUE-268 fixed the same seam in
`SignalRNotificationService`, `RealNotificationDispatcher`, `SessionActivityMiddleware`.

**Consequence for sibling tests:** adding the scoped `ITenantJobRunner` dependency means any InMemory unit
test that builds its own `IServiceScopeFactory` for that service must now register
`ITenantContext` + `IConfiguration` + `ITenantJobRunner` (else `No service for type ITenantJobRunner`). This
is a legit wiring update, not a test weakening.

**RLS-test gotcha (non-obvious):** in a Testcontainers RLS-on proof, hardwire `AppDbContext` DIRECTLY to the
`hrm_app` connection (as `RlsIsolationPostgresTests` test #9 does) — do NOT go through
`ConnectionRoutingInterceptor`. The interceptor routes an UNRESOLVED ambient to the privileged `hrm_owner`
role, which BYPASSES RLS and MASKS the 42501 bare-insert repro. Hardwiring hrm_app keeps the mechanism honest:
runner-wrapped write satisfies WITH CHECK, bare write throws 42501. Note the notification tables are SINGULAR
(`notification`, `notification_delivery`) and have NO FK on the user columns (arbitrary Guids are fine in
seeds) — but `refresh_tokens` (plural) DOES have an FK to `users`, so seed a `User` first. The
session-activity leg (`AuthService.UpdateSessionActivityAsync`, IgnoreQueryFilters UPDATE) has a WORSE RLS
failure mode than the INSERTs: no 42501, it silently matches 0 rows (SELECT hidden by RLS USING) — so its
regression arm asserts LastActiveAt UNCHANGED bare vs CHANGED through the runner (read back via BYPASSRLS
owner), not an exception. Test: `HRM.Tests/Integration/NotificationRlsPostgresTests.cs` (4 arms). Related: [[reference-rls-increment-3a]],
[[reference-notification-delivery-infra]].
