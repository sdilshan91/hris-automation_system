---
id: TC-AUTH-024
user_story: US-AUTH-009
module: Authentication
priority: high
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-024: Concurrent session limit enforced

## 1. Test Objective
Verify that when a tenant's session policy enforces a maximum concurrent session limit, a user who exceeds that limit is either denied login or has their oldest session revoked, depending on the configured strategy.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5

## 3. Preconditions
- Tenant "acme" has session policy configured with `maxConcurrentSessions = 3` and `concurrentSessionStrategy = "deny_new"`.
- User `john@acme.com` already has 3 active sessions (3 non-revoked, non-expired refresh tokens).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Has 3 active sessions |
| Tenant | acme | maxConcurrentSessions = 3 |
| Strategy (Test 1) | deny_new | Deny the 4th login |
| Strategy (Test 2) | revoke_oldest | Revoke oldest session |
| Session count | 3 | Currently at limit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify user has exactly 3 active (non-revoked, non-expired) refresh tokens | `refresh_token` table shows 3 active records for this user-tenant pair. |
| 2 | **Strategy: deny_new** -- Attempt login from a 4th device/browser | HTTP 403 or 409 with message "Maximum concurrent sessions reached. Please log out from another device or contact your administrator." |
| 3 | Verify no new tokens are issued | No access token or refresh token created. |
| 4 | Verify all 3 existing sessions remain active | No `revoked_at` set on any existing tokens. |
| 5 | Verify a `concurrent_session_denied` audit event is logged | Audit record contains user_id, tenant_id, session count, strategy. |
| 6 | Change strategy to `revoke_oldest`: update tenant policy to `concurrentSessionStrategy = "revoke_oldest"` | Policy updated. |
| 7 | Attempt login from a 4th device/browser | HTTP 200; login succeeds with new tokens. |
| 8 | Verify the oldest session (by `issued_at`) is revoked | The oldest refresh token now has `revoked_at` set. |
| 9 | Verify user now has exactly 3 active sessions (3 remaining including the new one) | Session count is maintained at the limit. |
| 10 | Verify a `concurrent_session_oldest_revoked` audit event is logged | Audit record contains the revoked session ID and the new session ID. |

## 6. Postconditions
- With "deny_new" strategy: login is blocked; session count stays at the limit.
- With "revoke_oldest" strategy: oldest session is revoked; new session is created; count stays at limit.
- Appropriate audit events are recorded.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
