---
id: TC-LV-032
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-032: Probation employee only accrues leave types marked probation_eligible

## 1. Test Objective
Verify that employees in probation status only receive entitlements for leave types that have `probation_eligible = true`. Leave types with `probation_eligible = false` should not accrue any entitlement during the probation period.

## 2. Related Requirements
- User Story: US-LV-002
- Business Rules: BR-3
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Leave type "Annual Leave" exists with `probation_eligible = false`, entitlement rule = 20 days.
- Leave type "Sick Leave" exists with `probation_eligible = true`, entitlement rule = 7 days.
- Leave type "Casual Leave" exists with `probation_eligible = true`, entitlement rule = 5 days.
- Employee "Probation Pete" has status = "probation", joined 2026-01-01, in Engineering department.
- Employee "Active Alice" has status = "active", joined 2026-01-01, same department/level as Pete.

## 4. Test Data
| Leave Type | Probation Eligible | Entitlement | Pete (Probation) | Alice (Active) |
|------------|-------------------|-------------|------------------|----------------|
| Annual Leave | false | 20.00 | 0.00 | 20.00 |
| Sick Leave | true | 7.00 | 7.00 | 7.00 |
| Casual Leave | true | 5.00 | 5.00 | 5.00 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Pete's employee status is "probation" | Status confirmed as probation. |
| 2 | Verify Alice's employee status is "active" | Status confirmed as active. |
| 3 | Trigger Hangfire accrual calculation for all employees | Job executes successfully. |
| 4 | Query Pete's leave balance for "Annual Leave" | Balance = 0.00 days (probation_eligible = false, not accrued). |
| 5 | Query Pete's leave balance for "Sick Leave" | Balance = 7.00 days (probation_eligible = true, accrued normally). |
| 6 | Query Pete's leave balance for "Casual Leave" | Balance = 5.00 days (probation_eligible = true, accrued normally). |
| 7 | Query Alice's leave balance for "Annual Leave" | Balance = 20.00 days (active employee, normal accrual). |
| 8 | Query Alice's leave balance for "Sick Leave" | Balance = 7.00 days (active employee, normal accrual). |
| 9 | Verify `leave_ledger` for Pete: no "accrual" entry for "Annual Leave" | No ledger entry exists for Annual Leave + Pete. |
| 10 | Verify `leave_ledger` for Pete: "accrual" entries for Sick Leave and Casual Leave exist | Ledger entries present with correct amounts. |
| 11 | Change Pete's status from "probation" to "active" | Status updated successfully. |
| 12 | Trigger accrual recalculation for Pete | Job executes. |
| 13 | Query Pete's leave balance for "Annual Leave" | Balance now accrues (pro-rated from the date probation ended, if applicable, or full if backdated to year start). |

## 6. Postconditions
- Probation employees only receive entitlements for leave types marked `probation_eligible = true`.
- Active employees receive all applicable entitlements regardless of probation_eligible flag.
- Transitioning from probation to active triggers re-evaluation of entitlements.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
