---
id: TC-LV-083
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-083: Unauthenticated request to the pending queue API returns 401

## 1. Test Objective
Verify that the pending leave queue endpoint requires authentication: a request with no valid JWT (missing, malformed, or expired) is rejected with 401 Unauthorized before any tenant or scope resolution.

## 2. Related Requirements
- User Story: US-LV-004
- Preconditions: Section 2 (authenticated)
- Related: US-AUTH-* (authentication)

## 3. Preconditions
- Tenant "acme" is active.
- The pending queue endpoint `GET /api/v1/leaves/pending` exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No token | (Authorization header absent) | Unauthenticated |
| Malformed token | "Bearer not.a.jwt" | Invalid |
| Expired token | A previously valid but expired JWT | Expired |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/pending` with no Authorization header | Response 401 Unauthorized; no queue data returned. |
| 2 | Call the endpoint with a malformed bearer token | Response 401 Unauthorized. |
| 3 | Call the endpoint with an expired JWT | Response 401 Unauthorized. |
| 4 | Verify no data leakage in the 401 body | The error body contains no pending request data or internal details. |
| 5 | Confirm a valid token succeeds (control) | A valid manager token returns 200 -- the 401 is due to missing/invalid auth. |

## 6. Postconditions
- No data mutated.
- The pending queue endpoint is inaccessible without valid authentication.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
