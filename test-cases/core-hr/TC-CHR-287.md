---
id: TC-CHR-287
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-287: Employee role cannot assign reporting managers via API

## 1. Test Objective
Verify that a user with only the Employee role cannot assign or change reporting managers for anyone (including themselves) via the API. This validates authorization enforcement.

## 2. Related Requirements
- User Story: US-CHR-011
- Preconditions: Section 2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An Employee user is authenticated.
- The employee's own record and a target Manager M exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee User | emp.user@acme.test | Has Employee role only |
| Manager M | mgr@acme.test | Active manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Employee User. | Authentication succeeds. |
| 2 | Attempt to assign Manager M as own reporting manager via API. | HTTP 403 Forbidden. |
| 3 | Attempt to assign Manager M to another employee via API. | HTTP 403 Forbidden. |
| 4 | Verify no state changes occurred. | No `reports_to_employee_id` values were modified. |

## 6. Postconditions
- No state change. Employee role has no write access to reporting structure.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
