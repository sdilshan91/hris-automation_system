---
id: US-AUTH-012
module: Authentication & Authorization
priority: Should Have
persona: Tenant Admin
status: draft
created: 2026-06-21
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-012: Per-tenant SSO configuration

## 1. Description
**As a** tenant admin,
**I want to** enable and configure Microsoft Entra SSO for my organization — including which Entra directory and email domains are trusted and the default role for new users,
**So that** my employees can sign in with Microsoft and only my organization's users can enter my workspace.

**As a** platform,
**I want** SSO configuration to live on the existing `TenantAuthSettings` and to be gated by the subscription plan's SSO entitlement,
**So that** the feature is consistent with other auth policy, properly isolated per tenant, and only available to entitled customers.

## 2. Preconditions
- The OIDC foundation (US-AUTH-011) exists.
- `TenantAuthSettings` already stores per-tenant MFA, session, and lockout policy and is editable by tenant admins (existing `TenantAuthSettingsController`).
- The tenant's subscription plan exposes `PlanFeatureFlags.Sso` (US-ADM-009).
- The acting user has a tenant-admin role in the resolved tenant (US-AUTH-006).

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The tenant's plan has `Sso = true` | A tenant admin opens Security / SSO settings | The system shows the SSO configuration section (enable toggle, allowed Entra tenant IDs, allowed email domains, default JIT role, enforcement mode) populated with current values or sensible defaults (SSO disabled by default). |
| AC-2 | The tenant's plan has `Sso = false` | A tenant admin opens Security settings | The SSO section is hidden or shown disabled with an "upgrade your plan" note; any API attempt to enable SSO returns 403 `sso_not_entitled`. |
| AC-3 | A tenant admin enters one or more Entra **tenant IDs** (`tid`, GUID format) and/or verified email **domains** and saves | They submit the SSO settings | The system validates each `tid` is a well-formed GUID and each domain is a valid domain, persists them scoped to the current tenant, and logs an `sso_config_updated` audit event. |
| AC-4 | A tenant admin tries to enable SSO with an **empty** allow-list (no `tid` and no domain) | They submit | The system rejects the save with a validation error ("Add at least one trusted directory or email domain before enabling SSO") — SSO cannot be enabled without an allow-list (fail-closed config). |
| AC-5 | A tenant admin sets a **default role** for JIT-provisioned users | They submit | The system validates the role exists in the tenant's role set (US-AUTH-006) and is not a privileged/admin role above an allowed ceiling; otherwise it rejects with a validation error. |
| AC-6 | SSO settings are saved for tenant A | A tenant admin of tenant B reads or writes SSO settings | Tenant B sees only tenant B's settings; tenant A's `tid`/domains/role are never visible or writable from tenant B (tenant isolation). |
| AC-7 | A tenant admin sets the **enforcement mode** to `sso_only` | They submit | The system accepts it **only if** a break-glass admin path is preserved (US-AUTH-016); otherwise it warns and blocks until enforcement preconditions are met. |

## 4. Functional Requirements
- FR-1: The system SHALL extend `TenantAuthSettings` with SSO fields: `sso_enabled` (bool), `allowed_entra_tenant_ids` (list of GUID), `allowed_email_domains` (list of string), `jit_default_role` (string/role ref), `jit_enabled` (bool), `enforcement_mode` (`optional` | `sso_only`).
- FR-2: The system SHALL expose read/write of these fields through the existing tenant auth settings endpoint/controller, scoped to the resolved tenant.
- FR-3: The entire SSO settings surface SHALL be gated on `PlanFeatureFlags.Sso`; writes by a tenant whose plan lacks the entitlement SHALL return 403 `sso_not_entitled`.
- FR-4: The system SHALL validate `tid` values as GUIDs and `allowed_email_domains` as syntactically valid domains; invalid entries SHALL be rejected.
- FR-5: The system SHALL prevent enabling SSO with an empty allow-list and prevent `sso_only` enforcement without a preserved break-glass path.
- FR-6: The system SHALL validate `jit_default_role` against the tenant's roles and against a maximum-privilege ceiling.
- FR-7: All create/update operations on SSO settings SHALL be written to the tenant audit log (US-NTF-004) with before/after values (excluding secrets).
- FR-8: The settings SHALL be the single source of truth consumed by US-AUTH-013 (isolation) and US-AUTH-014 (matching/JIT).

## 5. Non-Functional Requirements
- NFR-1: SSO settings reads SHALL be cacheable per tenant and invalidated on write so the callback path (US-AUTH-013) does not pay a DB round-trip per login.
- NFR-2: The settings API SHALL enforce tenant scoping at the query level (global query filter on `TenantId`) — no cross-tenant read/write is possible even with a forged identifier.
- NFR-3: The configuration UI SHALL load within 1 second and validate inline before submit.
- NFR-4: List fields (`tid`s, domains) SHALL support at least 20 entries each without performance degradation.

## 6. Business Rules
- BR-1: SSO is **disabled by default** for every tenant; enabling it is an explicit, audited admin action.
- BR-2: SSO settings are strictly per tenant; there is no global/shared SSO config that crosses tenants.
- BR-3: A tenant cannot enable SSO without at least one trusted `tid` or verified domain (fail-closed).
- BR-4: Only tenant-admin roles may view or change SSO settings; regular users and managers cannot.
- BR-5: The `jit_default_role` SHALL NOT be a system-admin or tenant-owner role (privilege-escalation guard).
- BR-6: Changing `enforcement_mode` to `sso_only` does not retroactively invalidate existing local-login sessions but governs new logins (subject to break-glass, US-AUTH-016).

## 7. Data Requirements
- **`TenantAuthSettings` new fields:** `sso_enabled` (bool, default false), `allowed_entra_tenant_ids` (jsonb/text[] of GUID), `allowed_email_domains` (jsonb/text[]), `jit_enabled` (bool, default false), `jit_default_role` (nullable role ref), `enforcement_mode` (enum/text, default `optional`).
- **DTO additions:** extend `TenantAuthSettingsRequest` / `TenantAuthSettingsResponse` with the SSO fields.
- **Audit records:** `sso_config_updated` (tenant, admin user, changed fields with before/after, excluding any secret).
- **Validation inputs:** GUID format for `tid`, RFC-compliant domain for email domains, existing-role check for default role.

## 8. UI/UX Notes
- Tenant Admin > Security Settings gains an **"Single Sign-On (Microsoft Entra ID)"** card, consistent with the existing MFA/session/lockout settings styling.
- Fields: enable toggle; multi-entry input for Entra Directory (tenant) IDs with GUID validation; multi-entry input for allowed email domains; a default-role dropdown (filtered to non-privileged roles); a JIT toggle; an enforcement-mode selector (Optional / SSO only) with an inline warning about break-glass.
- When the plan lacks the SSO entitlement, show the card disabled with a clear "Available on higher plans" badge rather than hiding the capability entirely.
- Inline validation: GUID/domain errors shown per-entry; the "Enable SSO" toggle is blocked with a tooltip until the allow-list is non-empty.
- Provide a copy-able **admin-consent URL** and the tenant's Entra Directory ID field with helper text linking to US-AUTH-016 onboarding guidance.

## 9. Dependencies
- US-AUTH-011 (OIDC foundation) — the flow these settings configure.
- US-AUTH-006 (RBAC) — role validation for `jit_default_role` and admin-only access.
- US-AUTH-013 (isolation) and US-AUTH-014 (matching/JIT) — consumers of this config.
- US-AUTH-016 (enforcement/break-glass) — gates `sso_only`.
- US-ADM-009 (`PlanFeatureFlags.Sso`) — entitlement gate.
- US-NTF-004 (audit trail) — config-change auditing.

## 10. Assumptions & Constraints
- Extending `TenantAuthSettings` is preferred over a new settings table to keep all per-tenant auth policy in one place and reuse the existing controller/validator/migration path.
- Storing `tid`/domain lists as jsonb/array columns (snake_case, per the EF naming convention) is acceptable for the expected cardinality.
- Multiple `tid`s per tenant are supported (CR-AUTH-001 OQ-4) for customers with several directories.
- Secrets (client secret/cert) are platform-level, not per-tenant, and are NOT stored in `TenantAuthSettings`.

## 11. Test Hints
- **Entitlement gate:** With `Sso=false`, assert the section is hidden/disabled and the write API returns 403 `sso_not_entitled`.
- **Empty allow-list block:** Attempt to enable SSO with no `tid`/domain; assert validation error and `sso_enabled` stays false.
- **GUID/domain validation:** Submit malformed `tid` and domain values; assert per-entry rejection.
- **Default-role guard:** Set `jit_default_role` to a privileged role; assert rejection.
- **Tenant isolation:** Save config in tenant A; as tenant B admin, assert A's values are invisible and unwritable.
- **Enforcement guard:** Set `sso_only` without break-glass preconditions; assert block; satisfy preconditions; assert it saves.
- **Audit:** Change settings; assert `sso_config_updated` recorded with before/after and no secret material.
- **Cache invalidation:** Update settings; assert the cached copy used by the login path reflects the change immediately.
