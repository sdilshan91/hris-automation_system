---
id: TC-AUTH-112
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-112: Unlock of an already-unlocked account is idempotent and non-destructive

## 1. Test Objective
Verify that calling the admin unlock endpoint on a user who is NOT currently locked (either never locked, or already unlocked) is handled gracefully -- it either succeeds as a no-op or returns a meaningful status -- and does not corrupt the user's state or create misleading audit events.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6

## 3. Preconditions
- User `alice@acme.com` has `failed_login_count = 0`, `locked_until = null` (not locked).
- Tenant admin `admin@acme.com` is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Target user | alice@acme.com | Not locked |
| Tenant admin | admin@acme.com | Has unlock permission |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` has `failed_login_count = 0`, `locked_until = null`. | Not locked. |
| 2 | As `admin@acme.com`, send `POST /api/v1/tenant/users/{alice_id}/unlock`. | HTTP 200 OK (idempotent) or HTTP 409/400 indicating the user is not locked. Either is acceptable as long as it does not error with 500. |
| 3 | Query `users.failed_login_count` and `users.locked_until`. | Both remain at 0 and null respectively -- no corruption. |
| 4 | Verify no spurious `account_unlocked_by_admin` audit event is created (or if one is created, it accurately reflects the action). | Audit log does not contain misleading unlock events for an account that was not locked. |
| 5 | `alice@acme.com` logs in with correct credentials. | HTTP 200 OK -- login works normally. The unlock call did not affect her ability to log in. |

## 6. Postconditions
- User state is unchanged and uncorrupted.
- No misleading audit trail.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
