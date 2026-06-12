---
id: TC-CHR-091
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-091: Unauthenticated request to employee API returns 401

## 1. Test Objective
Verify that all employee API endpoints require authentication. Unauthenticated requests (no JWT, expired JWT, malformed JWT) should return 401 Unauthorized.

## 2. Related Requirements
- User Story: US-CHR-001
- Preconditions: "HR Officer is authenticated"
- Authentication module dependency

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No valid authentication token is available for the test requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Authorization header | (none) | No token |
| Expired token | expired-jwt-token | Expired JWT |
| Malformed token | not-a-jwt | Invalid format |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees` with no Authorization header | 401 Unauthorized. |
| 2 | Send `POST /api/v1/tenant/employees` with no Authorization header | 401 Unauthorized. |
| 3 | Send `GET /api/v1/tenant/employees/{id}` with expired JWT | 401 Unauthorized. |
| 4 | Send `POST /api/v1/tenant/employees` with malformed token "not-a-jwt" | 401 Unauthorized. |
| 5 | Verify response bodies do not leak internal error details | Generic error message only; no stack traces or system information. |

## 6. Postconditions
- No employee data is exposed to unauthenticated requesters.
- No employee records are created or modified.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
