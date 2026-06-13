---
id: TC-LV-101
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-101: Unauthenticated request to the approve/reject API returns 401

## 1. Test Objective
Verify that the approve and reject endpoints require authentication: a request with no (or an invalid/expired) bearer token returns 401 Unauthorized and performs no state change.

## 2. Related Requirements
- User Story: US-LV-005
- Preconditions (Section 2): Manager is authenticated
- Related: US-AUTH-* (JWT bearer auth)

## 3. Preconditions
- Tenant "acme" is active; a pending request R exists.
- No valid authentication token is supplied.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Authorization header | _absent / invalid / expired_ | Three variants |
| Request R | Pending | Target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{R}/approve` with no Authorization header | 401 Unauthorized. |
| 2 | `POST /api/v1/leaves/{R}/reject` with an expired token | 401 Unauthorized. |
| 3 | `POST /api/v1/leaves/{R}/approve` with a malformed/invalid token | 401 Unauthorized. |
| 4 | Inspect R after the attempts | `status` remains `Pending`; no side effects. |

## 6. Postconditions
- All unauthenticated attempts rejected with 401; R unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
