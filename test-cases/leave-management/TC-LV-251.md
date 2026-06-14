---
id: TC-LV-251
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-251: Unauthenticated requests to report/analytics endpoints return 401

## 1. Test Objective
Verify that the report, analytics, and export endpoints reject requests with no/invalid/expired bearer token with HTTP 401 and never reach the handler or disclose data.

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-2, NFR-3
- Cross-ref: US-AUTH-* (JWT bearer)

## 3. Preconditions
- Tenant "acme"; the report/analytics/export endpoints are deployed.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Token | none / expired / malformed | unauthenticated |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/reports/utilization` with no Authorization header | 401 Unauthorized; no data, no handler execution. |
| 2 | Call `GET /api/v1/leaves/analytics/trend` with an expired token | 401 Unauthorized. |
| 3 | Call the export endpoint with a malformed token | 401 Unauthorized; no file generated. |
| 4 | Repeat with a valid token (positive control) | The endpoint authenticates and proceeds to authz — confirms the 401 is auth-based. |

## 6. Postconditions
- All report/analytics/export endpoints require authentication; unauthenticated calls are rejected with 401.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
