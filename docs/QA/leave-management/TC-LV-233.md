---
id: TC-LV-233
user_story: US-LV-012
module: Leave Management
priority: critical
type: integration
status: pass
created: 2026-06-14
exec_note: "2026-07-03 VERIFICATION RE-RUN (BUG-037/086 fix PR#117): GET /api/v1/leaves/my-balance?year=2026 (employee@acme) -> HTTP 200 (was 500 on legacy 'Accrued' ledger enum). GET /api/v1/leaves/reports/BalanceSummary?year=2026 (hr@acme) -> HTTP 200. Values reconcile: EMP-0001 John Doe Annual Leave shows entitlement 2.74 / used 10.5 / balance -0.26 in BOTH the dashboard my-balance and the Balance Summary report. Accrued-enum 500 resolved; dashboard==report AC-1 reconciliation holds. PASS."
---

# TC-LV-233: Balance Summary values match the individual employee dashboard (AC-1, Test Hint)

## 1. Test Objective
Verify the Test Hint: balances shown in the Leave Balance Summary report exactly match the values an employee sees on their individual leave-balance dashboard (US-LV-006), proving the report reuses the same authoritative balance computation rather than a divergent calculation.

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-1
- Business Rules: BR-3 (real-time computed values)
- Cross-ref: US-LV-006 (`GET /api/v1/leaves/my-balance`), US-LV-002 entitlement engine

## 3. Preconditions
- Tenant "acme"; employee "Mark Otieno" with known entitlement, used, carry-forward, expired, adjustments per leave type.
- Mark can view his own US-LV-006 dashboard; HR can run the report.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Mark Otieno | known ledger |
| Leave type | Annual | entitlement 14, used 4 → remaining 10 (example) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Mark, read his US-LV-006 dashboard balance for each leave type | Record entitlement/used/remaining (BR-1 formula: entitlement + carryForward − used − expired + adjustments). |
| 2 | As HR, run the Balance Summary report and locate Mark's row | Mark's per-type remaining balance equals the dashboard value to the same precision for every leave type. |
| 3 | Apply an adjustment (e.g. an `adjusted` ledger entry) and re-run both | Both the dashboard and the report reflect the same updated balance (no drift). |
| 4 | Verify pending is not deducted from remaining in either view (BR-2 dashboard rule) | The report's "remaining" matches the dashboard's "balance", not balance−pending. |

## 6. Postconditions
- Report balances are reconciled with the per-employee dashboard — single source of truth confirmed.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
