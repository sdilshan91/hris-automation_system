---
id: TC-CHR-114
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-114: Manager cannot edit direct report's profile -- read-only enforced

## 1. Test Objective
Verify that a Manager who can view a direct report's profile cannot modify any fields via the API. PATCH requests from a Manager role must be rejected with 403 Forbidden. This validates FR-3 and BR-3.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-1 (view-only for Manager)
- Functional Requirements: FR-3
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- User "Maria" is authenticated with Manager role in "acme".
- Employee "Jane Doe" is a direct report of Maria.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Manager | Read-only for reports |
| Manager | Maria | Jane Doe's manager |
| Employee ID | {jane_doe_id} | Direct report |
| Attempted Phone | 555-HACK | Unauthorized edit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Maria (Manager role) in "acme" tenant | JWT contains manager role and tenant_id for acme. |
| 2 | Send `GET /api/v1/tenant/employees/{jane_doe_id}` | Response is 200 OK with profile data (read access is allowed). |
| 3 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with body `{ "phone": "555-HACK", "xmin": "{current_xmin}" }` | Response is 403 Forbidden. Error message indicates Manager role does not have write access. |
| 4 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with body `{ "department_id": "{other_dept}", "xmin": "{current_xmin}" }` | Response is 403 Forbidden. |
| 5 | Verify employee record is unchanged in database | Phone and department are the same as before. |

## 6. Postconditions
- No modifications were applied.
- Manager read access is confirmed; write access is denied.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
