---
id: TC-AUTH-ISO-006
user_story: US-AUTH-016
module: Authentication
priority: critical
type: security
status: pass
created: 2026-07-24
---

# TC-AUTH-ISO-006: SSO enforcement & onboarding are strictly tenant-scoped -- one tenant's `sso_only`/onboarding never affects another tenant's login

## 1. Test Objective
Verify BR-6 / AC-1: enforcement mode, break-glass designation, and admin-consent onboarding are per tenant. Tenant A being on `sso_only` must NOT block, alter, or leak into tenant B's login (B stays on `optional` with local login working), and vice versa. A captured customer `tid` from A's consent must never appear in B's allow-list. Tenant scoping holds even against a forged tenant identifier (header/claim/id injection) because enforcement is evaluated from the resolved tenant's own cached setting, backed by the EF Core global query filter on `TenantId` and (platform reality) the `TenantInterceptor` write-stamp -- not Postgres RLS, which is deferred; a cross-tenant id injection therefore yields 404-not-found, not another tenant's data.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-1
- Business Rules: BR-6
- Functional Requirements: FR-1, FR-6
- Non-Functional Requirements: NFR-4 (cached per-tenant setting)

## 3. Preconditions
- Two active tenants: "acme" (acme.yourhrm.com, tenant A) and "globex" (globex.yourhrm.com, tenant B), both `Sso = true`.
- Tenant A: `enforcement_mode = sso_only`, designated break-glass `admin-a@acme.com`, consent completed capturing `tid = C1`.
- Tenant B: `enforcement_mode = optional`, SSO not enforced, ordinary local login working; `user-b@globex.com` has valid local creds.
- `admin-a@acme.com` admin in A; `admin-b@globex.com` admin in B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A captured tid | cccccccc-cccc-cccc-cccc-cccccccccccc | acme allow-list only |
| Forged header | X-Tenant-Subdomain / X-Tenant-Id = acme | Sent from a globex session |
| A settings row id | {acme_settings_id} | For direct-id injection |
| Endpoints | POST /api/v1/auth/login, GET/PUT /api/v1/tenant/auth-settings | Login + settings |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **B unaffected by A's enforcement:** As `user-b@globex.com` at globex.yourhrm.com, `POST /api/v1/auth/login` with valid local creds. | Login SUCCEEDS (HTTP 200) -- globex is `optional`; acme's `sso_only` does not leak across tenants (BR-6). |
| 2 | **A still enforced:** As `user-a@acme.com` at acme.yourhrm.com, local `POST /api/v1/auth/login`. | Refused with the "requires Microsoft" message -- A's enforcement applies to A only. |
| 3 | **Settings read isolation:** As `admin-b@globex.com`, `GET /api/v1/tenant/auth-settings`. | Shows globex's `enforcement_mode = optional`, empty break-glass/onboarding; acme's `sso_only`, break-glass admin, and captured `tid = C1` never appear. |
| 4 | **Forged subdomain header:** From a globex-authenticated session, send `GET/PUT /api/v1/tenant/auth-settings` (and a login) with a forged `X-Tenant-Subdomain: acme`. | The resolved tenant/JWT claim must match -- request is rejected or resolves to globex only. It can neither read A's enforcement/onboarding nor apply A's `sso_only` to B. HTTP 401/403 or globex-scoped result -- never acme data. |
| 5 | **Direct settings-id injection:** As `admin-b@globex.com`, attempt to address acme's settings row by id (`{acme_settings_id}`) or embed it in the payload. | HTTP 404 Not Found / rejected -- the EF Core global query filter on `TenantId` makes acme's row invisible from the globex context (RLS deferred; scoping via query filter + `TenantInterceptor`). |
| 6 | **Cross-tenant break-glass write blocked:** As `admin-b@globex.com`, `PUT` attempting to add `admin-a` to a break-glass list or flip enforcement "for acme". | Write is scoped to globex only; acme's `break_glass_admin_user_ids`/`enforcement_mode` are unchanged on re-read (step 3 holds for acme). |
| 7 | **DB-level isolation:** With a direct DB connection scoped to globex, `SELECT` acme's `tenant_auth_settings` row by `tenant_id = {acme}`. | 0 rows when scoped to globex (global query filter blocks the cross-tenant read). |
| 8 | **Cache-key isolation:** Inspect the enforcement/settings cache. | Distinct tenant-scoped keys `t:{acme_tenant_id}:auth-settings` and `t:{globex_tenant_id}:auth-settings` with different `enforcement_mode` values; B's login path never reads A's cached enforcement. |

## 6. Postconditions
- A's `sso_only`/onboarding never affected B's login; no enforcement/onboarding/`tid` data leaked via API, forged identifier, direct id, DB, or cache.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
