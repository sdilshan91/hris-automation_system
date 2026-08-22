---
name: admin-iso-fixture-exec-2026-06-30
description: 2026-06-30 report-only exec of 14 admin-console isolation/authz/lifecycle TCs on the iso* throwaway fixture - 7 pass / 7 fail; 2 NEW HIGH bugs + 1 MED issue
metadata:
  type: project
---

2026-06-30 ran 14 admin-console cross-tenant ISOLATION / AUTHZ / LIFECYCLE TCs (API-layer curl probes) against the pre-seeded `iso*` throwaway fixture (isoa/isob Active, isosus Suspended, isoterming Terminating, isoterm Terminated; all pw `Admin@123!`). REPORT-ONLY.

**Verdicts: 7 PASS / 7 FAIL / 0 BLOCKED.**
- PASS: TC-ADM-001-08 (provision authz: tenant/auditor->403, no-token->401, SystemAdmin lists), 001-09 (cross-tenant id-injection under OWN header->404), ISO-004 (tenant-context cache scoped), 008-12 (Auditor read 200 / export 403), 003-10 (terminated impersonation->403 reject), 010-10 (terminated export->409 both personas).
- FAIL: ISO-001/ISO-002/ISO-003 (all = existing **BUG-003**: isoa token + foreign `X-Tenant-Subdomain: isob` header -> 200 + full isob rows leaked; same-context reads ARE isolated, only the foreign-header mismatch leaks). 003-03/004-04/004-05 (= new **BUG-106**). 003-05 (= new **BUG-107**). 004-08 (= new **ISSUE-217**).

**NEW findings filed:**
- **BUG-106 HIGH** (BE): Suspended-tenant Tenant Admin/Owner is NOT exempt from the 451 gate. `TenantStatusEnforcementMiddleware.IsTenantAdminOrOwner` reads `ICurrentUser.Roles` (= `User.FindAll("roles")`); even though the JWT carries `"roles":"Tenant Admin"` matching `BuiltInRoles.TenantAdmin` exactly, the exemption never fires -> admin 451'd on EVERY tenant API (employees/audit/data-exports). Suspected JWT inbound claim-mapping. Symptom 100%, internal cause ~55%. Also `/tenant/lifecycle-notice`->404 (route unimplemented).
- **BUG-107 HIGH** (BE): Impersonation FR-6 destructive-op block bypassed. `ImpersonationReadOnlyBehavior.DestructiveCommandMarkers` matches by request-type-name SUBSTRING, but the real commands `ForcePasswordResetCommand`/`DeactivateUserCommand`/`AssignUserRolesCommand`/`EditUserRolesCommand` match NONE of the markers (markers have ResetPassword/AssignRole/DeleteUser; commands have PasswordReset/AssignUserRoles/DeactivateUser). `force-password-reset` actually executed (200, Serilog-confirmed) under a full SystemAdmin impersonation. Read-only gate (`EndsWith("Command")`) works; the destructive blocklist is the broken part.
- **ISSUE-217 MED** (BE): Terminating-tenant `POST /tenant/data-exports`->403 because `IsExport` substring marker `/exports` doesn't match `/data-exports`; audit-log `/export` (has `/export`) passes. Grace-window GDPR export wrongly blocked (AC-3/BR-6).

**Writes made into fixture (coordinator owns teardown):** created 1 real employee in **isoa** via benign-impersonation control probe (`EMP-0001`, ImpBenign Probe, id 019f1926-de66-7c6b-ac48-cfe7c431ff0d); forced a password-reset flag on isoa-admin (still logs in with Admin@123!). All within iso* fixture; NO writes/deletes to acme/techoneglobal/platform; no DELETE/teardown SQL run. Several impersonation sessions started+ended cleanly (BR-3 one-active-per-operator confirmed; end via platform context when target is on a suspended tenant, else the 451 gate traps the end call).

Gotcha: `/api/v1/employees` is 404 - real route is `/api/v1/tenant/employees` (see [[fe-be-tenant-url-prefix-mismatch]]). Impersonation start body = {targetUserId, targetTenantId, reason(min10)}.
