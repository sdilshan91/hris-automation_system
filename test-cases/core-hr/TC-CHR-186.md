---
id: TC-CHR-186
user_story: US-CHR-007
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-186: Unauthenticated request to location API returns 401

## 1. Test Objective
Verify that all location CRUD API endpoints require authentication. Requests without a valid JWT token should be rejected with HTTP 401 Unauthorized. This validates basic authentication enforcement.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-1, FR-8
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No authentication token is provided in the requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Authorization Header | (absent or invalid) | No valid JWT |
| Location ID | Any valid UUID | For single-resource endpoints |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/locations` without Authorization header | Response status is 401 Unauthorized. No location data is returned. |
| 2 | Send `GET /api/v1/tenant/locations/{id}` without Authorization header | Response status is 401 Unauthorized. |
| 3 | Send `POST /api/v1/tenant/locations` with a valid JSON body but no Authorization header | Response status is 401 Unauthorized. No location is created. |
| 4 | Send `PUT /api/v1/tenant/locations/{id}` without Authorization header | Response status is 401 Unauthorized. |
| 5 | Send `POST /api/v1/tenant/locations/{id}/deactivate` without Authorization header | Response status is 401 Unauthorized. |
| 6 | Send `GET /api/v1/tenant/locations` with an expired JWT token | Response status is 401 Unauthorized. |
| 7 | Send `GET /api/v1/tenant/locations` with a malformed JWT token ("Bearer invalid-token") | Response status is 401 Unauthorized. |
| 8 | Verify no response body leaks location data or internal details | Error responses contain a generic "Unauthorized" message, not location data or stack traces. |

## 6. Postconditions
- No data was accessed or modified.
- No information leakage occurred in error responses.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
