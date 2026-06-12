---
id: TC-CHR-162
user_story: US-CHR-006
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-162: Unauthenticated request to org-tree API returns 401

## 1. Test Objective
Verify that an unauthenticated request (no JWT or expired JWT) to the org-tree API endpoint returns HTTP 401 Unauthorized. This validates FR-8 and security requirements.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No valid authentication token is available for the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | GET /api/v1/org-tree?view=department&depth=2 | Org tree API |
| Authorization Header | (none) | No JWT |
| Expired JWT | eyJ... (expired token) | Alternative test |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/org-tree?view=department&depth=2` without an Authorization header | Response status is 401 Unauthorized. Response body contains a generic error message (no sensitive data leaked). |
| 2 | Send `GET /api/v1/org-tree?view=department&depth=2` with an expired JWT in the Authorization header | Response status is 401 Unauthorized. |
| 3 | Send `GET /api/v1/org-tree?view=department&depth=2` with a malformed JWT (e.g., "Bearer invalid-token") | Response status is 401 Unauthorized. |
| 4 | Verify no org-tree data is returned in any of the 401 responses | Response body does not contain department names, employee names, or any tree data. |

## 6. Postconditions
- No data was exposed to the unauthenticated caller.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
