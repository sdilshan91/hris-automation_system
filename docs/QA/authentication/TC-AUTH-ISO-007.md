---
id: TC-AUTH-ISO-007
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: blocked
created: 2026-07-24
---

# TC-AUTH-ISO-007: Cross-directory SSO isolation — a valid Entra user from directory A cannot enter tenant B's workspace

## 1. Test Objective
Verify the end-to-end multi-tenant isolation guarantee of the SSO foundation: the tenant is chosen ONLY from the signed `state`, and the resolved tenant's own allow-list (`tid`/domain) gates entry fail-closed. A perfectly valid Entra user belonging to directory A (allow-listed for HRM tenant "acme") must NOT be able to obtain a session in HRM tenant "globex" — not by targeting globex's callback, not by forging a globex state, and not because globex happens to share infrastructure. Isolation holds at the guard, at the query filter, and in cache keys — RLS is deferred, so a cross-tenant id-injection yields 404/rejection, not another tenant's data.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-2, AC-6 (isolation), FR-3, FR-5, FR-9 seam
- Business Rules: BR-1, BR-5

## 3. Preconditions
- Two active HRM tenants: "acme" (acme.yourhrm.com) and "globex" (globex.yourhrm.com), both with SSO configured.
- acme allow-list: `AllowedTenantIds=[C_A]` (Directory A), `AllowedDomains=[acme.com]`.
- globex allow-list: `AllowedTenantIds=[C_B]` (Directory B), `AllowedDomains=[globex.com]`.
- `alice@acme.com` is a valid work/school user in Directory A (`tid=C_A`, `oid=O_A`), NOT a member of globex and NOT in globex's allow-list.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Directory A tid | aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa (C_A) | acme only |
| Directory B tid | bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb (C_B) | globex only |
| Alice id_token | tid=C_A, oid=O_A, email=alice@acme.com, valid signature/aud/iss/exp/nonce | A real, valid token |
| acme state | signed, subdomain=acme | |
| globex state | signed, subdomain=globex | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Legitimate baseline:** Alice completes SSO with an **acme** state and her valid `tid=C_A` token. | SUCCESS — session created in acme (C_A is allow-listed for acme). Redirect targets the acme subdomain. |
| 2 | **Cross-tenant via forged state target:** Replay Alice's valid `tid=C_A` token against a **globex** state (subdomain=globex). | REJECTED fail-closed — globex's allow-list has C_A neither in `AllowedTenantIds` nor `alice@acme.com`'s domain in `AllowedDomains`. Result `access_denied`; NO globex session; NO token. |
| 3 | **Tenant chosen from state, not tid:** Confirm step 2 never lands Alice in acme either (the state said globex). | No session anywhere — a globex state cannot borrow acme's allow-list; the `tid` does not select acme. |
| 4 | **Direct callback targeting:** Send Alice's token + a globex state directly to `/api/v1/auth/sso/callback` (the single fixed redirect handles all tenants). | Same rejection — one fixed callback, but the state's tenant + that tenant's allow-list govern; C_A is not allowed for globex. |
| 5 | **DB/query-filter isolation:** After the acme login (step 1), confirm any user/membership rows created are stamped `TenantId = acme` and are invisible from a globex-scoped context. | Global query filter on `TenantId` hides acme rows from globex; a globex-scoped `SELECT` returns 0 acme rows (RLS deferred; scoping via EF filter + `TenantInterceptor`). |
| 6 | **Cross-tenant id injection:** From a globex-scoped session, attempt to read the acme-created user/session row by id. | HTTP 404 / rejected — the query filter makes acme's row invisible from globex (404-not-403, no cross-tenant leak). |
| 7 | **Cache-key isolation:** Inspect any SSO/session/permission cache populated by the acme login. | Keys are tenant-scoped (`t:{acme_tenant_id}:…`); globex's login path never reads acme's cached entries. |
| 8 | **Positive control (B's own user):** `bob@globex.com` (Directory B, `tid=C_B`) completes SSO with a globex state. | SUCCESS in globex — confirming globex SSO works for ITS allow-listed directory while rejecting A's users. |

## 6. Postconditions
- Directory A's valid user obtained a session only in acme; every attempt to enter globex was rejected; no acme data leaked to globex via API, id-injection, DB, or cache.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
