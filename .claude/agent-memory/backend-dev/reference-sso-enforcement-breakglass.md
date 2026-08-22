---
name: reference-sso-enforcement-breakglass
description: US-AUTH-016 SSO enforcement + break-glass + admin-consent — where the login gate, designation guard, and consent flow live, and the one MFA gap
metadata:
  type: reference
---

# SSO enforcement, break-glass & admin-consent (US-AUTH-016)

Built ON [[reference-sso-settings]] (US-AUTH-012). Still no `TenantAuthSettings` entity — everything is
columns on `Tenant`.

- **New Tenant columns:** `break_glass_admin_user_ids` (jsonb List<string> of user-id GUIDs) +
  `sso_onboarding_status` (`not_started`|`consent_pending`|`consented`|`enabled`, default not_started). Constants
  in `HRM.Domain/Authorization/SsoOnboardingStatuses.cs`. Migration `AddTenantSsoEnforcementOnboarding`.
- **Login gate:** `AuthService.LoginAsync` is now a thin wrapper over private `LoginInternalAsync(..., breakGlass)`;
  `BreakGlassLoginAsync` = same core with `breakGlass:true`. The decision is `EvaluateSsoEnforcementAsync` (step 5a,
  after membership): reads the CACHED `SsoSettingsSnapshot` (NFR-4, no Entra dependency → NFR-1). Standard path
  under `sso_only` → 403 "requires sign-in with Microsoft" for EVERYONE (AC-1; a break-glass admin must use the
  break-glass path). Break-glass path → permitted ONLY if `user.Id` in `BreakGlassAdminUserIds` (else 403 +
  `break_glass_login_denied` audit). Snapshot now carries `BreakGlassAdminUserIds`.
- **Break-glass success:** emits `break_glass_login` (high-severity audit, secrets excluded) + Hangfire
  `IBreakGlassNotificationService.SendBreakGlassAlertAsync` (mirrors `ILockoutNotificationService`; log-only email
  when SMTP unset). Fires in `LoginInternalAsync` ONLY on token issuance.
- **KNOWN GAP (flagged):** a break-glass admin WITH MFA hits the two-step flow — step 2 is `VerifyMfaLoginAsync`,
  which does NOT know it's break-glass, so it skips the `break_glass_login` audit + alert (login still succeeds).
  Single-shot (no MFA, or inline `mfaCode`) is fully covered. Wiring the two-step needs touching the MFA-verify
  path (out of the "don't rebuild MFA" lane).
- **sso_only WRITE guard (replaces 012's):** `UpdateTenantAuthSettingsAsync` now requires ≥1 VALID designated
  break-glass admin to enable `sso_only` (AC-3/BR-1) — `GetValidBreakGlassAdminIdsAsync` = active member + password
  + Tenant Owner/Admin role. The old `HasLocalBreakGlassAdminAsync` ("any local admin exists") was REMOVED. The
  012 Postgres test's sso_only-success arm was updated to pass `BreakGlassAdminUserIds` (adapted, not weakened).
  Enabling SSO sets onboarding→enabled; a mode change emits `sso_enforcement_changed` (before/after) on top of
  `sso_config_updated`.
- **Admin-consent (AC-4/5/6):** `IEntraSsoService.BuildAdminConsentUrlAsync` (`{authority}/adminconsent`, vendor
  ClientId + `AdminConsentRedirectUri` option, signed `AdminConsentState{subdomain,origin,nonce}` — HRM tenant is
  resolved from the STATE subdomain, never the consent `tid`). `CompleteAdminConsentAsync` → on grant
  `AuthService.CaptureAdminConsentAsync` (adds tid to `AllowedEntraTenantIds`, status→consented, does NOT enable
  SSO per BR-3, audit `sso_admin_consent_completed`); on decline `RecordAdminConsentFailureAsync` (audit
  `sso_admin_consent_failed`, prior mode intact). Start endpoint is AUTHENTICATED on a SEPARATE
  `SsoOnboardingController` (`POST api/v1/tenant/sso/onboarding/admin-consent`) because SsoController's
  controller-level `[AllowAnonymous]` overrides action-level `[Authorize]`. Callback is anonymous on SsoController
  (`GET api/v1/auth/sso/admin-consent/callback`).
- **Break-glass login endpoint:** `POST api/v1/auth/break-glass-login` (AuthController, anonymous, same rate-limit
  + cookie handling as /login).
- **FE contract:** `TenantContextController` (public, pre-login) now exposes `enforcementMode` + `ssoEnabled` (read
  from the cached snapshot) so the login page can render the `sso_only` screen (AC-1).
