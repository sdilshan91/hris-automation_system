---
id: TC-CHR-281
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-281: Manager can have unlimited direct reports (no system-enforced limit)

## 1. Test Objective
Verify that there is no system-enforced limit on the number of direct reports a manager can have. Assigning a large number of employees to the same manager succeeds. This validates BR-2.

## 2. Related Requirements
- User Story: US-CHR-011
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Manager M exists with status `active` and currently has 20 direct reports.
- Employee E21 exists with no manager assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager M | mgr.many@acme.test | Already has 20 direct reports |
| Employee E21 | emp21@acme.test | 21st report to be assigned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify Manager M currently has 20 direct reports via `GET /api/v1/tenant/employees/{M.id}/direct-reports`. | Returns 20 employees. |
| 2 | Assign Employee E21 to Manager M. | Save succeeds without any "maximum direct reports exceeded" error. |
| 3 | Verify Manager M now has 21 direct reports. | `GET /api/v1/tenant/employees/{M.id}/direct-reports` returns 21 employees, including E21. |
| 4 | Verify the "My Team" view for Manager M loads correctly. | All 21 direct reports display without pagination issues or errors. |

## 6. Postconditions
- Manager M has 21 direct reports with no system-imposed cap.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
