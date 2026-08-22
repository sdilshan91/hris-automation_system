---
name: reference-sso-settings
description: US-AUTH-012 per-tenant SSO config — where it lives, the entitlement/break-glass seams, and the FR-8 gap US-AUTH-013 must close
metadata:
  type: reference
---

# Per-tenant Entra SSO settings (US-AUTH-012)

- **There is no `TenantAuthSettings` entity.** The `TenantAuthSettings{Request,Response}` DTOs and the
  `TenantAuthSettingsController` all read/write columns **directly on the `Tenant` entity** (MFA, session,
  lockout, and now the SSO fields: `SsoEnabled`, `AllowedEntraTenantIds`/`AllowedEmailDomains` (jsonb lists),
  `JitEnabled`, `JitDefaultRole`, `SsoEnforcementMode`). The read/write logic lives in
  `AuthService.{Get,Update}TenantAuthSettingsAsync` (delegated to via MediatR handlers).
- **Entitlement gate:** resolved in `AuthService.IsSsoEntitledAsync` by joining `Tenant.PlanId == SubscriptionPlan.Code`
  and reading `FeatureFlags.Sso` (US-ADM-009). An SSO write (any non-null SSO request field) by a non-entitled
  tenant returns `Result.Failure(..., 403, errorCode: "sso_not_entitled")`. The controller was updated to pass
  `result.ErrorCode` through `ApiResponse.Fail`.
- **Break-glass (AC-7):** `HasLocalBreakGlassAdminAsync` = ≥1 ACTIVE membership whose user is active AND has a
  non-null `PasswordHash` AND holds `Tenant Owner`/`Tenant Admin`. `sso_only` is blocked without it. (Real
  US-AUTH-016 enforcement is still separate; this is just the precondition guard.)
- **Privilege ceiling (BR-5):** `PermissionCatalog.BuiltInRoles.PrivilegedForJit` = {Tenant Owner, Tenant Admin,
  System Admin}. Enforced both stateless (validator, by name) and stateful (service also checks role-exists).
- **Cache (NFR-1):** `AuthService.GetSsoSettingsAsync(tenantId)` is a cache-aside seam (`sso-settings:{tenantId}`,
  10-min TTL, IDistributedCache) returning `SsoSettingsSnapshot`; invalidated on every SSO write. Single writer =
  safe invalidation.
- **FR-8 GAP (flagged out-of-lane):** the login/callback isolation guard `EntraSsoService.CheckIsolation` still
  reads the allow-list from **configuration** (`_options.TenantAllowList`), NOT from these DB settings.
  **US-AUTH-013 must be rewired to consume `GetSsoSettingsAsync`** for FR-8/NFR-1 to actually pay off. Until then
  the DB config is authored but not consumed by the login path. See [[reference-notification-delivery-infra]]-style
  "seam built, consumer deferred" pattern.
