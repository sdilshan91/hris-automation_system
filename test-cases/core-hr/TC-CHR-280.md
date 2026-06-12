---
id: TC-CHR-280
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-280: Employee can have at most one direct reporting manager

## 1. Test Objective
Verify that assigning a new manager to an employee who already has a manager replaces the previous manager, not adds a second one. Only one `reports_to_employee_id` value is stored. This validates BR-1.

## 2. Related Requirements
- User Story: US-CHR-011
- Business Rules: BR-1
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E exists with Manager M1 assigned (`reports_to_employee_id` = M1.id).
- Manager M2 exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee E | emp@acme.test | Currently reports to M1 |
| Manager M1 | mgr1@acme.test | Current manager |
| Manager M2 | mgr2@acme.test | New manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Employee E currently has `reports_to_employee_id` = M1.id. | Confirmed via API. |
| 2 | Navigate to Employee E's profile and edit the Reporting Manager field. | Manager selector shows M1 as the current selection. |
| 3 | Select Manager M2 and save. | Save succeeds. |
| 4 | Verify Employee E's record: `GET /api/v1/tenant/employees/{E.id}`. | `reports_to_employee_id` = M2.id (not M1.id). Only one FK value stored. |
| 5 | Verify M1's direct reports no longer include E. | E is removed from M1's direct-reports response. |
| 6 | Verify M2's direct reports include E. | E appears in M2's direct-reports response. |

## 6. Postconditions
- Employee E reports to M2 only. Previous manager M1 no longer has E as a report.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
