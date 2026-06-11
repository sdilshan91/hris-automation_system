---
id: TC-CHR-016
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-016: Unauthenticated request to department API returns 401

## 1. Test Objective
Verify that department API endpoints require authentication and return 401 Unauthorized when no valid JWT is provided.

## 2. Related Requirements
- User Story: US-CHR-004
- Preconditions: Section 2 (user is authenticated)
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No authentication token is provided in the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Authorization Header | (none) | No JWT |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/departments` without Authorization header | Response status is 401 Unauthorized. |
| 2 | Send `POST /api/v1/departments` with body `{ name: "Hack" }` without Authorization header | Response status is 401 Unauthorized. |
| 3 | Send `PUT /api/v1/departments/{any_id}` without Authorization header | Response status is 401 Unauthorized. |
| 4 | Send `DELETE /api/v1/departments/{any_id}` without Authorization header | Response status is 401 Unauthorized. |
| 5 | Send `GET /api/v1/departments` with an expired JWT | Response status is 401 Unauthorized. |
| 6 | Send `GET /api/v1/departments` with a malformed JWT | Response status is 401 Unauthorized. |

## 6. Postconditions
- No data was returned or modified.
- No department records were created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
