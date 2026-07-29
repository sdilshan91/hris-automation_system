---
id: TC-AUTH-143
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: blocked
created: 2026-07-24
---

# TC-AUTH-143: An HRM tenant with NO SSO allow-list entry can never complete SSO (fail-closed default)

## 1. Test Objective
Verify the fail-closed default of the isolation guard (US-AUTH-013 seam / BR-5): if the resolved HRM tenant has no allow-list entry at all, or an entry whose allow rules are all empty (`HasAnyAllowRule == false`), then NO Entra user — however valid their token — can complete SSO for that tenant. This guarantees the foundation cannot silently allow cross-tenant entry before isolation is configured.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-6 (fail-closed), FR-9 seam
- Business Rules: BR-5 (disabled until isolation enforced)

## 3. Preconditions
- Entra SSO configured at the deployment level. A valid signed `state` for subdomain=acme exists.
- Microsoft returns a fully-valid `id_token` (signature/aud/iss/exp/nonce all good) with `tid=C1`, `email=user-a@acme.com`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme allow-list (case A) | (absent — no key for "acme") | `TenantAllowList` has no entry |
| acme allow-list (case B) | present but `AllowedTenantIds=[]` AND `AllowedDomains=[]` | `HasAnyAllowRule == false` |
| Callback | GET /api/v1/auth/sso/callback?code={valid}&state={acme-state} | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Case A: acme has NO allow-list entry. Complete the callback with the fully-valid token. | Rejected fail-closed: guard returns not-allowed, result `access_denied`; HTTP 302 to `/auth/login?sso_error=access_denied`; NO token issued. |
| 2 | Case B: acme has an entry but both lists empty. Repeat. | Same fail-closed rejection (`HasAnyAllowRule == false` ⇒ no entry can authenticate). NO token. |
| 3 | Inspect the audit/log. | A warning records that acme has no SSO allow-list configured (fail-closed), naming the incoming `tid`; the raw token is not logged (NFR-5). |
| 4 | Add a valid `AllowedTenantIds=[C1]` for acme and repeat (positive control). | Now the token is accepted — confirming the earlier rejections were the absent/empty allow-list, not another failure. |

## 6. Postconditions
- Absent/empty allow-list ⇒ SSO is impossible for that tenant; only an explicit allow rule enables it.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
