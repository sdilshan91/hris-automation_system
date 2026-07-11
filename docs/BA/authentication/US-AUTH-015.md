---
id: US-AUTH-015
module: Authentication & Authorization
priority: Should Have
persona: Tenant User (all roles)
status: draft
created: 2026-06-21
sprint: backlog
acceptance_criteria_count: 6
---

# US-AUTH-015: "Sign in with Microsoft" frontend

## 1. Description
**As a** tenant user,
**I want** a clear "Sign in with Microsoft" button on the login page when my organization has SSO enabled,
**So that** I can authenticate with my corporate identity in one click and understand what is happening at each step.

**As a** tenant whose plan or settings don't include SSO,
**I want** the login page to remain exactly as it is today (email + password),
**So that** SSO never confuses users who can't use it.

## 2. Preconditions
- The backend SSO challenge/callback (US-AUTH-011) and per-tenant config (US-AUTH-012) exist.
- The frontend can determine, for the resolved tenant subdomain, whether SSO is enabled and entitled (a public, pre-auth tenant-SSO-status endpoint or a value injected at tenant resolution).
- The existing Angular login page, tenant resolution (mirroring the backend), and the API response envelope interceptor (US-PLT-001) are in place.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The resolved tenant has SSO enabled and entitled | A user loads the login page | A "Sign in with Microsoft" button is shown alongside (or above) the email/password form, using Microsoft's brand-compliant button styling. |
| AC-2 | The resolved tenant does NOT have SSO enabled/entitled | A user loads the login page | The login page shows only the existing email/password form; no SSO button appears and no extra request blocks page load. |
| AC-3 | The tenant has `enforcement_mode = sso_only` | A user loads the login page | The email/password form is hidden (or collapsed behind a break-glass link, US-AUTH-016) and "Sign in with Microsoft" is the primary action. |
| AC-4 | A user clicks "Sign in with Microsoft" | The click is handled | The browser navigates to the backend challenge endpoint for the resolved tenant (a full-page redirect, not an XHR), beginning the OIDC flow. |
| AC-5 | The SSO callback returns successfully with app tokens | The frontend completes the return | The user is landed in the app authenticated, with tokens stored exactly as for a local login, and routed to their default authorized landing page. |
| AC-6 | The SSO flow fails (isolation reject, no membership, IdP error, token invalid) | The user is returned to the login page with an error code | The page shows a friendly, non-technical message mapped from the error (e.g. "not authorized for this workspace", "ask your administrator for access", "couldn't sign you in — try again"), and the email/password form remains available (unless `sso_only`). |

## 4. Functional Requirements
- FR-1: The login component SHALL conditionally render the "Sign in with Microsoft" button based on the resolved tenant's SSO-enabled/entitled status.
- FR-2: The button SHALL trigger a **full-page redirect** to the backend challenge endpoint (OIDC requires top-level navigation), carrying the resolved tenant context.
- FR-3: The frontend SHALL handle the post-callback return, store the issued app JWT + refresh token using the existing auth-token mechanism, and route to the default authorized page.
- FR-4: The frontend SHALL map backend SSO error codes to friendly, localized (ngx-translate) messages without exposing technical detail.
- FR-5: For `sso_only` tenants, the FE SHALL hide the password form and surface SSO as the primary path, while still allowing the break-glass entry point (US-AUTH-016) where applicable.
- FR-6: The SSO button and flow SHALL be fully keyboard-accessible and screen-reader labelled.
- FR-7: When SSO is not enabled, the FE SHALL make **no** extra blocking network call and render the classic login unchanged.

## 5. Non-Functional Requirements
- NFR-1: Determining SSO availability SHALL not delay first paint of the login form by more than 200 ms (resolve via injected config or a fast, cached pre-auth endpoint).
- NFR-2: The button SHALL meet Microsoft's branding guidelines and WCAG 2.1 AA contrast.
- NFR-3: No tokens or codes SHALL be placed in browser history / URL fragments that persist; the one-time return code SHALL be consumed and cleared.
- NFR-4: The component SHALL be unit-tested with OnPush change detection and signals consistent with the existing auth feature.

## 6. Business Rules
- BR-1: The SSO button visibility is driven by the **resolved tenant's** SSO status, never a global toggle.
- BR-2: A tenant without the SSO plan entitlement never sees the button (consistent with US-AUTH-012 BR-1).
- BR-3: SSO and local login produce identical authenticated sessions from the UI's perspective.
- BR-4: For `sso_only`, the password form is hidden but the break-glass path remains reachable for designated admins (US-AUTH-016).
- BR-5: Error messaging never enumerates tenant existence or which isolation check failed (consistent with US-AUTH-013 FR-7).

## 7. Data Requirements
- **Pre-auth tenant SSO status:** `{ ssoEnabled: bool, enforcementMode: 'optional' | 'sso_only' }` for the resolved subdomain (no secrets, no allow-list contents).
- **Challenge entry:** backend challenge URL + resolved tenant.
- **Return payload:** one-time code/tokens consumed by the existing auth service.
- **Error codes:** `sso_isolation_rejected`, `sso_no_membership`, `sso_idp_error`, `sso_token_validation_failed`, `sso_not_entitled` → mapped to friendly strings.

## 8. UI/UX Notes
- Place the Microsoft button above the email/password fields with an "or" divider, matching the platform's Material + Tailwind styling.
- Use the official Microsoft logo + "Sign in with Microsoft" label per Microsoft's button guidelines.
- `sso_only`: show only the Microsoft button; a subtle "Administrator sign-in" text link reveals the break-glass password form (US-AUTH-016).
- Errors render in the existing login error banner (ngx-toastr / inline), localized, non-technical.
- Mobile: the button is full-width and the redirect flow works within the mobile browser.
- Loading state: show a spinner/disabled state during the redirect to avoid double-clicks.

## 9. Dependencies
- US-AUTH-011 (challenge/callback) — the flow the button drives.
- US-AUTH-012 (config) — supplies SSO-enabled/enforcement status.
- US-AUTH-013/014 — produce the error codes the FE maps.
- US-AUTH-016 (enforcement/break-glass) — governs `sso_only` UI.
- US-PLT-001 (response envelope interceptor) — for any JSON status endpoint.
- Existing Angular auth service, login component, tenant resolution, ngx-translate.

## 10. Assumptions & Constraints
- OIDC requires a top-level redirect; an in-app popup/iframe is out of scope for v1.
- The FE mirrors the backend tenant resolution rules (subdomain → tenant), already established for local dev via `X-Tenant-Subdomain`.
- Cross-subdomain token return uses the one-time-code approach from CR-AUTH-001 (OQ-1), consumed by the auth service on landing.
- Microsoft brand assets are bundled per their licensing terms.

## 11. Test Hints
- **Conditional render:** SSO enabled → button shown; disabled → classic login only, no extra blocking call.
- **sso_only:** Password form hidden, Microsoft primary, break-glass link reveals admin form.
- **Click → redirect:** Assert full-page navigation to the challenge endpoint with tenant context (not an XHR).
- **Successful return:** Mock a successful callback return; assert tokens stored via auth service and routing to default page.
- **Error mapping:** For each error code, assert the correct friendly localized message and that the password form remains (unless sso_only).
- **No enumeration:** Assert error copy never reveals tenant existence or specific isolation failure.
- **A11y:** Keyboard focus, screen-reader label, and AA contrast on the button.
- **History hygiene:** Assert no token/code persists in URL/history after landing.
