---
name: reference-rec010-autocreate-user
description: US-REC-010 FR-5/BR-7 (ISSUE-140) auto-create login account on applicant→employee conversion; the Model-A seam US-NTF-006 credential delivery hangs off
metadata:
  type: project
---

FR-5/BR-7 auto-create-user-on-hire is now IMPLEMENTED (ISSUE-140, "Model A"), previously deferred.

**Why:** converting an applicant to an employee should optionally hand them a login. Gated per-tenant so existing tenants are unchanged.

**How to apply:**
- Toggle: `Tenant.AutoCreateUserOnHire` (default false). DB default false via `TenantConfiguration`. Settable via `ITenantSettingsService.UpdateHiringSettingsAsync` → `PUT /api/v1/tenant/settings/hiring` (`Tenant.ManageSettings` perm), audited as `tenant_settings.hiring_updated`. Also surfaced read-only on the GET settings snapshot (`TenantSettingsDto.AutoCreateUserOnHire`).
- Provisioning lives in `ApplicantConversionService.TryProvisionUserAccountAsync` — creates a **passwordless** `User` (reuses an existing global user by email via IgnoreQueryFilters, no duplicate) + Active `UserTenant` + built-in `Employee` `UserTenantRole`, links `Employee.UserId`. Runs inside the SAME atomic unit as the conversion; added entities join the BUG-264 catch-block detach list (`provisioned`).
- **Still DEFERRED to US-NTF-006:** credential DELIVERY (welcome/set-password email). The account is created passwordless — NTF-006 is where the set-password link gets sent. FR-8 onboarding trigger also still deferred (no onboarding module).
- Result flag: `ConversionResultDto.UserAccountCreated` is now true iff the toggle was on (was hardcoded false).
