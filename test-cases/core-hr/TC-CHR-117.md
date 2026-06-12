---
id: TC-CHR-117
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-117: Unauthenticated request to employee profile API returns 401

## 1. Test Objective
Verify that all employee profile endpoints require authentication. Requests without a valid JWT return 401 Unauthorized.

## 2. Related Requirements
- User Story: US-CHR-002
- Functional Requirements: FR-3, FR-7
- Preconditions in user story: "The user is authenticated with a valid tenant context"

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Employee "Jane Doe" exists in acme.
- No authentication token is provided in requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Auth Token | (none) | No JWT |
| Employee ID | {jane_doe_id} | Target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/{jane_doe_id}` with no Authorization header, but with `X-Tenant-Subdomain: acme` | Response is 401 Unauthorized. No employee data in response body. |
| 2 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with no Authorization header | Response is 401 Unauthorized. |
| 3 | Send `GET /api/v1/tenant/employees` with no Authorization header | Response is 401 Unauthorized. |
| 4 | Send requests with an expired JWT | Response is 401 Unauthorized. |
| 5 | Send requests with a malformed JWT (random string) | Response is 401 Unauthorized. |

## 6. Postconditions
- No data was exposed to unauthenticated callers.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
