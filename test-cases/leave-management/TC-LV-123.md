---
id: TC-LV-123
user_story: US-LV-006
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-123: Unauthenticated request to balance/ledger/upcoming APIs returns 401

## 1. Test Objective
Verify that the dashboard data endpoints require authentication: requests without a valid bearer token are rejected with 401 Unauthorized and leak no balance data (NFR-3, Preconditions Section 2).

## 2. Related Requirements
- User Story: US-LV-006
- Preconditions (Section 2): Employee is authenticated with an active employee record
- Non-Functional Requirements: NFR-3
- Related: US-AUTH-*

## 3. Preconditions
- Tenant "acme" active. Endpoints `my-balance`, `my-ledger`, `my-upcoming` exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Token | none / expired / malformed | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/my-balance` with no Authorization header | 401 Unauthorized; no balance payload returned. |
| 2 | Call `GET /api/v1/leaves/my-ledger?leaveTypeId={x}&year=2026` with an expired token | 401 Unauthorized. |
| 3 | Call `GET /api/v1/leaves/my-upcoming` with a malformed token | 401 Unauthorized. |
| 4 | Inspect error bodies | Generic auth error; no employee, balance, or ledger details leaked. |

## 6. Postconditions
- All three endpoints enforce authentication.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
