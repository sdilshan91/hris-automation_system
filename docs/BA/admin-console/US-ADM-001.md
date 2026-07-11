---
id: US-ADM-001
module: Admin Console — System Admin
priority: Must Have
persona: System Admin (Platform Operator Staff)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-ADM-001: System Admin Provisions New Tenant

## 1. Description
**As a** System Admin (Platform Operator Staff),
**I want to** provision a new tenant by specifying the organization name, subdomain, subscription plan, primary owner email, region, and optional trial period,
**So that** a new customer organization is onboarded onto the platform with a fully isolated workspace, seeded master data, and a welcome email sent to the tenant owner within minutes.

## 2. Preconditions
- The System Admin is authenticated at `admin.yourhrm.com` with a valid system-tenant JWT containing the `SystemAdmin` role claim.
- At least one active subscription plan exists in the `subscription_plan` table.
- The SMTP/transactional email service is configured and operational.
- The wildcard DNS (`*.yourhrm.com`) and wildcard TLS certificate are in place.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The System Admin is on the "Create Tenant" form in the System Admin Console | They fill in all required fields (name, subdomain, plan, owner email, region) and submit | A new `tenant` record is created with `status = 'trial'` (if trial days > 0) or `status = 'active'`, a `user` record is created (or linked if the email already exists globally), a `user_tenant` record with the `TenantOwner` role is created, default master data (leave types, holiday calendar template, default workflows) is seeded, and a welcome email with a set-password link is dispatched to the owner. |
| AC-2 | The System Admin enters a subdomain that is already in use or appears in the reserved subdomains list (`www`, `api`, `admin`, `app`, `mail`, `status`, `docs`, `help`, `support`, `static`, `cdn`, `dev`, `stage`, `prod`, `test`, `qa`) | They attempt to submit the form | The system displays a validation error "Subdomain is unavailable" and prevents submission. |
| AC-3 | The System Admin enters an owner email that already belongs to an existing global user | They submit the form | The system links the existing `users` record to the new tenant via a new `user_tenant` row (no duplicate user created), and the welcome email references the existing account. |
| AC-4 | The provisioning completes successfully | The System Admin views the tenant list | The new tenant appears in the list with correct status, plan, and creation timestamp. A `tenant_lifecycle_event` record with `event_type = 'created'` is written. The action is logged in `system_audit_log`. |
| AC-5 | The System Admin attempts to create a tenant with an invalid subdomain (uppercase, special characters, length > 50, or < 3 characters) | They attempt to submit | Client-side and server-side validation rejects the input with a descriptive error message. |
| AC-6 | The tenant is provisioned | Any API request to `{subdomain}.yourhrm.com` by the tenant owner | The tenant resolution middleware correctly resolves the subdomain, the `ITenantContext` is populated, and PostgreSQL RLS policies are active for the new `tenant_id`, ensuring complete data isolation from all other tenants. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The "Create Tenant" form shall collect: organization name (required, max 200 chars), subdomain (required, lowercase alphanumeric + hyphens, 3-50 chars, unique), subscription plan (dropdown from active plans), primary owner email (required, valid email format), region (dropdown), trial days (optional, default from plan).
- FR-2: Subdomain validation shall check against both the `tenant.subdomain` column and the hardcoded reserved list in real-time (debounced availability check).
- FR-3: On successful creation, the backend shall execute a transactional operation that creates the `tenant`, `user` (if new), `user_tenant`, seeds master data, and writes the `tenant_lifecycle_event` atomically.
- FR-4: The welcome email shall use the system lifecycle email template (`trial_welcome` or `active_welcome`) and include: tenant name, subdomain URL, set-password link (one-time token, 72-hour expiry).
- FR-5: The `tenant_id` shall be a UUIDv7 for time-ordered generation.
- FR-6: The system shall set the PostgreSQL session variable `app.current_tenant_id` for the new tenant and verify RLS policies are effective before returning success.
- FR-7: Redis cache key `t:{tenantId}:config` shall be populated with initial tenant configuration.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Tenant provisioning (from form submit to welcome email dispatch) shall complete in under 60 seconds (target) and under 5 minutes (SLA).
- NFR-2: The provisioning operation must be idempotent — retrying with the same subdomain after a partial failure shall either complete the operation or return a clear error, never create duplicates.
- NFR-3: All provisioning actions shall be logged to `system_audit_log` with the acting System Admin's `user_id`, timestamp, IP address, and full request payload (excluding sensitive fields).
- NFR-4: The form shall be fully accessible (WCAG 2.1 AA) and responsive from 360px to 4K.

## 6. Business Rules
- BR-1: Only users with the `SystemAdmin` role in the system tenant context may provision tenants. `SystemSupport` roles are explicitly denied this capability.
- BR-2: A subdomain, once allocated, cannot be reused by another tenant even after termination (to prevent subdomain takeover).
- BR-3: If the selected plan has `trial_days > 0`, the tenant is created with `status = 'trial'` and `trial_ends_at` is set accordingly. If `trial_days = 0`, status is set to `active`.
- BR-4: The primary owner email becomes the `billing_email` by default.
- BR-5: Each tenant is created with `is_system = false`. The system tenant (`is_system = true`) cannot be created through this flow.

## 7. Data Requirements
- **Input:** `name` (string), `subdomain` (string), `subscription_plan_id` (UUID), `owner_email` (string), `region` (string), `trial_days` (int, optional).
- **Output:** `tenant_id` (UUID), `subdomain` (string), `status` (string), `created_at` (timestamptz).
- **Seeded data:** Default leave types (Annual, Sick, Casual), generic holiday calendar template, default leave-approval workflow (1-step manager approval).
- **Tables affected:** `tenant`, `users`, `user_tenant`, `user_tenant_role`, `tenant_lifecycle_event`, `system_audit_log`, `tenant_setting` (defaults), leave-related seed tables.

## 8. UI/UX Notes
- The "Create Tenant" form follows the Notion-like design aesthetic: clean whitespace, subtle shadows, smooth transitions.
- Subdomain field shows real-time availability feedback (green checkmark / red X) with debounced API call (300ms).
- Plan selection uses a card-based picker showing plan name, price, and key limits.
- On successful creation, show a success toast with the tenant URL (`{subdomain}.yourhrm.com`) as a clickable link.
- The form should support keyboard navigation and screen readers.
- Use Angular Material form fields with Tailwind CSS styling overrides.

## 9. Dependencies
- Subscription plan management must be implemented (at least one plan seeded).
- Email service integration must be operational.
- Wildcard DNS and TLS must be configured.
- PostgreSQL RLS policies must be defined for all business tables.
- Tenant resolution middleware must be implemented.

## 10. Assumptions & Constraints
- Phase 1 uses manual provisioning only; self-serve signup is deferred to Phase 2.
- Single-region deployment in Phase 1; the `region` field is captured but all tenants are provisioned in the same region.
- Billing/payment integration is handled offline in Phase 1; the system does not charge the tenant automatically.
- The global `users` table is not governed by RLS (it is a platform-level table).

## 11. Test Hints
- **Happy path:** Provision a tenant with all valid fields; verify tenant record, user record, user_tenant record, lifecycle event, audit log, and welcome email dispatch.
- **Duplicate subdomain:** Attempt to create two tenants with the same subdomain; verify the second is rejected.
- **Reserved subdomain:** Attempt each reserved subdomain; verify all are rejected.
- **Existing user:** Create a tenant with an email that already exists as a global user; verify no duplicate user is created and the existing user gains a new tenant membership.
- **Tenant isolation:** After provisioning Tenant A, authenticate as Tenant A and attempt to query Tenant B data via direct ID injection; verify 404 (not 403).
- **RLS verification:** Execute a raw SQL query using the application DB role without setting `app.current_tenant_id`; verify zero rows returned from business tables.
- **Concurrent provisioning:** Submit two tenant creations simultaneously; verify both succeed without race conditions on subdomain uniqueness.
- **Partial failure:** Simulate email service failure during provisioning; verify the tenant is still created and the email can be retried.
