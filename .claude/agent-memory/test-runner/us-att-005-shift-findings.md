---
name: us-att-005-shift-findings
description: 2026-06-26 US-ATT-005 shift mgmt/assignment API test pass — verdicts, routes, and findings BUG-048/ISSUE-074..077/ENH-006
metadata:
  type: project
---

# US-ATT-005 Shift Management & Assignment — REPORT-ONLY API pass 2026-06-26

Owned TCs: TC-ATT-051..066 + TC-ATT-ISO-008 (17). **13 PASS / 3 FAIL / 2 BLOCKED.**
Cross-ref-only (left draft): 088/090/092/094 (ATT-007), 100/105/106/111/117 (ATT-008), 121/127 (ATT-009), 132 (ATT-010).

**Routes** (all `[RequirePermission("Attendance.Shift.Manage")]`): `GET/POST /api/v1/attendance/shifts`, `PUT/DELETE .../shifts/{id}`, `POST .../shifts/{id}/clone` (returns **200** not 201), `POST .../shifts/{id}/assign`, `GET .../employees/{id}/shift?date=`. Service = `HRM.Infrastructure/Services/ShiftService.cs`.

**Authz matrix:** TenantAdmin/HR = full; Manager/Employee = **403 on everything incl. resolve own shift** (no self-scope); no-token = 401. Correct gate except self-resolve (ISSUE-076).

**What WORKS (PASS):** SINGLE create, validation (zero-duration BR-7, neg break/grace, bad/dup working_days normalized to distinct+sorted), FLEXIBLE (null times, minHours required/positive, capped 24), night shift (end<start accepted), bulk assign (3-emp, tenant-stamped, idempotent re-assign), **effective-dating EXACT** (A.effective_to=B.effectiveFrom-1, exactly one active/date, history preserved), default fallback (provisioned "General Shift" isDefault=true resolves for unassigned), **rotating resolves perfectly** across cycle (Morning R/R+3, Evening R+7/R+10, Morning R+14, pre-R→default), delete-prevention EXACT verbatim msg + code `shift_in_use` 409 (dynamic {N}), clone (copies params, distinct name "Copy of X", 0 inherited assignments, independent edit), working_days+grace metadata exposed. NFR-1 perf: list p95=38ms, resolve p95=47ms @20VU (SLA 2000ms).

**FINDINGS (next IDs were BUG-048 / ISSUE-074 / ENH-006):**
- **BUG-048 HIGH BE** — trailing/leading-whitespace name → **HTTP 500** (unhandled 23505). Duplicate pre-check compares RAW `request.Name` (ShiftService:60) but stores `.Trim()` (:74) → slips check, collides on `ix_shift_tenant_name_unique`. Same on UpdateAsync:110. Class of BUG-047 (constraint w/o handled pre-check).
- **ISSUE-074 MED BE** — name uniqueness **case-SENSITIVE** ("day shift" + "Day Shift" both 201). No LOWER/citext. BUG-013/016/017 class.
- **ISSUE-075 MED BE** — **no audit_logs** on any shift create/update/delete/assign/clone (only Serilog INF). DB confirmed 0 shift rows ever. ISSUE-067/069/071/073 class.
- **ISSUE-076 LOW BE** — employee can't resolve OWN shift (resolve gated by Manage → 403). Fails closed; TC-063 step6 documented-expectation gap.
- **ISSUE-077 LOW BE** — no API to set/transfer `is_default` (create/clone hardcode false, not in ShiftRequest). Provisioning seeds 1 default; fallback works but can't manage. TC-058 step6 untestable.
- **ENH-006** — clone 200 vs TC's 201; ResolvedShiftDto has no per-date working-day flag; minHours cap 24 vs §7 999.99 (impl stricter/correct).
- **BUG-003 EXTENDED (not re-filed)** — TC-ISO-008: acme JWT + `X-Tenant-Subdomain: techoneglobal` → 200 with TG's shift (id 019ef3c3 vs acme 019ef3bb); platform header → 019ed613. Read leak CONFIRMED. Matching-context isolation CLEAN (TG shift not in acme list; acme shift→404 under foreign header). Write-arm INFERRED only (NOT executed — no TG write): TenantInterceptor stamps header-resolved tenant, so spoofed-header create would write to TG.

**BLOCKED:** TC-065 (bulk 500 emps — acme not seeded to 500; would create heavy residue), TC-066 (a11y/cross-browser — fe-platform-bound + axe needs UI).

**ACME RESIDUE (flag cleanup):** shifts created — Day Shift, day shift, ZD-control, Flex 8h, Flex-24, Night Shift, Morning, Evening, Rotation 2wk, Copy of Day Shift, Copy of Day Shift (2) (ZD5 deleted in TC-060). Assignments: John Doe (Day→Night eff 06-29), Def Status + Prob Status (Day Shift), Et Contract (Day eff 06-27), Gen Pnts (Rotation eff 06-29). NO writes to techoneglobal/platform/globex.

See [[testing-loop-report-only]] [[qa-no-debugger-for-perf]] [[qa-personas-reseed-2026-06-25]] [[us-att-002-clockout-findings]]
