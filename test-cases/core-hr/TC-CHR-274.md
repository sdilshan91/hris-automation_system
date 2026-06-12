---
id: TC-CHR-274
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-274: Self-assignment rejected -- employee cannot report to themselves

## 1. Test Objective
Verify that the system rejects an attempt to assign an employee as their own reporting manager. This validates BR-7.

## 2. Related Requirements
- User Story: US-CHR-011
- Business Rules: BR-7
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E exists with status `active` and no current manager.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee E | selfref@acme.test | Attempt to assign as own manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee E's profile, Employment Details section. | Reporting Manager field shows "Not Assigned". |
| 2 | Click edit on the Reporting Manager field. | Manager selector opens. |
| 3 | Search for Employee E in the autocomplete. | Ideally, Employee E is excluded from the search results (UI prevention). If not filtered in UI, select E. |
| 4 | If E was selectable: attempt to save (E reports to E). | The system rejects with an error message indicating self-assignment is not allowed (e.g., "An employee cannot be assigned as their own reporting manager."). |
| 5 | Verify via API: `PUT/PATCH` assigning `reports_to_employee_id` = E.id on Employee E. | API returns 400 Bad Request with a clear error message about self-assignment being prohibited. |
| 6 | Verify Employee E's record is unchanged. | `reports_to_employee_id` remains null. |

## 6. Postconditions
- No state change. Employee E has no manager assigned.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
