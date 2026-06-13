---
id: TC-LV-034
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-034: Department transfer mid-year triggers pro-rata recalculation for both periods

## 1. Test Objective
Verify that when an employee transfers departments mid-year, their leave entitlement is recalculated pro-rata for both the old department period and the new department period, and the total reflects the correct combined entitlement.

## 2. Related Requirements
- User Story: US-LV-002
- Business Rules: BR-5
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with leave year starting January 1.
- Leave type "Annual Leave" exists and is active.
- Entitlement rule A: "Annual Leave" + Department "Engineering" = 25 days/year.
- Entitlement rule B: "Annual Leave" + Department "Marketing" = 20 days/year.
- Employee "Transfer Tom" is in Engineering, joined 2026-01-01 (full year).
- Tom currently has 25.00 days annual leave balance.

## 4. Test Data
| Period | Department | Rule Days | Duration | Pro-Rata |
|--------|-----------|-----------|----------|----------|
| Jan 1 -- Jun 30 | Engineering | 25 | 6/12 | 12.50 |
| Jul 1 -- Dec 31 | Marketing | 20 | 6/12 | 10.00 |
| **Total** | | | 12/12 | **22.50** |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Tom's current balance for "Annual Leave" = 25.00 days (full year Engineering) | Balance confirmed as 25.00. |
| 2 | Transfer Tom from Engineering to Marketing with effective date 2026-07-01 | Department transfer recorded in Core HR. |
| 3 | Observe that a Hangfire recalculation job is triggered for Tom | Job enqueued for entitlement recalculation. |
| 4 | Wait for the Hangfire job to complete | Job status = Succeeded. |
| 5 | Query Tom's updated leave balance for "Annual Leave" | Balance = 22.50 days (12.50 from Engineering Jan-Jun + 10.00 from Marketing Jul-Dec). |
| 6 | Verify `leave_ledger` entries reflect the adjustment | Adjustment entry: -2.50 days (from 25.00 to 22.50) with reason indicating department transfer recalculation. |
| 7 | Verify Tom's leave balance breakdown (if UI supports it) shows two periods | Period 1: Engineering, Jan-Jun, 12.50 days. Period 2: Marketing, Jul-Dec, 10.00 days. |
| 8 | Verify another Engineering employee (same level) still has 25.00 days | Other employees unaffected by Tom's transfer. |
| 9 | Verify audit log records the entitlement recalculation triggered by department transfer | Audit entry with reason = "department_transfer" and before/after values. |

## 6. Postconditions
- Tom's leave entitlement reflects the weighted combination of both department rules.
- `leave_ledger` has adjustment entries reflecting the recalculation.
- The recalculation was triggered automatically by the department transfer event.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
