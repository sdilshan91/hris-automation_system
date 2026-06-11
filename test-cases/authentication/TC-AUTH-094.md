---
id: TC-AUTH-094
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-094: System admin can unlock any user regardless of tenant

## 1. Test Objective
Verify that a system admin (System Super Admin) can unlock any locked user account regardless of which tenant the user belongs to (BR-4).

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-4
- Functional Requirements: FR-6, FR-7

## 3. Preconditions
- User `alice@acme.com` (tenant "acme") is locked.
- User `bob@globex.com` (tenant "globex") is locked.
- System admin `sysadmin@yourhrm.com` is authenticated on `admin.yourhrm.com`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| System admin | sysadmin@yourhrm.com | System Super Admin |
| System subdomain | admin.yourhrm.com | System context |
| Locked user A | alice@acme.com | Tenant "acme" |
| Locked user B | bob@globex.com | Tenant "globex" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` and `bob@globex.com` are both locked (`locked_until` in the future). | Precondition verified. |
| 2 | As `sysadmin@yourhrm.com` on `admin.yourhrm.com`, send the unlock request for `alice@acme.com`. | HTTP 200 OK; unlock succeeds. |
| 3 | Verify `alice@acme.com` has `locked_until = null`, `failed_login_count = 0`. | Counters cleared. |
| 4 | Verify an `account_unlocked_by_admin` audit event with the system admin's user_id. | Audit event recorded. |
| 5 | As `sysadmin@yourhrm.com`, send the unlock request for `bob@globex.com`. | HTTP 200 OK; unlock succeeds for a user in a different tenant. |
| 6 | Verify `bob@globex.com` has `locked_until = null`, `failed_login_count = 0`. | Counters cleared. |
| 7 | Verify the audit event for bob's unlock also records the system admin's user_id. | Audit event recorded. |
| 8 | `alice@acme.com` attempts login with correct credentials via `acme.yourhrm.com`. | HTTP 200 OK; login succeeds immediately. |
| 9 | `bob@globex.com` attempts login with correct credentials via `globex.yourhrm.com`. | HTTP 200 OK; login succeeds immediately. |

## 6. Postconditions
- Both users are unlocked by the system admin regardless of their tenant membership.
- `account_unlocked_by_admin` audit events are recorded for each unlock.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
