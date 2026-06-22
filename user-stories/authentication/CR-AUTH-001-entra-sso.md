---
id: CR-AUTH-001
type: Change Request / Epic
module: Authentication & Authorization
priority: Should Have
status: draft
created: 2026-06-21
sprint: backlog
feature_flag: PlanFeatureFlags.Sso
stories: US-AUTH-011, US-AUTH-012, US-AUTH-013, US-AUTH-014, US-AUTH-015, US-AUTH-016
---

# CR-AUTH-001: Enterprise SSO via Microsoft Entra ID (multi-tenant O365 login)

## 1. Problem Statement
Customers of the HRM SaaS platform increasingly mandate that employees authenticate
with their existing corporate identity (Microsoft 365 / Microsoft Entra ID) rather than
a separate HRM-managed email + password. The reasons are standard enterprise procurement
requirements:

- **Security & compliance:** centralized credential lifecycle, MFA, and conditional-access
  policies enforced by the customer's own IdP; no second password to phish or leak.
- **Lifecycle:** when an employee leaves and the customer disables their Entra account, their
  HRM access should die with it (eventually via SCIM — see out-of-scope) rather than lingering.
- **Frictionless adoption:** "Sign in with Microsoft" removes onboarding friction for the
  thousands of organizations already standardized on Microsoft 365.

The subscription model already *sells* this capability — `PlanFeatureFlags.Sso` ("Single
sign-on (SAML/OIDC)") is a plan entitlement — but there is **no implementation behind it**.
This CR closes that gap for the **OIDC / Microsoft Entra ID** case.

## 2. As-Is Gap (verified in the codebase)
- **SSO is not implemented.** Authentication today is local email + password (BCrypt) which
  mints the application's own JWT in `HRM.Infrastructure/Identity/JwtService.cs`, with optional
  TOTP MFA (US-AUTH-005). The only registered authentication scheme is `AddJwtBearer`
  (`HRM.Api/Program.cs`).
- **No federated-identity code or packages exist** — no Azure AD / Entra / OIDC / MSAL / SAML
  libraries, handlers, controllers, or config anywhere in `src/`.
- **The only existing hook** is the sellable entitlement `PlanFeatureFlags.Sso`
  (`HRM.Domain/Entities/SubscriptionPlan.cs:105`) — a boolean placeholder, no behavior.
- **Per-tenant auth configuration already exists:** `TenantAuthSettings`
  (`Features/Auth`, surfaced via `TenantAuthSettingsResponse` / `TenantAuthSettingsController`)
  already holds MFA policy, idle/absolute session timeouts, concurrent-session strategy, and
  lockout policy. **This is the correct home for per-tenant SSO configuration** — we extend it
  rather than introduce a parallel settings store.
- **Multi-tenancy** is subdomain-based (`acme.yourhrm.com`); the tenant is resolved by
  `TenantResolutionMiddleware` before auth runs. Tenant isolation is the platform's #1
  non-negotiable rule and is the dominant risk this CR must address (see §5).

## 3. Proposed Multi-Tenant Technical Model
The design follows the standard SaaS pattern for "log in with the customer's Microsoft 365":

1. **ONE multi-tenant Entra app registration**, owned by the vendor, configured for
   *"Accounts in any organizational directory"* (`signInAudience = AzureADMultipleOrgs`).
   We do **not** register an app per customer. Each customer's Microsoft 365 administrator
   grants **admin consent** to this single app once (see §4).
2. **OIDC Authorization Code flow** against authority
   `https://login.microsoftonline.com/organizations`:
   - The HRM app redirects the browser to Microsoft (the `/challenge`).
   - Microsoft authenticates the user (incl. the customer's own MFA / conditional access).
   - Microsoft redirects back to the HRM callback (`/signin-oidc`) with an authorization code.
   - The HRM backend exchanges the code, **validates the resulting `id_token`**, and then
     **issues its own application JWT** via the existing `JwtService`. The rest of the
     application (controllers, refresh-token flow, RBAC, sessions, lockout) is unchanged —
     SSO is purely a new *front door* that terminates in the same JWT.
3. **Fixed redirect host + `state`-carried tenant.** Azure redirect URIs must be
   pre-registered and per-tenant wildcard subdomains are **not reliable**. Therefore the
   callback uses **one fixed redirect host** (e.g. `https://app.yourhrm.com/signin-oidc`),
   and the resolved HRM tenant (subdomain) is carried in the OIDC `state` parameter (signed /
   tamper-evident) so the callback knows which tenant the login is for and where to return the
   user. The browser is sent back to the originating tenant subdomain only after the app JWT is
   issued.
4. **Tenant-scoped issuer/`tid` validation (the security crux — see §5).**
5. **User matching / JIT.** Match the Entra user (by verified email and/or `oid`) to an
   existing `user_tenant` membership; link the `oid` on first SSO login. Optionally
   just-in-time provision a new membership for allow-listed domains with a configured default
   role.

```
Browser (acme.yourhrm.com)                HRM API (app.yourhrm.com)            Microsoft Entra
        │  "Sign in with Microsoft"             │                                    │
        ├──────────────────────────────────────►│  /auth/sso/challenge?tenant=acme   │
        │                                        │  build state{tenant=acme}, redirect│
        │◄───────────────────────────────────────┤  302 → login.microsoftonline.com  │
        ├────────────────────────────────────────────────────────────────────────────►│
        │                                        │                       (user auths) │
        │◄────────────────────────────────────────────────────────────────────────────┤
        ├──────────────────────────────────────►│  /signin-oidc?code=…&state=…       │
        │                                        │  exchange code, validate id_token, │
        │                                        │  check tid/domain allow-list,      │
        │                                        │  match user → user_tenant,         │
        │                                        │  issue app JWT (+ refresh)         │
        │◄───────────────────────────────────────┤  302 → acme.yourhrm.com (+ tokens) │
```

## 4. Azure-Side Setup Checklist (vendor, one-time)
Performed once by the platform/vendor team in the **vendor's** Entra tenant:

1. **App registration** — Entra admin center → *App registrations* → *New registration*.
   - Name: e.g. `HRM SaaS Platform SSO`.
   - **Supported account types:** *Accounts in any organizational directory (Any Microsoft Entra
     ID tenant — Multitenant)* → `signInAudience = AzureADMultipleOrgs`.
   - **Redirect URI (Web):** `https://app.yourhrm.com/signin-oidc` (the single fixed callback;
     add a `https://localhost:xxxx/signin-oidc` entry for local dev). No per-tenant URIs.
2. **Front-channel logout URL (optional):** `https://app.yourhrm.com/signout-callback-oidc`.
3. **Credentials** — *Certificates & secrets*:
   - Create a **client secret** (or, preferred for production, a **certificate**) for the
     code-for-token exchange. Store as a secret (.NET user-secrets / Key Vault), **never** in
     `appsettings.json` (Critical Rule #6). Record expiry and add a rotation reminder.
4. **API permissions** — *API permissions* → Microsoft Graph → *Delegated*:
   - `openid`, `profile`, `email` (core OIDC claims), and `User.Read` (read the signed-in
     user's basic profile). These are low-privilege delegated scopes.
   - Mark them so they appear on the consent screen; no application (app-only) permissions are
     required for v1.
5. **Token configuration (recommended):** add the **email** and (if needed) **upn** optional
   claims to the ID token so user matching has a reliable verified email.
6. **Publisher verification (recommended):** verify the publisher domain to reduce consent
   friction / warnings for customer admins.
7. **Record the constants** the app needs: `ClientId`, `Authority`
   (`https://login.microsoftonline.com/organizations`), redirect URI, and the secret/cert
   reference. These are *platform-level* (not per-tenant) and live in app configuration / secrets.

### Customer-side (per tenant, see US-AUTH-016)
Each customer's Microsoft 365 admin grants **admin consent** to the vendor app via the
admin-consent URL the HRM platform generates:
`https://login.microsoftonline.com/{customer_tenant}/adminconsent?client_id={ClientId}&redirect_uri=…`.
The customer admin then enters their Entra **Directory (tenant) ID** (`tid`) and/or verified
email domains into the HRM Tenant Admin SSO settings (US-AUTH-012) so the platform can
allow-list them (US-AUTH-013).

## 5. Security Model (tenant isolation is non-negotiable)
The dominant threat is **cross-tenant entry**: because this is a *multi-tenant* Entra app, by
default *any* Microsoft user from *any* organization can complete the Microsoft login. Without a
guard, a Microsoft user from org B could authenticate and land in tenant A's HRM workspace. The
guard is mandatory and is the subject of **US-AUTH-013**:

- **`tid` allow-listing:** the validated `id_token`'s `tid` (the customer's Entra Directory ID)
  MUST match one of the Entra tenant IDs configured (and admin-consented) for the **resolved HRM
  tenant**. A custom **issuer validator** is required because the `organizations` authority issues
  tokens for many issuers — the default single-issuer validation is insufficient.
- **Verified-email-domain allow-listing** as an additional/alternative check: the user's verified
  email domain MUST be in the HRM tenant's allow-list. Domains must come from a **verified** email
  claim, never an unverified one.
- **Fail-closed:** if neither `tid` nor domain is allow-listed for the resolved tenant, the login
  is **rejected** (no app JWT issued, no JIT provisioning), the attempt is audited, and a generic
  error is shown. Misconfiguration (empty allow-list) also fails closed.
- **`state` integrity:** the OIDC `state` carrying the HRM tenant MUST be tamper-evident
  (signed/HMAC) and single-use (CSRF + replay protection); the resolved tenant from `state` is
  cross-checked against the originating subdomain.
- **Nonce / token validation:** standard OIDC `nonce`, audience (`aud == ClientId`), `iss`,
  expiry, and signature (Microsoft JWKS) validation all apply.
- **Break-glass (US-AUTH-016):** at least one local-credential administrator path MUST remain so a
  tenant cannot be locked out if Entra, the vendor app, or the allow-list is misconfigured. SSO
  enforcement must never disable the last break-glass admin login.

## 6. Out-of-Scope (explicitly NOT in v1)
These are acknowledged future work, deliberately excluded to keep v1 shippable:

- **SAML 2.0** federation (only OIDC / Entra in v1; the plan flag text mentions SAML as a future
  protocol).
- **SCIM 2.0 auto-provisioning / auto-deprovisioning** (the `PlanFeatureFlags.Scim` flag is a
  separate entitlement; lifecycle de-provisioning when a user leaves Entra is future work).
- **Group → role mapping** (mapping Entra security groups/app roles to HRM roles; v1 uses a single
  configurable default role for JIT).
- **Non-Microsoft OIDC IdPs** (Google Workspace, Okta, generic OIDC) — the architecture is
  protocol-generic but v1 targets Entra only.
- **Identity-provider-initiated (IdP-initiated) login** from the Microsoft app launcher.

## 7. Phased Implementation Plan
| Phase | Scope | Stories | Outcome |
|-------|-------|---------|---------|
| **Phase 1 — OIDC foundation + POC** | Add OIDC challenge/callback endpoints, code exchange, `id_token` validation against `organizations` authority, issue app JWT. Single hardcoded test tenant, no per-tenant config yet. | US-AUTH-011 | A developer can complete a Microsoft login against one test org and receive a valid app JWT. |
| **Phase 2 — Per-tenant config + `tid` isolation** | Extend `TenantAuthSettings` with SSO config (enabled, `tid`(s), allowed domains, default role, enforcement mode); add Tenant Admin UI; **enforce tenant-scoped `tid`/domain allow-listing** with a custom issuer validator (fail-closed). | US-AUTH-012, US-AUTH-013 | SSO is configurable per tenant and isolation is enforced — a user from the wrong org cannot enter. |
| **Phase 3 — User matching, linking & JIT** | Match Entra `email`/`oid` to an existing `user_tenant` membership; link `oid` on first login; optional JIT provisioning for allow-listed domains with the default role. | US-AUTH-014 | Existing members log in via Microsoft; allow-listed new users are provisioned just-in-time. |
| **Phase 4 — Enforcement, break-glass, onboarding & FE polish** | SSO-only enforcement mode with break-glass admin path; customer admin-consent onboarding flow; login-page polish. | US-AUTH-016 (+ US-AUTH-015 polish) | End-to-end customer-ready experience: onboarding via admin consent, polished login, optional SSO enforcement with a safe escape hatch. |

### Addendum — approved entry point & revised increment order (2026-06-21)
Per stakeholder decision, the **"Continue with Microsoft" button is added to the default login page now** (the visible entry point users take to sign in with O365), rather than being held to Phase 4. Revised increments:
- **Increment 1 (delivered, verifiable without Azure):** the login-page button + the `GET /api/v1/auth/sso/challenge` entry endpoint, **config-driven & fail-closed** — when no Entra app registration is configured, the flow redirects back to the login page with a clear "Microsoft sign-in isn't set up for this workspace yet" message. No insecure/half-wired flow is shipped. (Starts US-AUTH-011 scaffold + US-AUTH-015 button.)
- **Increment 2 (requires the Azure app registration — §4):** the secure OIDC round-trip — `/auth/sso/callback`, `id_token` validation, the **US-AUTH-013 `tid`/domain isolation guard (fail-closed)**, and US-AUTH-014 user matching that issues the app JWT. This cannot be built/tested meaningfully until the vendor multi-tenant Entra app exists (ClientId/secret/redirect URI) and a customer `tid` is allow-listed.
- **Dev tenant routing note:** the challenge is a full-page browser redirect, so it cannot carry the dev `X-Tenant-Subdomain` header — the tenant is passed as a query param (`?tenant={subdomain}`) in dev and resolved from the host subdomain in prod; the signed OIDC `state` carries tenant + returnUrl across the round-trip.
- **Activation blocker:** real Microsoft login is **gated on the customer/vendor completing the Azure setup in §4**. Until then the button is visible but reports "not configured."

## 8. Dependencies
- **US-AUTH-001** (local login) — SSO terminates in the same app JWT; local login remains the
  break-glass path.
- **US-AUTH-002** (JWT issuance + refresh) — reused verbatim to mint tokens after SSO.
- **US-AUTH-006** (RBAC) — JIT-provisioned users and matched members get tenant roles via the
  existing role model; default-role assignment depends on it.
- **US-AUTH-007** (tenant resolution from subdomain) — the resolved tenant drives `state`,
  allow-listing, and the return redirect.
- **`PlanFeatureFlags.Sso`** (US-ADM-009 subscription plans) — the whole feature is gated on this
  entitlement.
- **`TenantAuthSettings`** infrastructure (US-AUTH-005/009/010) — extended to host SSO config.
- **Tenant audit logging** (US-NTF-004/005) — all SSO config changes and login decisions are
  audited.
- **External:** a vendor-owned multi-tenant Entra app registration (§4); the
  `Microsoft.AspNetCore.Authentication.OpenIdConnect` / `Microsoft.Identity.Web` package(s).

## 9. Rough Effort & Sequence
- **Phase 1:** ~M (backend OIDC plumbing + package wiring; highest unfamiliarity risk).
- **Phase 2:** ~M–L (settings extension + migration + Tenant Admin UI + the security-critical
  custom issuer validator; **do not parallelize 013 ahead of 012** — 013 reads 012's config).
- **Phase 3:** ~M (matching/linking is mostly data; JIT touches RBAC).
- **Phase 4:** ~M (FE button + UX is small; enforcement + break-glass + admin-consent onboarding
  carry the lockout risk and need careful testing).
- **Recommended order:** 011 → 012 → 013 → 014 → 015 → 016 (strictly sequential where security
  depends on config; 015 FE can begin once 011's contract is stable).

## 10. Traceability — Stories in this CR
| Story | Title | Phase | Priority |
|-------|-------|-------|----------|
| [US-AUTH-011](US-AUTH-011.md) | Entra OIDC authentication foundation | 1 | Should Have |
| [US-AUTH-012](US-AUTH-012.md) | Per-tenant SSO configuration | 2 | Should Have |
| [US-AUTH-013](US-AUTH-013.md) | Tenant-scoped `tid` / domain validation & isolation | 2 | Must Have |
| [US-AUTH-014](US-AUTH-014.md) | User matching, account linking & JIT provisioning | 3 | Should Have |
| [US-AUTH-015](US-AUTH-015.md) | "Sign in with Microsoft" frontend | 4 | Should Have |
| [US-AUTH-016](US-AUTH-016.md) | SSO enforcement, break-glass & admin-consent onboarding | 4 | Should Have |

## 11. Open Questions (for human review)
- **OQ-1:** Confirm the fixed redirect host (`app.yourhrm.com`) is provisioned and TLS-terminated,
  and that it can issue tokens then redirect back to arbitrary tenant subdomains without a
  cross-site cookie problem (recommend returning tokens via a one-time code, not a cookie, across
  subdomains).
- **OQ-2:** Client **secret** vs **certificate** for the token exchange — recommend certificate +
  Key Vault for production; secret acceptable for the POC.
- **OQ-3:** For JIT, is a single tenant-wide default role acceptable for v1 (recommended), deferring
  group→role mapping to a future CR?
- **OQ-4:** Should `tid` allow-listing support **multiple** Entra tenant IDs per HRM tenant (e.g.
  customer with several directories)? Recommend yes — store a list.
