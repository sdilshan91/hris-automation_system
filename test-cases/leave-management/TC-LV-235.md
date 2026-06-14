---
id: TC-LV-235
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-235: Utilization percentage math — 200 entitlement, 80 used → 40% (AC-2, Test Hint)

## 1. Test Objective
Verify the AC-2 Test Hint: for a department of 10 employees with 200 total entitlement days and 80 days used, the report computes utilization = used ÷ entitlement = 80 ÷ 200 = 40%. Validates the aggregation formula and rounding.

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-2
- Data Requirements: §7 (AVG utilization, SUM of days)

## 3. Preconditions
- Tenant "acme", department "Engineering" with exactly 10 employees, combined entitlement 200 days, combined used 80 days for the selected range/type.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employees | 10 | Engineering |
| Total entitlement | 200 days | sum |
| Total used | 80 days | sum |
| Expected utilization | 40% | 80/200 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the Utilization Report filtered to Engineering for the data range | Total used = 80 days; total entitlement = 200 days. |
| 2 | Read the utilization percentage | Utilization = 40.0% (80 ÷ 200), formatted per the UI rounding convention. |
| 3 | Boundary: a department with 0 used | Utilization = 0% (no divide error). |
| 4 | Boundary: a leave type with 0 entitlement (e.g. Unpaid/LOP) | Utilization handled as 0% / N-A (no divide-by-zero), consistent with the dashboard `usedFraction` guard. |

## 6. Postconditions
- Utilization percentage is computed as used ÷ entitlement (40% for the sample), with zero-entitlement guarded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
