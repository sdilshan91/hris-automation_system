---
id: TC-LV-098
user_story: US-LV-005
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-098: Approving leave for a payroll-locked period is blocked (CONDITIONAL on payroll module)

## 1. Test Objective
Verify that when a leave request spans a period that has since been locked for payroll, approval is blocked with the message "Cannot approve leave for a payroll-locked period.", the request stays Pending, and no ledger entry is created (BR-4).

## 2. Related Requirements
- User Story: US-LV-005
- Business Rules: BR-4
- Dependencies: payroll module (period-lock) -- CONDITIONAL/forward-looking

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- A direct report has a pending leave request whose date range falls within a payroll period.
- That payroll period has been locked since the request was submitted.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request period | 2026-06-02..06-04 | Within a locked payroll period |
| Payroll period | 2026-06 | Status Locked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, click "Approve" on the request spanning the locked period | API returns a 4xx with the exact message "Cannot approve leave for a payroll-locked period." (BR-4). |
| 2 | Inspect the request status | Remains `Pending`. |
| 3 | Query `leave_ledger` | No `used` entry is created; balance unchanged. |
| 4 | Approve a different request whose period is NOT payroll-locked | Succeeds normally (the lock check only blocks locked periods). |
| 5 | If the payroll module / period-lock is NOT yet implemented | This test is marked CONDITIONAL/DEFERRED on the payroll module: the block is validated once a payroll-lock signal exists. This is recorded explicitly as a dependency, NOT a silent pass; the non-locked approval path (step 4) is verified now. |

## 6. Postconditions
- Locked-period request stays Pending with no ledger entry; non-locked approval succeeds.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
