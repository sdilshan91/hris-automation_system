---
id: TC-CHR-309
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-309: Unauthenticated request to custom fields API returns 401

## 1. Test Objective
Verify that all custom fields API endpoints require authentication. Requests without a valid JWT token return HTTP 401 Unauthorized.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- API server is running.
- No authentication token is provided in the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Authorization Header | (none) | No token provided |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /api/v1/tenant/custom-fields?entityType=Employee` without Authorization header. | HTTP 401 Unauthorized. |
| 2 | `POST /api/v1/tenant/custom-fields` with valid body but no Authorization header. | HTTP 401 Unauthorized. |
| 3 | `PUT /api/v1/tenant/custom-fields/{id}` without Authorization header. | HTTP 401 Unauthorized. |
| 4 | `POST /api/v1/tenant/custom-fields/{id}/deactivate` without Authorization header. | HTTP 401 Unauthorized. |
| 5 | Send request with an expired JWT token to `GET /api/v1/tenant/custom-fields`. | HTTP 401 Unauthorized. |

## 6. Postconditions
- No data is returned or modified. All requests are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
