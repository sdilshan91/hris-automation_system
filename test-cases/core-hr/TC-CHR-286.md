---
id: TC-CHR-286
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-286: Manager role cannot assign reporting managers via API

## 1. Test Objective
Verify that a user with the Manager role (but not HR Officer or Tenant Admin) cannot assign or change reporting managers via the API. The Manager role can view direct reports (AC-4) but cannot modify reporting structure. This validates the authorization boundary.

## 2. Related Requirements
- User Story: US-CHR-011
- Preconditions: Section 2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A Manager user is authenticated.
- Employee E exists with no manager assigned.
- Manager M exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager User | mgr.user@acme.test | Has Manager role only |
| Employee E | emp@acme.test | No manager |
| Manager M | mgr.target@acme.test | Target manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Manager User. | Authentication succeeds. |
| 2 | Send API request to assign M as E's manager. | HTTP 403 Forbidden. Response indicates insufficient permissions. |
| 3 | Verify Employee E's record is unchanged. | `reports_to_employee_id` remains null. |
| 4 | Send `GET /api/v1/tenant/employees/{mgr.user.id}/direct-reports`. | 200 OK. Manager can read their own direct reports (AC-4 is a read operation). |

## 6. Postconditions
- No state change. Manager role can read but not modify reporting structure.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
