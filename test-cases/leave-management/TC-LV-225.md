---
id: TC-LV-225
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-225: Unauthenticated requests to LOP endpoints return 401

## 1. Test Objective
Verify that the LOP endpoints (`assign-lop`, `lop-summary`, compulsory-leave bulk-assign, override) reject requests with no/invalid bearer token with HTTP 401 and never reach the handler.

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-2 (security baseline)
- Cross-ref: US-AUTH-* (JWT bearer)

## 3. Preconditions
- Tenant "acme"; the LOP endpoints are deployed.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Token | none / expired / malformed | unauthenticated |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST /api/v1/leaves/assign-lop` with no Authorization header | 401 Unauthorized; no handler execution, no persistence. |
| 2 | Call `GET /api/v1/leaves/lop-summary?...` with an expired token | 401 Unauthorized; no LOP data disclosed. |
| 3 | Call the compulsory-leave bulk-assign endpoint with a malformed token | 401 Unauthorized. |
| 4 | Repeat with a valid token (positive control) | The endpoint authenticates and proceeds to authz — confirms the 401 is auth-based. |

## 6. Postconditions
- All LOP endpoints require authentication; unauthenticated calls are rejected with 401.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
