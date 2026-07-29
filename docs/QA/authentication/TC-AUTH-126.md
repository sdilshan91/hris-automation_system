---
id: TC-AUTH-126
user_story: US-AUTH-016
module: Authentication
priority: high
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-126: `enforcement_mode = sso_only` refuses ordinary local login with the "requires Microsoft" message; the SSO path is accepted

## 1. Test Objective
Verify AC-1 / FR-1: once a tenant admin has designated a break-glass admin and set `enforcement_mode = sso_only`, a new email/password login by an ordinary (non-break-glass) user is refused with the message "Your organization requires sign-in with Microsoft," while the Microsoft SSO path continues to authenticate that same user successfully. Enforcement is evaluated on the login path from the cached tenant setting (NFR-4) without needing a page reload.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-4
- Business Rules: BR-1 (a break-glass admin is a precondition of this state)

## 3. Preconditions
- Tenant "acme" (acme.yourhrm.com) is active with `PlanFeatureFlags.Sso = true` and a valid SSO config from US-AUTH-012 (allow-list `tid`/domain present, SSO enabled).
- A designated break-glass admin (`admin-a@acme.com`) exists in `break_glass_admin_user_ids` (so BR-1/AC-3 permits the switch).
- `enforcement_mode = sso_only` has been saved for acme.
- `user-a@acme.com` is an ordinary user with local credentials AND a valid Entra SSO membership (email in an allowed domain / linked `oid`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Login endpoint | POST /api/v1/auth/login | Standard local login (US-AUTH-001) |
| enforcement_mode | "sso_only" | On acme's `TenantAuthSettings` |
| Ordinary user | user-a@acme.com / correct password | Valid local creds, not break-glass |
| Expected refusal message | "Your organization requires sign-in with Microsoft" | Exact copy from AC-1 |
| SSO login start | GET /api/v1/auth/sso/authorize (or FE "Sign in with Microsoft") | Redirects to Entra with signed `state` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | At acme.yourhrm.com, `POST /api/v1/auth/login` with `user-a@acme.com` and the CORRECT password. | Login refused (HTTP 403 / domain error). Response carries the enforcement reason and the message "Your organization requires sign-in with Microsoft," NOT an invalid-credentials error. No JWT or refresh token issued. |
| 2 | Observe the rendered `sso_only` login page (US-AUTH-015). | Primary "Sign in with Microsoft" button is shown; the email/password form is suppressed/blocked for ordinary users; only a discreet "Administrator sign-in" break-glass link remains. |
| 3 | Start the SSO path for `user-a@acme.com` (click "Sign in with Microsoft" -> Entra -> return to the fixed redirect with a valid token whose `tid` is in acme's allow-list). | SSO authentication succeeds; a normal app JWT + refresh token are issued for user-a in acme (SSO terminates in the existing `JwtService`). The user reaches the dashboard. |
| 4 | Re-attempt the local login of step 1. | Still refused with the same message -- enforcement is stable and evaluated from the cached tenant setting, adding negligible overhead (NFR-4). |

## 6. Postconditions
- Ordinary local logins for acme are blocked; SSO logins succeed.
- No tokens were issued to the refused local-login attempt.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
