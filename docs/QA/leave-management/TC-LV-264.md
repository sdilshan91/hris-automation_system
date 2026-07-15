---
id: TC-LV-264
user_story: US-LV-006
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-07-15
defect:
  - ISSUE-305
---

# TC-LV-264: Apr–Mar fiscal tenant — leave-year boundary, accrual window, and carry-forward expiry anchor to April, not January (ISSUE-305 regression)

## 1. Test Objective
Verify the ISSUE-305 fix on US-LV-006/008: the leave subsystem reads `Tenant.FiscalYearStartMonth` instead of hardcoding calendar Jan 1–Dec 31. For a tenant with `FiscalYearStartMonth = 4` (Apr–Mar), the **leave-year boundary**, the **accrual window** (`LeaveAccrualJob`), the **year-end** processing (`ProcessLeaveYearEndJob`), the **carry-forward expiry** (`LeaveCarryForwardCalculator.ComputeExpiryDate`), and **pro-rata** (`LeaveEntitlementEngine.CalculateProRata`) all anchor to **April**.

## 2. Related Requirements
- User Story: US-LV-006 (also US-LV-002 pro-rata, US-LV-008 carry-forward)
- Acceptance Criteria: leave-year is calendar-or-fiscal per tenant
- Defect: ISSUE-305
- Cross-reference: `Tenant.FiscalYearStartMonth` (spec Phase 4); related ISSUE-176 (reports)

## 3. Preconditions
- Tenant with `FiscalYearStartMonth = 4`.
- An employee with an entitlement, accrual, and a carry-forward-eligible balance.
- Postgres-backed context; jobs runnable for a controlled "as-of" date.
- Pre-fix: `leaveYear = DateTime.UtcNow.Year` + `new DateTime(leaveYear,1,1)…12,31` make these arms FAIL.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| FiscalYearStartMonth | 4 | Apr–Mar |
| Leave-year start | 1 April | boundary |
| Leave-year end | 31 March | boundary |
| Mid-year hire | e.g. 1 Oct | pro-rata over Apr–Mar |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve the current leave-year window for the tenant. | **1 Apr – 31 Mar** (not 1 Jan – 31 Dec). |
| 2 | Run the accrual job and inspect the leave-year it accrues into. | Accrual window anchored to the Apr–Mar year. |
| 3 | Run year-end / carry-forward and inspect the carry-forward **expiry date**. | Expiry anchored to the fiscal year (e.g. carry expires within the next Apr–Mar year per policy), not the calendar year. |
| 4 | Compute pro-rata for the 1-Oct hire. | Pro-rated over the remaining Apr–Mar period (~6 months), not Jan–Dec. |
| 5 | Control: a tenant with `FiscalYearStartMonth = 1`. | Unchanged calendar Jan–Dec behaviour (no regression). |

## 6. Postconditions
- Fiscal-year tenants get correct leave-year boundaries, accrual, expiry, and pro-rata; calendar-year tenants unaffected.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
