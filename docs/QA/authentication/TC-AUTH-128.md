---
id: TC-AUTH-128
user_story: US-AUTH-016
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-128: Break-glass path stays functional under `sso_only` even when Entra / the vendor app / the allow-list is unreachable (anti-lockout resilience)

## 1. Test Objective
Verify NFR-1 / FR-2 / BR-1: the break-glass local-credential path has no runtime dependency on any external identity component. With Entra (or the vendor multi-tenant app, or the allow-list lookup) misconfigured or unreachable, a designated break-glass admin can STILL log in locally under `sso_only`, guaranteeing a tenant can never be locked out. The break-glass login is still audited (`break_glass_login`) even while the SSO path is failing.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-2 (resilience aspect)
- Functional Requirements: FR-2, FR-4
- Non-Functional Requirements: NFR-1
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is on `sso_only` with a designated break-glass admin `admin-a@acme.com`.
- A fault is injected so the SSO dependency is unavailable: Entra authority unreachable / vendor app returns an error / the allow-list store is down. (Simulate via network block or a fault stub.)

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Fault | Entra/vendor-app/allow-list unreachable | Only the SSO path is degraded |
| Break-glass admin | admin-a@acme.com / correct password | In `break_glass_admin_user_ids` |
| SSO login attempt | GET /api/v1/auth/sso/authorize | Expected to fail while fault is active |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the fault active, an ordinary user attempts the Microsoft SSO path. | The SSO login fails/errors (dependency unreachable). Fail-closed: no token is issued via SSO. |
| 2 | The designated break-glass admin follows the "Administrator sign-in" path and `POST /api/v1/auth/login` with valid local credentials. | Login PERMITTED (HTTP 200) -- the break-glass path does NOT consult Entra/vendor app/allow-list. A valid app JWT + refresh token are issued. The tenant is not locked out (BR-1, NFR-1). |
| 3 | Inspect the audit log. | A `break_glass_login` record is still written for admin-a (audit does not depend on the SSO subsystem being healthy). |
| 4 | Restore the SSO dependency; retry the ordinary user's SSO login. | SSO now succeeds again -- confirming step 1's failure was the injected fault, not a break in break-glass isolation. |

## 6. Postconditions
- Break-glass login succeeded while SSO was down; the tenant retained an admin entry path throughout.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
