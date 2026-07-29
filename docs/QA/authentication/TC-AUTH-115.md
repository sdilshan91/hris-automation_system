---
id: TC-AUTH-115
user_story: US-AUTH-012
module: Authentication
priority: high
type: functional
status: pass
created: 2026-07-24
---

# TC-AUTH-115: Entitled tenant admin saves a valid SSO configuration -> persisted + `sso_config_updated` audit event

## 1. Test Objective
Verify the happy path of US-AUTH-012: a tenant admin whose plan has `PlanFeatureFlags.Sso = true` opens the SSO settings card, enters a valid allow-list (one or more Entra `tid`s and email domains), selects a non-privileged `jit_default_role`, sets `enforcement_mode = optional`, enables SSO, and saves. The system persists the SSO fields on `TenantAuthSettings` scoped to the current tenant and writes a single `sso_config_updated` audit event with before/after values (no secret material).

## 2. Related Requirements
- User Story: US-AUTH-012
- Acceptance Criteria: AC-1, AC-3
- Functional Requirements: FR-1, FR-2, FR-7, FR-8
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" (acme.yourhrm.com) is active and its subscription plan exposes `PlanFeatureFlags.Sso = true`.
- `admin-a@acme.com` holds a tenant-admin role in acme (US-AUTH-006).
- SSO is currently disabled for acme (default state, BR-1); `TenantAuthSettings` already stores MFA/session/lockout policy.
- The role "Employee" exists in acme's role set and is non-privileged.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint (read) | GET /api/v1/tenant/auth-settings | Existing tenant auth-settings controller |
| Endpoint (write) | PUT /api/v1/tenant/auth-settings | SSO fields extend the existing DTO |
| allowed_entra_tenant_ids | ["7c9e6679-7425-40de-944b-e07fc1f90ae7"] | Well-formed GUID `tid` |
| allowed_email_domains | ["acme.com"] | RFC-valid domain |
| jit_default_role | "Employee" | Non-privileged, exists in tenant |
| jit_enabled | true | Opt-in JIT |
| enforcement_mode | "optional" | No break-glass precondition needed |
| sso_enabled | true | Enabled with a non-empty allow-list |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin-a@acme.com` at acme.yourhrm.com; open Security > Single Sign-On (Microsoft Entra ID) card. | HTTP 200 on `GET /api/v1/tenant/auth-settings`. Card renders enabled with SSO disabled by default; allow-list empty; enforcement = Optional (AC-1). |
| 2 | Enter `tid` `7c9e6679-7425-40de-944b-e07fc1f90ae7`, domain `acme.com`, select `jit_default_role = Employee`, toggle JIT on, leave enforcement = Optional, toggle SSO on. | Inline validation passes; no per-entry errors; Enable-SSO toggle is unblocked (allow-list non-empty). |
| 3 | Submit `PUT /api/v1/tenant/auth-settings` with the SSO fields. | HTTP 200 OK. Response echoes `sso_enabled = true`, the `tid`/domain lists, `jit_default_role = Employee`, `enforcement_mode = optional`. |
| 4 | Re-read `GET /api/v1/tenant/auth-settings`. | Persisted values match the submission exactly, scoped to acme's `TenantAuthSettings` row (FR-1, FR-2). |
| 5 | Inspect the tenant audit log (US-NTF-004). | Exactly one `sso_config_updated` record for acme + `admin-a@acme.com`, with before (SSO disabled/empty) and after (the new values). No client secret/cert material appears (FR-7). |

## 6. Postconditions
- acme's `TenantAuthSettings` has SSO enabled with the saved allow-list and role.
- The config is the single source of truth for US-AUTH-013/014 (FR-8).
- One audit event recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
