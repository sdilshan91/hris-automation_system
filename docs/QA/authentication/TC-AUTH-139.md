---
id: TC-AUTH-139
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-139: The HRM tenant is resolved from the signed `state`, NEVER from the token `tid`

## 1. Test Objective
Verify AC-2 / FR-3 and the SSO security crux: the originating HRM tenant is taken exclusively from the tamper-evident signed `state` (which carries the subdomain), and NEVER inferred from the `id_token`'s `tid`. A token whose `tid` maps to a directory associated with a DIFFERENT HRM tenant must not cause the session to land in that other tenant; the session is only ever created for the tenant named in the `state` (and only if that state-tenant's own allow-list permits the `tid`).

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3, FR-5
- Business Rules: BR-1

## 3. Preconditions
- Two active HRM tenants: "acme" (acme.yourhrm.com) and "globex" (globex.yourhrm.com).
- acme allow-list: `AllowedTenantIds=[C1]`. globex allow-list: `AllowedTenantIds=[C2]`.
- A valid signed `state` for subdomain=acme (nonce=N, verifier=V) exists from an acme challenge.
- Microsoft returns a validly-signed `id_token` with `tid=C1` (the directory allow-listed for acme).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| state | signed, subdomain=acme | The ONLY trusted source of the HRM tenant |
| id_token tid | C1 | Allow-listed for acme, NOT for globex |
| Callback | GET /api/v1/auth/sso/callback?code={valid}&state={acme-state} | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Complete the callback with the acme `state` and a token whose `tid=C1`. | Session is created for tenant **acme** (from the state), and the acme allow-list check for `tid=C1` passes. The final redirect targets the acme subdomain. |
| 2 | Inspect how the tenant was chosen (log/trace). | The subdomain came from the unprotected `state`; the `tid` claim was used ONLY to check against acme's allow-list — it was never used to select the tenant. |
| 3 | Repeat with the SAME token (`tid=C1`) but a signed `state` for subdomain=**globex**. | globex's allow-list does NOT contain C1 → the isolation guard REJECTS (fail-closed): result `access_denied`, no token, redirect to login with `sso_error`. The globex session is never created despite a valid Microsoft token. |
| 4 | Confirm no cross-tenant landing. | At no point does a token's `tid` alone place the user into the directory-owning tenant; only the state-named tenant is ever considered. |

## 6. Postconditions
- Tenant selection is driven solely by the signed state; a valid Microsoft token cannot redirect a user into a tenant it was not issued a state for.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
