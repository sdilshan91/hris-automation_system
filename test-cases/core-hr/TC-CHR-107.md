---
id: TC-CHR-107
user_story: US-CHR-002
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-107: Employee attempts to PATCH a restricted field (salary) -- expects 403

## 1. Test Objective
Verify that when an Employee sends a PATCH request to modify a restricted field (salary, department, or job title), the API rejects the request with 403 Forbidden. This validates AC-5, FR-3, and BR-1.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "John Smith" is authenticated with Employee role in the "acme" tenant.
- Employee record for "John Smith" exists with salary 55000.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Limited write access |
| Employee ID | {john_smith_id} | Own profile |
| Original Salary | 55000 | Restricted field |
| Attempted Salary | 99999 | Unauthorized change |
| xmin | {current_xmin} | Concurrency token |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as John Smith (Employee role) in "acme" tenant | JWT contains employee role and tenant_id for acme. |
| 2 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "salary": 99999, "xmin": "{current_xmin}" }` | Response status is 403 Forbidden. Response body contains an error message indicating the field is restricted for the Employee role. |
| 3 | Verify the employee record in the database | Salary remains 55000. No change was applied. |
| 4 | Verify no audit log entry for this attempted change | No audit entry for a salary change on this employee. |
| 5 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "department_id": "{other_dept_id}", "xmin": "{current_xmin}" }` | Response status is 403 Forbidden. |
| 6 | Send `PATCH /api/v1/tenant/employees/{john_smith_id}` with body `{ "job_title_id": "{other_title_id}", "xmin": "{current_xmin}" }` | Response status is 403 Forbidden. |

## 6. Postconditions
- Employee record is unchanged.
- No unauthorized modifications were persisted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
