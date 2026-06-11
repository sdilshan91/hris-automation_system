---
id: TC-AUTH-087
user_story: US-AUTH-010
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-087: Admin manual unlock clears counters, logs audit, and enables immediate login

## 1. Test Objective
Verify that a tenant admin can manually unlock a locked user account via the unlock endpoint, that the unlock sets `locked_until = null` and `failed_login_count = 0`, logs an `account_unlocked_by_admin` audit event with the admin's user ID, and allows the user to log in immediately.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6, FR-7
- Data Requirements: `account_unlocked_by_admin` audit record

## 3. Preconditions
- User `alice@acme.com` is locked: `locked_until` is approximately 10 minutes in the future, `failed_login_count = 5`.
- Tenant admin `admin@acme.com` has the `User.Manage` (or equivalent) permission in tenant "acme."
- `alice@acme.com` has a membership in tenant "acme."

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Locked user | alice@acme.com | Locked account in tenant "acme" |
| Tenant admin | admin@acme.com | Has unlock permission |
| Tenant subdomain | acme | Context for admin action |
| locked_until | now() + 10 minutes | Active lockout |
| failed_login_count | 5 | At threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` is locked: `locked_until` is in the future, `failed_login_count = 5`. | Precondition verified. |
| 2 | As `admin@acme.com`, send `POST /api/v1/tenant/users/{alice_user_id}/unlock` (or the designated unlock endpoint). | HTTP 200 OK; unlock succeeds. |
| 3 | Query `users.failed_login_count` for `alice@acme.com`. | Value is `0`. |
| 4 | Query `users.locked_until` for `alice@acme.com`. | Value is `null`. |
| 5 | Query the audit log for an `account_unlocked_by_admin` event. | Event exists with: `user_id` (alice), `admin_user_id` (admin@acme.com's ID), and timestamp. |
| 6 | Verify the audit event is recorded in both tenant audit log and system audit log. | Present in both logs per FR-7. |
| 7 | Immediately (without waiting) send `POST /api/v1/auth/login` as `alice@acme.com` with correct credentials. | HTTP 200 OK; login succeeds. Tokens are issued. |
| 8 | Verify `failed_login_count` remains 0 after the successful login. | Value is `0`. |

## 6. Postconditions
- `alice@acme.com` is fully unlocked: `failed_login_count = 0`, `locked_until = null`.
- `account_unlocked_by_admin` audit event is recorded.
- User can log in normally.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
