---
name: us-adm-004-lifecycle-run
description: US-ADM-004 tenant-lifecycle (suspend/terminate/reactivate/restore) deep API run 2026-06-24 — verdicts, the graceDays-default bug (BUG-002), and what blocks the login/451/deletion arms
metadata:
  type: project
---

US-ADM-004 (tenant suspend/terminate/reactivate/restore) deep API pass — 2026-06-24.

**Real routes** (TC text says `/api/v1/admin/tenants/{id}/suspend` — WRONG): `POST /api/v1/system/tenants/{id:guid}/lifecycle/{suspend|reactivate|terminate|restore}` + `GET .../lifecycle/history`. Suspend body `{reason}` (10-500 chars); terminate body `{reason, graceDays}` (graceDays 7-90). Mutations gated by `Tenant.Lifecycle` perm (SystemAdmin only); history by `Tenant.ViewLifecycle` (SystemAdmin+SystemSupport). All run in admin/system context (`X-Tenant-Subdomain: admin`).

**Result: 9 PASS / 1 FAIL / 11 BLOCKED, 1 finding [[BUG-002 graceDays default]].** Provisioned tenants are `Trial`; Trial is treated as Active for suspend/terminate ([[transition-matrix]] in `TenantLifecycleTransitions.cs`). Provisioning ALSO writes a `created` lifecycle event + `Tenant.Provisioned` audit row. Terminate schedules 4 `tenant_scheduled_jobs` (deletion + 14d/7d/1d reminders) — the Hangfire scheduler seam IS wired in the running app (it's null only in tests). Restore is prior-state-aware: reverts to Suspended if `suspended_at` is still set (terminate does NOT clear it), else Active — and de-queues all scheduled jobs. BR-2 system-tenant guard keys on **subdomain=="platform"** config, not an is_system column (that column doesn't exist).

**BUG-002 (MED, BE):** terminate with `graceDays` OMITTED → 400 `grace_days_invalid`, instead of applying the AC-3/FR-2/BR-4 documented plan default (30). Cause: `TerminateTenantRequest(string Reason, int GraceDays)` — non-nullable int defaults to 0 → rejected by both the validator and the service guard; no null→default branch exists. FE always sends a value so the live UI is fine; only spec-following API clients bite. Fix direction: make it `int?`, resolve null→30 before the 7-90 check.

**Why TC-04/05/08 are BLOCKED (re-test needs this):** login-block (AuthService:196), the 451 suspended-gate and the 403 terminating-read-only gate (TenantStatusEnforcementMiddleware) are all code-correct and present, BUT driving them live needs a **known-password non-admin tenant user on a suspended/terminating tenant**. Throwaway-tenant owners get a random/unknown password (provisioning doesn't return one), and acme/platform must never be suspended. To unblock: seed a tenant + a known-password Employee user, then suspend THAT tenant. TC-05 step-4 (admin reads suspended tenant → 200) DID pass. TC-09/13 blocked because reaching `Terminated` needs force-running the Hangfire deletion job (no API trigger; dashboard needs Playwright, which is down). TC-17 (typed-subdomain confirm) is FE-only — and note the terminate API has NO confirmation param, so the TC's step-6 "server defense-in-depth" check does not exist.

**Test data left in place:** throwaway tenants `qa04-{susp-1,term-1,term-2,matrix-1,react-1,restore-a,grace-1}` in mixed final states. acme(Trial)+platform(Active) verified untouched. See [[qa-baseline-2026-06-19]] for the seeded-persona / API-layer method.
