---
id: US-ADM-006
module: Admin Console — Tenant Admin
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-006: Tenant Admin Configures Company Settings (Logo, Colors, Policies)

## 1. Description
**As a** Tenant Admin,
**I want to** configure my organization's profile, branding (logo, favicon, brand colors), localization preferences (language, date/number formats, time zone, currency), and operational policies (fiscal year, password policy, session policy),
**So that** the platform reflects my company's identity and operational requirements, providing a consistent branded experience for all employees within my tenant.

## 2. Preconditions
- The Tenant Admin is authenticated on `{subdomain}.yourhrm.com` with the `TenantAdmin` role.
- The tenant is in `active` or `trial` status.
- File storage service is operational (for logo/favicon uploads).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The Tenant Admin navigates to Organization Profile settings | They update the company name, legal name, registration number, address, industry, and size, then save | The `tenant_setting` records are updated for the current tenant. Changes are reflected immediately across the tenant's UI (e.g., company name in the sidebar/header). The change is recorded in the tenant's `audit_log` with before/after values. No other tenant's settings are visible or affected. |
| AC-2 | The Tenant Admin navigates to Branding settings | They upload a new logo (main), email logo, and favicon, and select a primary brand color using a color picker | The files are stored in the tenant-scoped storage path (`{tenantId}/branding/`), URLs are saved in `tenant_setting`, and the UI immediately reflects the new branding. The login page for `{subdomain}.yourhrm.com` shows the uploaded logo and primary color. Email templates use the email logo. PDFs (payslips, letters) use the main logo. |
| AC-3 | The Tenant Admin configures localization settings | They set the default language (from system-supported list), date format, number format, time zone, and currency | These settings become the defaults for all users in the tenant (overridable at user level per the configuration hierarchy). The UI renders dates, numbers, and currency in the chosen formats. The change is audited. |
| AC-4 | The Tenant Admin configures the password policy | They set minimum password length, complexity requirements (uppercase, lowercase, digit, special), password history count, and max password age | The policy is saved in `tenant_setting` and enforced on all future password changes/resets for users in this tenant. Existing passwords are not retroactively invalidated but will be checked at next change. The policy is audited. |
| AC-5 | The Tenant Admin attempts to access or modify settings for another tenant (e.g., by manipulating API parameters) | The API processes the request | The request is rejected. The `ITenantContext` ensures all reads and writes target only the current tenant's `tenant_setting` rows. PostgreSQL RLS blocks any cross-tenant access at the database layer. The API returns 404. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The organization profile settings endpoint shall read/write `tenant_setting` records keyed by the current `ITenantContext.TenantId`. Settings keys include: `org.name`, `org.legal_name`, `org.registration_number`, `org.address`, `org.industry`, `org.size`, `org.fiscal_year_start`.
- FR-2: The branding endpoint shall accept file uploads (logo: PNG/SVG, max 2MB; favicon: ICO/PNG, max 500KB) and store them at `{tenantId}/branding/{filename}`. File URLs shall be returned as tenant-scoped signed URLs or CDN paths.
- FR-3: The color theme settings shall accept a primary color (hex), and the system shall auto-generate complementary shades (dark, light, contrast text) for consistent UI rendering via CSS custom properties.
- FR-4: Localization settings shall validate against the system-supported languages list (available from `ITenantContext.EnabledModules` or a system configuration endpoint).
- FR-5: Password policy settings shall be stored as structured JSON in `tenant_setting` with key `security.password_policy` and enforced by the password validation service on every password change.
- FR-6: Session policy settings (idle timeout, absolute timeout, max concurrent sessions per user) shall be stored and enforced by the authentication middleware.
- FR-7: All settings changes shall trigger a cache invalidation for the tenant's configuration cache key (`t:{tenantId}:config`).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Settings pages shall load within 1.5 seconds, including logo preview rendering.
- NFR-2: Logo uploads shall be validated server-side for file type (magic bytes, not just extension) and size before storage.
- NFR-3: Branding changes shall propagate to all active sessions within 60 seconds (via cache invalidation + SignalR notification or next page load).
- NFR-4: All settings changes shall be audited with before/after values to the tenant's `audit_log`.
- NFR-5: The settings UI shall be fully mobile responsive (360px to 4K).

## 6. Business Rules
- BR-1: Only `TenantAdmin` and `TenantOwner` roles may modify company settings.
- BR-2: Settings follow the configuration hierarchy: user preference overrides tenant setting overrides plan default overrides system default. Tenant Admin configures the "tenant setting" layer.
- BR-3: Certain settings are constrained by the subscription plan. For example, a tenant on the "Starter" plan may not enable features flagged as enterprise-only (e.g., custom CSS, white-label). The UI shall disable such options with a "Upgrade your plan" indicator.
- BR-4: The fiscal year start month affects leave accrual, payroll cycles, and reporting periods across the tenant.
- BR-5: The default language set by the Tenant Admin applies to all users who have not set a personal language preference.
- BR-6: Logo and branding files are tenant-scoped in storage; Tenant A's branding files are inaccessible to Tenant B.

## 7. Data Requirements
- **Input:** Settings key-value pairs (see FR-1), file uploads (logo, favicon).
- **Output:** Current settings state, file URLs.
- **Tables affected:** `tenant_setting` (upsert by tenant_id + key), file storage (`{tenantId}/branding/`), `audit_log`.
- **Settings keys:** `org.name`, `org.legal_name`, `org.registration_number`, `org.address`, `org.industry`, `org.size`, `org.fiscal_year_start`, `branding.logo_url`, `branding.email_logo_url`, `branding.favicon_url`, `branding.primary_color`, `locale.default_language`, `locale.date_format`, `locale.number_format`, `locale.time_zone`, `locale.currency`, `security.password_policy`, `security.session_policy`, `security.mfa_policy`.

## 8. UI/UX Notes
- Settings organized in a tabbed or sidebar-navigated layout: Organization Profile, Branding, Localization, Security & Policies.
- Branding tab: drag-and-drop upload zones for logo/favicon with live preview showing how the logo appears in the sidebar, login page, and email header.
- Color picker: a hex input with a visual color swatch, plus a live preview panel showing the primary color applied to buttons, links, and the sidebar.
- Localization tab: dropdowns for language, date format (with example preview: "11 May 2026" vs "05/11/2026"), number format, currency, and time zone.
- Password policy: sliders or number inputs for length/history, toggle switches for complexity requirements.
- Each section has a "Save" button that is disabled until changes are detected (dirty form tracking).
- Notion-like aesthetic: clean sections with subtle dividers, grouped fields with descriptive labels, smooth save transitions.

## 9. Dependencies
- US-ADM-001: Tenant must be provisioned.
- File storage service (tenant-scoped paths).
- Redis cache for configuration cache invalidation.
- Internationalization (i18n) framework for language support.
- Authentication middleware for password/session policy enforcement.

## 10. Assumptions & Constraints
- Custom CSS injection (white-label enterprise feature) is deferred to Phase 2.
- Login page customization beyond logo and primary color (e.g., background image, custom text) is deferred to Phase 2.
- The system supports English + one secondary language out of the box; additional languages are framework-supported but require translation files.
- File storage uses the platform's configured storage provider (local/S3/Azure Blob); the tenant-scoped path prefix ensures isolation.

## 11. Test Hints
- **Branding update:** Upload a logo for Tenant A; verify it appears on Tenant A's login page but not on Tenant B's login page.
- **Color theme:** Set a primary color; verify CSS custom properties are updated and buttons/links reflect the new color.
- **Localization:** Set date format to `DD/MM/YYYY`; verify all date displays in the tenant UI use the new format.
- **Password policy enforcement:** Set minimum length to 12; attempt to set a 10-character password; verify rejection.
- **Cross-tenant isolation:** As Tenant A admin, attempt to read `tenant_setting` records for Tenant B via API manipulation; verify 404 or empty result.
- **File storage isolation:** Attempt to access Tenant A's logo URL from Tenant B's context; verify access denied.
- **Cache invalidation:** Update a setting; verify the change is reflected in a new browser tab within 60 seconds.
- **Audit trail:** Change the company name; verify the audit log records the old and new values with the Tenant Admin's identity.
- **Plan-gated features:** On a Starter plan, verify that enterprise-only settings are disabled in the UI and rejected by the API.
