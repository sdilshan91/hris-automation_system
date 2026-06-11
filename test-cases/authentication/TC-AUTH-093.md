---
id: TC-AUTH-093
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-093: Tenant admin can only unlock users with membership in their own tenant

## 1. Test Objective
Verify that a tenant admin can only unlock users who have an active membership in their tenant (BR-3). Attempting to unlock a user who does not belong to the admin's tenant must be rejected.

## 2. Related Requirements
- User Story: US-AUTH-010
- Business Rules: BR-3
- Functional Requirements: FR-6

## 3. Preconditions
- User `alice@acme.com` belongs to tenant "acme" only and is locked (`locked_until` in the future).
- User `bob@globex.com` belongs to tenant "globex" only and is locked.
- `admin@acme.com` is a tenant admin for "acme."
- `admin@globex.com` is a tenant admin for "globex."

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acme admin | admin@acme.com | Tenant Admin for "acme" |
| Globex admin | admin@globex.com | Tenant Admin for "globex" |
| Acme user (locked) | alice@acme.com | Membership in "acme" only |
| Globex user (locked) | bob@globex.com | Membership in "globex" only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `admin@acme.com`, send `POST /api/v1/tenant/users/{alice_id}/unlock`. | HTTP 200 OK; unlock succeeds. `alice@acme.com` has `locked_until = null`, `failed_login_count = 0`. |
| 2 | Verify `account_unlocked_by_admin` audit event for alice. | Audit event logged with admin_user_id of `admin@acme.com`. |
| 3 | As `admin@acme.com`, send `POST /api/v1/tenant/users/{bob_id}/unlock` (bob is in "globex," not "acme"). | HTTP 403 Forbidden or HTTP 404 Not Found -- the acme admin cannot see or unlock users outside their tenant. |
| 4 | Verify `bob@globex.com` remains locked: `locked_until` still in the future, `failed_login_count` unchanged. | No change to bob's lockout state. |
| 5 | As `admin@globex.com`, send `POST /api/v1/tenant/users/{bob_id}/unlock`. | HTTP 200 OK; unlock succeeds for bob within their own tenant context. |
| 6 | As `admin@globex.com`, attempt to unlock `alice@acme.com` (already unlocked, but testing cross-tenant access). | HTTP 403 or 404 -- globex admin cannot access acme users. |

## 6. Postconditions
- Each admin can only unlock users within their own tenant.
- Cross-tenant unlock attempts are rejected.
- Audit events are only created for successful unlocks.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
