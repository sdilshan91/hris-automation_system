---
id: TC-LV-206
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-206: Unauthenticated cancellation request is rejected with 401 (security; NFR-2)

## 1. Test Objective
Verify that the cancellation endpoint requires authentication: a request to `POST /api/v1/leaves/{id}/cancel` without a valid JWT (or with an expired/invalid token) is rejected with 401 Unauthorized before any state change, ledger write, or notification (NFR-2, US-AUTH-*).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-2
- Cross-reference: US-AUTH-* (authentication)

## 3. Preconditions
- Tenant "acme".
- A valid PENDING request R exists for employee "Jane Smith".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Token (a) | (none) | no Authorization header |
| Token (b) | expired JWT | invalid |
| Token (c) | tampered JWT | invalid signature |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{R}/cancel` with NO Authorization header | 401 Unauthorized; no state change. |
| 2 | Repeat with an expired JWT | 401 Unauthorized. |
| 3 | Repeat with a tampered/invalid-signature JWT | 401 Unauthorized (signature validation fails). |
| 4 | Inspect R | Unchanged -- still Pending; no `cancelled_at`, no ledger row, no notification queued, no audit row. |

## 6. Postconditions
- Cancellation is unreachable without valid authentication; R is untouched by all unauthenticated attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
