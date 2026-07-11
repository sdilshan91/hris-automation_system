---
name: us-lv-001-leave-types-findings
description: 2026-06-25 US-LV-001 Configure Leave Types API test pass — routes/perms, BUG-025 missing-audit, BUG-026 BUG-003 extended, ISSUE-029 seed drift, residue
metadata:
  type: project
---

# US-LV-001 Configure Leave Types Per Tenant — test pass 2026-06-25

REPORT-ONLY API pass (FE platform-bound → UI/a11y/cross-browser/responsive BLOCKED). 40 bound TCs: **15 PASS / 3 FAIL / 22 BLOCKED**.

**Why:** representative leave-management module testing; first US-LV pass.
**How to apply:** reuse these facts when re-testing leave-types or other US-LV stories.

## Routes & perms (LeaveTypesController, `[Route("api/v1/tenant/leave-types")]`)
- GET `/` (`?activeOnly=`), GET `/{id}`, POST `/`, PUT `/{id}`, POST `/{id}/deactivate`, POST `/{id}/reactivate`, POST `/reorder`.
- Perms: `LeaveType.View / .Create / .Edit / .Deactivate`. **Authz matrix:** Tenant Admin = full, HR Officer = full, Manager = none (403), Employee = none (403). No-auth = 401. (NOTE: perm scheme is `LeaveType.*`, NOT the `Leave.Configure` the US/TCs name.)
- Service: `HRM.Infrastructure/Services/LeaveTypeService.cs`. EF filter `AppDbContext.cs:229` = `!IsDeleted && (!IsResolved || TenantId==ctx)`.

## Findings filed (continue ledger)
- **BUG-025 HIGH** — LeaveType create/edit/deactivate/reactivate/reorder write **ZERO audit rows** (only `LogInformation`); AC-2 + NFR-3 (before/after) unmet. `audit_logs` HAS before/after cols. Same missing-audit class as BUG-010/018/023/024. HIGH because audit is a named AC of this story (payroll-affecting).
- **BUG-026 HIGH** — **BUG-003 extended**: acme token + `X-Tenant-Subdomain: techoneglobal` → GET list + GET by-id return **techoneglobal's** leave types (200). Control: acme header on same id = 404. Proven via DB ground truth (acme=4 types, techoneglobal=3; cross-tenant returned techoneglobal's 3). Write arm NOT executed (no techoneglobal pollution). TC-ISO-001 FAIL.
- **ISSUE-029 MED** — FR-4 seed drift: provisioning (`TenantProvisioningService.SeedDefaultLeaveTypes:325`, called :173) seeds only **3** (Annual/Sick/Casual). The spec-complete 8-type `LeaveTypeService.SeedDefaultsForTenantAsync` (incl. Maternity/Paternity/Bereavement/Unpaid + LOP) is **DEAD CODE, 0 callers**. **No LOP system type in ANY tenant** (system_category<>'None' = 0 db-wide). LOP only lazily via `EnsureLopTypeForTenantAsync` (LeaveRequestService/LopService).
- **ISSUE-030 LOW** — duplicate-name + conflicts return **400** (msg exact); TC-003 expects 409/422. Status nit.
- **ISSUE-031 LOW** — XSS payloads stored verbatim (no API sanitization); Angular escapes on render → defense-in-depth only. SQLi closed (EF param). TC-015 PASSES as written.
- **ISSUE-032 LOW** — RLS NOT enabled on leave_types (relrowsecurity=f); **0 RLS policies in whole DB** (US-PLT-002 deferred). NFR-2 ("EF filter AND RLS") partially unmet → only EF layer. TC-ISO-003 BLOCKED env.

## Verified GOOD (don't re-flag)
- BR-1 case-insensitive uniqueness works (exact/lower/upper all 400); name **IS trimmed** (padded dup rejected); inner-double-space = distinct (matches TC-003 data). Validation all instant 400 (color/gender/accrual/negatives/lengths). Soft-delete/FR-5 (activeOnly filter) works. Reorder works. All 4 accrual freqs accepted. Tenant write-stamping correct. System-type deactivation-block coded (`DeactivateAsync` checks SystemCategory). No 500s/hangs (debugger-free, clean Serilog).

## Many bound TCs are cross-story
TC-048/051/057/058 (leave-request apply), 066 (approvals), 142 (holidays), 154/155 (carry-forward), 212 (LOP), 026 (US-LV-002 entitlement rules) — BLOCKED scope-other-US. 218 (LOP) BLOCKED data-no-LOP.

## RESIDUE left in acme (019ef3ba-…) — FLAG FOR CLEANUP
Created (all is_active, NOT cleaned — BR-2 has no hard-delete endpoint, only deactivate): `QA Annual Leave TC001`, `QA Accrual Monthly/Quarterly/Yearly/Upfront`, `QA NegBal`, `QA <script>alert(1)</script>`, `Annual  Leave` (double-space). **`Annual Leave` was EDITED by TC-002 (entitlement 14→25, cfl→8, cfe→6) and NOT reverted.**

DB pw: user-secrets `Sanjesi#123` (developer/hris_dev_db); psql at `/c/Program Files/PostgreSQL/18/bin/psql.exe`. See [[qa-no-debugger-for-perf]] [[qa-personas-reseed-2026-06-25]].
