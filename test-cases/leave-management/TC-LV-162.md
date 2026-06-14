---
id: TC-LV-162
user_story: US-LV-008
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-162: Unauthenticated request to the carry-forward preview API returns 401 (NFR-2)

## 1. Test Objective
Verify that the carry-forward preview endpoint rejects unauthenticated requests with 401 Unauthorized -- a request with no token or an invalid/expired token is denied before any projection is computed (NFR-2).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-2
- Preconditions (Section 2)
- Cross-reference: US-AUTH-002 (JWT)

## 3. Preconditions
- Tenant "acme" with carry-forward configuration present.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No Authorization header | -- | anonymous |
| Invalid/expired bearer token | -- | tampered |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/carry-forward-preview?year=2026` with no Authorization header | 401 Unauthorized; no projection returned. |
| 2 | Call the endpoint with an expired bearer token | 401 Unauthorized. |
| 3 | Call the endpoint with a malformed/tampered token | 401 Unauthorized (signature validation fails). |
| 4 | Confirm no side effects | No data is computed or returned; the request is rejected at the auth layer. |

## 6. Postconditions
- Anonymous and invalid-token requests to the preview API are rejected with 401.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
