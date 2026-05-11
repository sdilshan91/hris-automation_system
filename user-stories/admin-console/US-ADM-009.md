---
id: US-ADM-009
module: Admin Console — System Admin
priority: Must Have
persona: System Admin (Platform Operator Staff)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-009: System Admin Manages Subscription Plans

## 1. Description
**As a** System Admin (Platform Operator Staff),
**I want to** create, edit, archive, and manage subscription plans that define pricing, feature entitlements, module access, usage limits, and feature flags for tenant organizations,
**So that** I can offer tiered product packaging (Starter, Growth, Business, Enterprise), control what each tenant can access based on their plan, and support custom enterprise plans with negotiated limits.

## 2. Preconditions
- The System Admin is authenticated at `admin.yourhrm.com` with the `SystemAdmin` role.
- The `subscription_plan` table exists with the schema defined in the technical document (Section 19.2).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The System Admin navigates to the Plans section | The page loads | A list of all subscription plans is displayed showing: code, name, price (monthly/yearly), currency, active tenant count, public/private status, active/archived status. Plans are sortable by name, price, and tenant count. |
| AC-2 | The System Admin creates a new plan | They fill in: code (unique slug), name, description, monthly/yearly price, currency, trial days, and configure all limits (max employees, max storage GB, max API calls/month, max email sends/month, max custom roles, max custom fields, max workflows, max integrations, max concurrent sessions, audit log retention days, SLA tier), enable/disable modules (from the module list), and set feature flags (SSO, custom domain, white-label, SCIM, sandbox). They save | A new `subscription_plan` record is created. The plan is available for assignment to new tenants. The action is logged in `system_audit_log`. |
| AC-3 | The System Admin edits an existing plan that is assigned to active tenants | They change `max_employees` from 100 to 200 and save | The plan record is updated. Existing tenants on this plan immediately benefit from the increased limit (since limits are read from the plan at runtime). The edit is audited with before/after values. A notification is optionally sent to affected tenant admins about the plan change. |
| AC-4 | The System Admin archives a plan | They click "Archive" on a plan | The plan's `is_active` is set to `false`. It no longer appears as an option when provisioning new tenants. Existing tenants on this plan are unaffected and continue operating with their current plan. The action is logged. |
| AC-5 | The System Admin creates a custom enterprise plan with per-tenant limit overrides | They create a plan with `is_public = false` and configure a `plan_limit_override` for a specific tenant (e.g., `max_employees = unlimited` for Tenant X) | The `plan_limit_override` record is created, linked to the specific tenant. At runtime, the system checks `plan_limit_override` before `subscription_plan` limits. The override is audited. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The plan management endpoints shall support CRUD operations on the `subscription_plan` table, accessible only from the system admin context (`admin.yourhrm.com`).
- FR-2: The plan editor shall expose all fields from the `subscription_plan` schema: `code`, `name`, `is_public`, `is_active`, `max_employees`, `max_storage_gb`, `max_api_calls_per_month`, `max_email_sends_per_month`, `max_custom_roles`, `max_custom_fields_per_entity`, `max_workflows`, `enabled_modules` (JSONB array), `feature_flags` (JSONB object), `audit_log_retention_days`, `sla_tier`, `trial_days`, `price_per_month`, `price_per_year`, `currency`.
- FR-3: The `code` field shall be unique, lowercase, alphanumeric + hyphens, and immutable after creation (to prevent breaking references).
- FR-4: Plan limit overrides shall be managed via the `plan_limit_override` table: `tenant_id`, `limit_key`, `value`, `expires_at` (optional). The runtime limit resolution order is: `plan_limit_override` (if exists and not expired) > `subscription_plan` field.
- FR-5: The plan list shall show the count of active tenants currently on each plan.
- FR-6: Module toggles (`enabled_modules`) shall reference the canonical module list: CoreHR (always enabled), Leave, Attendance, Recruitment, Onboarding, Payroll, Performance, Training, Asset, Benefits, Reporting, CustomReportBuilder, PublicCareersPage.
- FR-7: Deleting a plan is not allowed if any tenant (including terminated tenants with retained records) references it. Only archiving is permitted.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Plan changes shall propagate to affected tenants within 60 seconds (via cache invalidation of `t:{tenantId}:config`).
- NFR-2: The plan management UI shall load within 1.5 seconds.
- NFR-3: All plan management operations shall be logged to `system_audit_log` with full before/after state.
- NFR-4: Plan data shall be cached in Redis and invalidated on any plan update.
- NFR-5: The plan editor shall be accessible only from the system admin console; tenant admin users cannot view or modify plans.

## 6. Business Rules
- BR-1: Only `SystemAdmin` role may create, edit, or archive plans. `SystemSupport` and `Billing` roles may have read-only access.
- BR-2: A plan marked `is_public = true` appears on the pricing page (Phase 2 self-serve). `is_public = false` plans are for custom/enterprise deals only.
- BR-3: `NULL` values for limit fields mean "unlimited."
- BR-4: Lowering a plan limit does not retroactively affect existing tenants. For example, if `max_employees` is reduced from 200 to 100 and a tenant already has 150 employees, they keep their employees but cannot add new ones until they are under the new limit.
- BR-5: The plan `code` cannot be reused once created, even if the plan is archived.
- BR-6: Module entitlements (`enabled_modules`) determine which Angular lazy-loaded feature modules are accessible and which API endpoints are gated per tenant.
- BR-7: Plan changes do not affect in-flight operations (e.g., a payroll run started before a plan downgrade continues to completion).

## 7. Data Requirements
- **Input:** All `subscription_plan` fields (see FR-2), optional `plan_limit_override` entries.
- **Output:** Plan record with all fields, active tenant count.
- **Tables affected:** `subscription_plan`, `plan_limit_override`, `system_audit_log`.
- **Related tables:** `tenant` (FK to `subscription_plan_id`), `tenant_setting` (inherits plan defaults).

## 8. UI/UX Notes
- Plan list: card-based layout with key metrics per plan (name, price, tenant count, status badge). Alternatively, a data table with expandable rows.
- Plan editor: a comprehensive form organized in sections: General (code, name, description, pricing), Limits (numeric inputs for each limit, with "Unlimited" toggle per field), Modules (checkbox grid with module icons), Feature Flags (toggle switches), and SLA/Trial (dropdowns and number inputs).
- Plan limit overrides: accessible from the tenant detail view in the System Admin console. A section showing "Custom Limits" with the ability to add/edit/remove overrides for that tenant.
- Comparison view: a side-by-side plan comparison table showing all limits/modules/features across plans (useful for pricing page and internal reference).
- Notion-like aesthetic: clean form sections with clear grouping, subtle dividers, and responsive layout.

## 9. Dependencies
- US-ADM-001: Plans must exist before tenants can be provisioned.
- US-ADM-002: Monitoring dashboard uses plan limits for usage gauge calculations.
- Redis cache for plan data propagation.
- Module gating middleware in both Angular (route guards) and ASP.NET Core (authorization policies).

## 10. Assumptions & Constraints
- Phase 1: Billing integration (Stripe, etc.) is offline/manual. The plan defines pricing, but actual payment processing is not automated.
- Self-serve plan changes by tenant admins are deferred to Phase 2.
- Coupons and discounts are captured in the technical document but deferred to Phase 2.
- Proration policy for mid-cycle plan changes is handled offline in Phase 1.
- The plan schema is designed to be extensible; new limit fields can be added via migrations without breaking existing plans.

## 11. Test Hints
- **Create plan:** Create a "Growth" plan with specific limits; verify all fields are persisted correctly in `subscription_plan`.
- **Unique code:** Attempt to create two plans with the same code; verify the second is rejected.
- **Assign to tenant:** Create a plan, provision a tenant with it; verify the tenant inherits the plan's limits and enabled modules.
- **Edit plan propagation:** Edit a plan's `max_employees` upward; verify affected tenants can now create more employees. Edit downward; verify existing data is preserved but new creations are blocked when over limit.
- **Archive plan:** Archive a plan; verify it no longer appears in the provisioning dropdown but existing tenants are unaffected.
- **Limit override:** Create a plan with `max_employees = 50`, assign to a tenant, add a `plan_limit_override` with `max_employees = 200` for that tenant; verify the tenant can have up to 200 employees.
- **Module gating:** Create a plan without the Recruitment module; provision a tenant; verify the Recruitment routes/APIs are inaccessible for that tenant.
- **Cannot delete referenced plan:** Attempt to delete a plan that has tenants assigned; verify rejection.
- **Audit trail:** Create and edit a plan; verify all actions are recorded in `system_audit_log` with correct before/after values.
