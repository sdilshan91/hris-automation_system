---
id: TC-AUTH-142
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-142: Fail-closed tenant isolation — an Entra `tid` NOT in the resolved tenant's allow-list is REJECTED (cross-tenant entry blocked)

## 1. Test Objective
Verify the SSO security crux (the US-AUTH-013 guard seam introduced in FR-9, now hardened): after a fully-valid `id_token` is validated, the resolved HRM tenant's SSO allow-list is checked, and a login whose Entra `tid` (and email domain) is NOT allow-listed for that tenant is REJECTED fail-closed — no application JWT is issued. This is what stops the `organizations` authority (which authenticates ANY work/school user from ANY directory) from letting a user from directory X sign in to an HRM tenant that only trusts directory C1.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-6 (fail-closed rejection), and the FR-9 isolation seam
- Functional Requirements: FR-5, FR-9
- Business Rules: BR-1, BR-5 (foundation stays disabled in prod until isolation is enforced)

## 3. Preconditions
- Entra SSO configured. Tenant "acme" allow-list: `AllowedTenantIds=[C1]`, `AllowedDomains=[acme.com]`.
- A valid signed `state` for subdomain=acme exists.
- Microsoft returns a validly-signed, otherwise-valid `id_token` (good signature/aud/iss/exp/nonce) but with `tid=EVIL` and `email=mallory@evil.com` — a real work/school user in a directory NOT allow-listed for acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| id_token tid | eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee (EVIL) | NOT in acme AllowedTenantIds |
| id_token email | mallory@evil.com | Domain NOT in acme AllowedDomains |
| Token signature/aud/iss/exp/nonce | all VALID | Isolate the `tid`/domain check as the sole failing condition |
| Callback | GET /api/v1/auth/sso/callback?code={valid}&state={acme-state} | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Complete the callback for acme with the fully-valid-but-foreign token (`tid=EVIL`, `mallory@evil.com`). | Token validation PASSES (signature/aud/iss/exp/nonce all good) but the isolation guard REJECTS: neither `tid` nor email domain is allow-listed for acme. Result `access_denied`. |
| 2 | Inspect the response. | HTTP 302 to `/auth/login?sso_error=access_denied`; NO app JWT, NO refresh token, no `Set-Cookie: refreshToken`. Mallory does NOT enter acme. |
| 3 | Inspect the audit/log. | An isolation-rejected record is written for tenant acme with the incoming `tid`/domain (a warning naming the un-allow-listed `tid`); the raw `id_token` is NOT logged (NFR-5). |
| 4 | Add `EVIL` to acme's `AllowedTenantIds` and repeat (positive control). | Now the same token is accepted and a session is created — confirming step 1's rejection was specifically the allow-list, and the guard is the enforcement point. |
| 5 | (Domain path) Remove `EVIL` again; instead allow-list `evil.com` under acme `AllowedDomains` and repeat. | Accepted via the email-domain rule — confirms the guard permits EITHER a `tid` match OR an allow-listed verified email domain. |

## 6. Postconditions
- A valid Microsoft user from a non-allow-listed directory cannot obtain an acme session; only allow-listed `tid`/domain users can.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
