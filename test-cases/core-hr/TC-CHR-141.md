---
id: TC-CHR-141
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-141: Unauthenticated request to directory API returns 401

## 1. Test Objective
Verify that the employee directory and export endpoints require authentication and return 401 Unauthorized when accessed without a valid JWT token.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No authentication token is provided.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth header | (missing) | No Bearer token |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/directory` without Authorization header | Response is 401 Unauthorized. |
| 2 | Send `GET /api/v1/tenant/employees/directory` with an expired JWT | Response is 401 Unauthorized. |
| 3 | Send `GET /api/v1/tenant/employees/directory` with a malformed JWT | Response is 401 Unauthorized. |
| 4 | Send `GET /api/v1/tenant/employees/directory/export?format=Csv` without Authorization header | Response is 401 Unauthorized. No file download occurs. |
| 5 | Verify response body does not leak tenant or employee data | Error response contains only a generic error message; no employee data in the body. |

## 6. Postconditions
- No data was exposed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
