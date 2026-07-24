---
id: TC-AUTH-148
user_story: US-AUTH-011
module: Authentication
priority: high
type: functional
status: draft
created: 2026-07-24
---

# TC-AUTH-148: Microsoft-returned OIDC error (e.g. `access_denied`, user cancelled) — no token, lockout counter untouched, friendly redirect

## 1. Test Objective
Verify AC-7 / BR-3: when Microsoft redirects back to the callback with an `error` (user cancelled consent, `access_denied`, admin-consent-required, etc.) instead of a `code`, the system issues NO token, does NOT count the event toward the local account-lockout counter (US-AUTH-010 BR-6), and returns the user to the login page with a friendly message. An IdP error/cancellation is not a credential failure.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-7
- Business Rules: BR-3 (IdP errors/cancellations do not count toward lockout)
- Related: US-AUTH-010 BR-6

## 3. Preconditions
- Entra SSO configured. `user-a@acme.com` exists with a local account and `failed_login_count = K` (some baseline, e.g. 0).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Callback (error) | GET /api/v1/auth/sso/callback?error=access_denied&error_description=... | No `code`, no `state` needed |
| Callback (cancel) | GET /api/v1/auth/sso/callback?error=access_denied&error_description=user+cancelled | User declined at Microsoft |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Callback with `error=access_denied`. | Result `access_denied`; HTTP 302 to `/auth/login?sso_error=access_denied`; the SPA renders the friendly "We couldn't sign you in with Microsoft…" message. NO app JWT/refresh issued. |
| 2 | Read `user-a`'s `failed_login_count`. | UNCHANGED (still K) — the IdP error did NOT increment lockout (BR-3 / US-AUTH-010 BR-6). No `locked_until` is set. |
| 3 | Immediately perform a normal local login for `user-a` with correct credentials. | Succeeds — the prior SSO IdP error had no effect on the local lockout state. |
| 4 | Inspect the audit/log. | An IdP-error record (expected `sso_idp_error`) is written with the resolved context and the Entra error code, not treated as a credential failure. |

## 6. Postconditions
- IdP errors/cancellations are cleanly handled: no session, no lockout impact, friendly UX.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
