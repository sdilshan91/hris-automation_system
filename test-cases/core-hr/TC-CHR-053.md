---
id: TC-CHR-053
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-053: Unauthenticated request to job titles API returns 401

## 1. Test Objective
Verify that unauthenticated requests (no JWT, expired JWT, malformed JWT) to any job title API endpoint are rejected with a 401 Unauthorized response.

## 2. Related Requirements
- User Story: US-CHR-005
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- No valid authentication token is provided in the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Auth Header (case 1) | (none) | No Authorization header |
| Auth Header (case 2) | Bearer expired_token_here | Expired JWT |
| Auth Header (case 3) | Bearer malformed_garbage | Malformed JWT |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/job-titles` with no Authorization header | Response status is 401 Unauthorized. |
| 2 | Call `POST /api/v1/job-titles` with no Authorization header | Response status is 401 Unauthorized. |
| 3 | Call `PUT /api/v1/job-titles/{any_id}` with no Authorization header | Response status is 401 Unauthorized. |
| 4 | Call `GET /api/v1/job-titles` with an expired JWT token | Response status is 401 Unauthorized. |
| 5 | Call `GET /api/v1/job-titles` with a malformed token string | Response status is 401 Unauthorized. |
| 6 | Verify the response body does not leak sensitive information (no stack traces, no internal paths) | Response contains a generic "Unauthorized" message. |

## 6. Postconditions
- No job title data was returned or modified.
- The system correctly enforced authentication requirements.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
