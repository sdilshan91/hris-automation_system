---
id: TC-AUTH-107
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-107: Non-admin user cannot call the unlock endpoint

## 1. Test Objective
Verify that only users with the tenant admin (or equivalent `User.Manage`) permission can call the unlock endpoint. A regular employee or user without admin permissions receives a 403 Forbidden response.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-6
- Business Rules: BR-3

## 3. Preconditions
- User `alice@acme.com` is locked (`locked_until` in the future).
- User `employee@acme.com` is an active Employee-role user in tenant "acme" WITHOUT `User.Manage` permission.
- Tenant admin `admin@acme.com` has the `User.Manage` permission.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Locked user | alice@acme.com | Target of unlock |
| Non-admin user | employee@acme.com | Employee role, no unlock permission |
| Tenant admin | admin@acme.com | Has unlock permission |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `employee@acme.com`, send `POST /api/v1/tenant/users/{alice_id}/unlock`. | HTTP 403 Forbidden -- insufficient permissions. |
| 2 | Verify `alice@acme.com` remains locked (no change to `locked_until` or `failed_login_count`). | Lockout state unchanged. |
| 3 | Verify no `account_unlocked_by_admin` audit event was created. | No audit record for this attempted unlock. |
| 4 | Send the unlock request without authentication (no JWT). | HTTP 401 Unauthorized. |
| 5 | Verify `alice@acme.com` remains locked. | Lockout state unchanged. |
| 6 | As `admin@acme.com`, send `POST /api/v1/tenant/users/{alice_id}/unlock`. | HTTP 200 OK; unlock succeeds (positive control). |

## 6. Postconditions
- Only authorized admins can unlock accounts.
- Unauthorized attempts are rejected and do not affect lockout state.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
