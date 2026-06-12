---
id: TC-CHR-229
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-229: Unauthenticated request to status change API returns 401

## 1. Test Objective
Verify that sending a status change request without any authentication token returns HTTP 401 Unauthorized. This validates basic authentication enforcement on the status change endpoint.

## 2. Related Requirements
- User Story: US-CHR-009
- Business Rules: BR-2
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Employee "John Smith" (`emp-001-uuid`) exists with status `active`.
- No authentication token is provided in the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth Token | (none) | Unauthenticated |
| Employee | John Smith (emp-001-uuid) | Target employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-001-uuid/status` with body `{ "newStatus": "suspended", "reason": "Test", "effectiveDate": "2026-06-12" }` and NO Authorization header. | Response status is 401 Unauthorized. |
| 2 | Send the same request with an expired JWT token. | Response status is 401 Unauthorized. |
| 3 | Send the same request with a malformed JWT token. | Response status is 401 Unauthorized. |
| 4 | Verify employee status has not changed. | Employee status remains `active`. |

## 6. Postconditions
- Employee status remains unchanged.
- No employment history or audit entries were created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
