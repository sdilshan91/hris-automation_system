---
id: TC-AUTH-123
user_story: US-AUTH-012
module: Authentication
priority: high
type: integration
status: pass
created: 2026-07-24
---

# TC-AUTH-123: SSO settings cache is invalidated on write -- the login/callback path sees the change immediately

## 1. Test Objective
Verify NFR-1: SSO settings reads are cached per tenant and the cache is invalidated on write, so the callback path (US-AUTH-013) does not pay a DB round-trip per login yet never serves stale config. After an admin updates the allow-list, the very next login-path read reflects the change immediately -- a `tid`/domain removed from the allow-list is rejected on the next SSO login attempt, and one added is accepted.

## 2. Related Requirements
- User Story: US-AUTH-012
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-8
- Dependency: US-AUTH-013 (isolation/callback consumer)

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; SSO enabled with allow-list `tid = T1`, domain `acme.com` (from TC-AUTH-115).
- The settings-read cache for acme is warm (at least one prior login-path read has populated it).
- Cache key is tenant-scoped (e.g. `t:{acme_tenant_id}:sso-settings`), consistent with the platform cache-key convention.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| initial allow-list | tid=T1, domain=acme.com | Cached |
| updated allow-list | tid=T1,T2; domain=acme.com | T2 added via admin write |
| removed value | T1 | Later removed to test negative invalidation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm the cache: trigger the login/callback path so acme's SSO settings are read and cached. | Cached copy = {T1, acme.com}. |
| 2 | As `admin-a@acme.com`, `PUT /api/v1/tenant/auth-settings` adding `tid = T2` to the allow-list. | HTTP 200 OK; write succeeds; the tenant-scoped SSO cache entry for acme is invalidated (evicted or refreshed) on write. |
| 3 | Immediately trigger an SSO login whose token `tid = T2`. | Login is accepted -- the callback path read the updated allow-list, not the stale cached {T1} set (no stale-config window). |
| 4 | As admin, `PUT` removing `tid = T1` from the allow-list. | HTTP 200 OK; cache invalidated again. |
| 5 | Immediately trigger an SSO login whose token `tid = T1`. | Login is rejected (fail-closed) -- the now-removed `T1` is no longer trusted; the change took effect immediately. |

## 6. Postconditions
- The login-path config is never stale after a write; reads remain cached between writes (no per-login DB round-trip).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
