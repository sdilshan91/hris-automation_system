---
name: payroll-regression-2026-06-27
description: Payroll US-PAY-001..012 REGRESSION re-test 2026-06-27 — no new defects, no regressions; all prior findings still present; isolation header-override still live
metadata:
  type: project
---

# Payroll regression re-test 2026-06-27 (REPORT-ONLY)

Re-ran high-signal payroll TCs against running stack (BE :5000 debugger-free, FE pinned platform). **Outcome: zero new findings, zero regressions, no TEST-STATUS state changes — all 12 US-PAY stay `[!]`.**

**Why:** verify deltas vs the 2026-06-26 baseline passes, not duplicate. **How to apply:** payroll is stable-but-buggy; the same defect set persists. Don't re-file these — they keep existing IDs.

## Still present (re-reproduced)
- BUG-060/071/077 — `hr@acme.test` → 403 on salary-components(Configure)/runs(Run)/reports(Export); `tenantadmin@acme.test` 200. HR Officer locked out of ALL payroll surfaces.
- BUG-072 HIGH — statutory POST `slabTo=1e18` → 500 numeric overflow, no row persisted.
- BUG-073 HIGH — statutory PUT (even identical body) → 500 `DbUpdateConcurrencyException` at `StatutoryRuleService.cs:141` (owned-children RemoveRange+rebuild collides w/ change-tracker). Re-confirmed via Serilog hrm-20260627.log:43319. Edit impossible.
- BUG-080 HIGH — audit-trail emits only 11 action types; missing SalaryStructure.*, EmployeeSalary.*, PayrollRun.Completed, PayslipPDF.Generated, PayslipEmail.Sent.
- ISSUE-187 HIGH (BUG-003 extends) — **CONCRETE LEAK**: acme JWT (tenant 019ef3ba-…) + `X-Tenant-Subdomain: techoneglobal` → 200 + 3 techoneglobal audit rows (019ef3c3-…). Header-override / US-AUTH-007 spoofable subdomain LIVE.
- ISSUE-181 HIGH — reconciliation same mechanism; this run returned 0 rows ONLY because techoneglobal has no finalized run (no data to leak), NOT fixed.

## Still self-protected (good news held — NOT BUG-003)
US-PAY-001 salary-components, 005 my-payslips, 006 statutory, 007 adjustments: acme JWT + foreign subdomain → 0-rows/403/404. US-PAY-001 count=0 under techoneglobal = empty tenant, NOT a fix (same header-override as ISSUE-187, manifests only where foreign tenant has data).

## Engine intact
June 2026 = 1 Finalized run (019f0434-…) untouched. Re-init June→409 period_already_finalized. Finalized→submit→409 invalid_transition. test-calc 200 side-effect-free. acme statutory back to 3 fixtures.

## Cross-link result
**BUG-086 (leave_ledger 'Accrued' enum) does NOT affect payroll** — US-PAY-010 `/reconciliation` reads leave cleanly (200, 28 rows, leaveDaysByType/totalLeaveDays populated). No 500, no new finding.

## Gotchas reconfirmed
- Isolation leak only visible where foreign tenant HAS data; audit-trail is the reliable demonstrator (techoneglobal has 3 audit rows seeded).
- Safety policy 2026-06-27: isolation checks READ-ONLY (foreign GET only); never cross-tenant write. Honored.
- See [[us-pay-007-adjustments-findings]] [[us-pay-005-payslip-self-service-findings]] [[us-pay-011-bulk-payslip-email-findings]].
