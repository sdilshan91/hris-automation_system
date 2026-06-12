---
id: TC-CHR-284
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-284: Unauthenticated request to manager assignment and direct-reports APIs returns 401

## 1. Test Objective
Verify that unauthenticated requests to the manager assignment endpoint and the direct-reports query endpoint both return HTTP 401 Unauthorized. This validates the authentication requirement.

## 2. Related Requirements
- User Story: US-CHR-011
- Preconditions: Section 2 ("user is authenticated")
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Employee E and Manager M exist.
- No authentication token is provided in the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee E UUID | {valid-uuid} | Existing employee |
| Manager M UUID | {valid-uuid} | Existing manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `PUT /api/v1/tenant/employees/{E.id}` with `reports_to_employee_id = M.id` and NO Authorization header. | HTTP 401 Unauthorized. No body data leaks. |
| 2 | Send `GET /api/v1/tenant/employees/{M.id}/direct-reports` with NO Authorization header. | HTTP 401 Unauthorized. |
| 3 | Send the bulk assign manager request with NO Authorization header. | HTTP 401 Unauthorized. |
| 4 | Verify no state change occurred. | Employee E's record is unchanged. |

## 6. Postconditions
- No state change. All requests rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
