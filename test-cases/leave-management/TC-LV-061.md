---
id: TC-LV-061
user_story: US-LV-003
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-061: Unauthenticated request to the leave submission API returns 401

## 1. Test Objective
Verify that the leave application endpoints require authentication and that requests without a valid JWT are rejected with 401 Unauthorized before any business logic executes.

## 2. Related Requirements
- User Story: US-LV-003
- Preconditions: Section 2 (authenticated employee)
- Dependency: US-AUTH-* (JWT authentication)

## 3. Preconditions
- Tenant "acme" is active.
- A valid active leave type exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Resolves the tenant |
| Authorization header | (none / invalid / expired) | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/leaves` with a valid body but NO Authorization header | Response is 401 Unauthorized. No request created. |
| 2 | Send `POST /api/v1/leaves` with a malformed/garbage bearer token | Response is 401 Unauthorized. |
| 3 | Send `POST /api/v1/leaves` with an expired JWT | Response is 401 Unauthorized. |
| 4 | Send `GET` for the eligible-leave-types/balance endpoint without a token | Response is 401 Unauthorized. |
| 5 | Send the same `POST` with a valid token for an authenticated employee | Response is 201 Created -- confirms the 401s were due to missing/invalid auth only. |

## 6. Postconditions
- No leave request is created for unauthenticated requests.
- Authentication is enforced at the middleware/pipeline layer before controller logic.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
