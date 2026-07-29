---
id: TC-AUTH-134
user_story: US-AUTH-016
module: Authentication
priority: high
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-134: Reverting `sso_only` -> `optional` immediately restores local login for all users without data loss

## 1. Test Objective
Verify FR-8 / BR-5: a tenant admin can revert enforcement from `sso_only` back to `optional` at any time. On revert, ordinary users can immediately log in with local email/password again -- no data loss, no re-configuration, and the change takes effect on the login path immediately (cached setting invalidated). SSO config (allow-list, captured `tid`) is preserved so SSO can be re-enabled later.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-1 (reverse of enforcement)
- Functional Requirements: FR-8
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" is on `sso_only` (from TC-AUTH-126) with a valid SSO config and a designated break-glass admin.
- `user-a@acme.com` (ordinary user, valid local creds) is currently refused local login under `sso_only`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Write endpoint | PUT /api/v1/tenant/auth-settings | Revert enforcement |
| Change | `enforcement_mode = optional` | From `sso_only` |
| Ordinary user | user-a@acme.com / correct password | Should log in after revert |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm baseline: `user-a@acme.com` local `POST /api/v1/auth/login` under `sso_only`. | Refused (per TC-AUTH-126). |
| 2 | As a tenant admin, `PUT /api/v1/tenant/auth-settings` setting `enforcement_mode = optional`. | HTTP 200; `enforcement_mode = optional` persists. The cached tenant setting is invalidated (NFR-4 path). |
| 3 | Immediately (no restart, no cache wait) re-attempt `user-a@acme.com` local login. | Login SUCCEEDS (HTTP 200) with a normal JWT + refresh token -- local login restored for everyone immediately (BR-5). |
| 4 | Re-read `GET /api/v1/tenant/auth-settings`. | SSO config is intact: allow-list `tid`/domains, `jit_default_role`, captured customer `tid`, and `break_glass_admin_user_ids` are unchanged (no data loss, FR-8). SSO can be re-enabled later without redoing consent. |
| 5 | Confirm the enforcement change was audited. | An `sso_enforcement_changed` record exists for the `sso_only -> optional` transition (see TC-AUTH-135). |

## 6. Postconditions
- acme is back on `optional`; all users can log in locally; SSO config preserved for future re-enable.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
