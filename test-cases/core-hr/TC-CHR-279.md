---
id: TC-CHR-279
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-279: Manager from different department can be assigned (cross-department reporting)

## 1. Test Objective
Verify that the reporting hierarchy is independent of the department hierarchy. An employee can report to a manager who belongs to a different department. This validates BR-5.

## 2. Related Requirements
- User Story: US-CHR-011
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E exists in Department "Engineering" with status `active`.
- Manager M exists in Department "Operations" with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee E | eng@acme.test | Department: Engineering |
| Manager M | ops.mgr@acme.test | Department: Operations |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee E's profile and edit the Reporting Manager field. | Manager selector opens. |
| 2 | Search for Manager M (from Operations department). | Manager M appears in results, showing "Operations" as department. |
| 3 | Select Manager M and save. | Save succeeds without any cross-department warning or block. |
| 4 | Verify Employee E's record: `reports_to_employee_id` = M.id. | FK is set correctly despite different departments. |
| 5 | Verify M's direct reports include E. | E appears in M's direct reports list, showing "Engineering" as E's department. |

## 6. Postconditions
- Employee E (Engineering) reports to Manager M (Operations).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
