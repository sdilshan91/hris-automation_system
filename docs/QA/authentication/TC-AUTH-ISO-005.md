---
id: TC-AUTH-ISO-005
user_story: US-AUTH-012
module: Authentication
priority: critical
type: security
status: pass
created: 2026-07-24
---

# TC-AUTH-ISO-005: SSO settings tenant isolation -- tenant A's config is invisible and unwritable from tenant B, even with a forged tenant identifier

## 1. Test Objective
Verify AC-6 / NFR-2 / BR-2: SSO configuration stored on `TenantAuthSettings` is strictly per tenant. A tenant B admin cannot read or write tenant A's SSO settings; tenant A's `tid`s/domains/`jit_default_role`/`enforcement_mode` never appear in tenant B's responses; and cross-tenant access is impossible even with a forged tenant identifier (header/claim/id injection) because tenant scoping is enforced at the query level (global query filter on `TenantId`) and at direct-DB level. There is no global/shared SSO config crossing tenants.

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-6
- Non-Functional Requirements: NFR-2
- Business Rules: BR-2
- Functional Requirements: FR-2

## 3. Preconditions
- Two tenants exist and are active: "acme" (acme.yourhrm.com, tenant A) and "globex" (globex.yourhrm.com, tenant B), both with `Sso = true`.
- Tenant A SSO settings saved: `tid = A1 (aaaaaaaa-...-aaaa)`, domain `acme.com`, `jit_default_role = Employee`, `enforcement_mode = optional`.
- Tenant B SSO settings saved: `tid = B1 (bbbbbbbb-...-bbbb)`, domain `globex.com`.
- `admin-a@acme.com` is admin in A; `admin-b@globex.com` is admin in B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A tid | aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa | acme allow-list |
| Tenant B tid | bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb | globex allow-list |
| A settings row id | {acme_settings_id} | For direct-id injection |
| Forged header | X-Tenant-Id / X-Tenant-Subdomain = acme | Sent from a globex session |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Read isolation:** As `admin-b@globex.com` at globex.yourhrm.com, `GET /api/v1/tenant/auth-settings`. | HTTP 200. Response shows only globex's SSO config (`tid = B1`, `globex.com`). Tenant A's `A1`/`acme.com`/`Employee` never appear. |
| 2 | **Reverse read isolation:** As `admin-a@acme.com`, `GET /api/v1/tenant/auth-settings`. | HTTP 200. Only acme's config; globex's `B1`/`globex.com` absent. Isolation is bidirectional. |
| 3 | **Cross-tenant write blocked:** As `admin-b@globex.com`, `PUT /api/v1/tenant/auth-settings` attempting to inject `tid = A1`/`acme.com` targeting tenant A's config. | The write is scoped to globex only -- it can never mutate acme's row. acme's settings are unchanged on re-read (step 2 values hold). |
| 4 | **Forged subdomain header:** From a globex-authenticated session, send `GET/PUT /api/v1/tenant/auth-settings` with a forged `X-Tenant-Subdomain: acme` (or `X-Tenant-Id` = acme's id). | Request is rejected or resolves to globex only (the middleware/JWT tenant claim must match the resolved tenant); acme's config is neither read nor written. HTTP 401/403 or globex-scoped result -- never acme data. |
| 5 | **Direct settings-id injection:** As `admin-b@globex.com`, attempt to address tenant A's settings row by id (e.g. `GET /api/v1/tenant/auth-settings/{acme_settings_id}` if such a route exists, or by embedding the id in the payload). | HTTP 404 Not Found / rejected -- the EF Core global query filter on `TenantId` makes acme's row invisible from the globex context. |
| 6 | **DB-level isolation:** With a direct DB connection in globex's tenant context, `SELECT` acme's `tenant_auth_settings` row (by `tenant_id = {acme}`). | 0 rows returned when the session is scoped to globex (global query filter / RLS blocks the cross-tenant read). |
| 7 | **Cache-key isolation:** Inspect the SSO-settings cache. | Distinct tenant-scoped keys `t:{acme_tenant_id}:sso-settings` and `t:{globex_tenant_id}:sso-settings` exist with different values; neither tenant's login path can read the other's cached config. |

## 6. Postconditions
- No SSO config leaks between tenants via API, forged identifiers, direct id, DB, or cache.
- Isolation is enforced at the query/DB layer, not just by the UI.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
