---
id: US-AUTH-008
module: Authentication & Authorization
priority: Should Have
persona: Cross-Tenant User
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-AUTH-008: Cross-tenant user switching without re-auth

## 1. Description
**As a** cross-tenant user (e.g., a payroll vendor, auditor, or parent-company admin who belongs to multiple tenants),
**I want to** switch between my tenant memberships without re-entering my credentials,
**So that** I can efficiently manage work across multiple organizations from a single login session.

## 2. Preconditions
- The user is authenticated with a valid JWT and refresh token for one tenant.
- The user has active memberships (`user_tenant.status = active`) in two or more tenants.
- All target tenants are in `active` or `trial` state.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user is logged into tenant A and has active memberships in tenants A and B | They request `GET /api/v1/auth/my-tenants` | The system returns a list of all tenants the user belongs to, with each tenant's `tenantId`, `subdomain`, `name`, `logoUrl`, `status`, and the user's roles in that tenant. |
| AC-2 | The user selects tenant B from the tenant switcher | They call `POST /api/v1/auth/switch-tenant` with `{ tenantId: "<tenant-B-id>" }` | The system verifies the user's active membership in tenant B, issues a new JWT with `tenant_id` = tenant B and the user's roles/permissions for tenant B, issues a new refresh token scoped to tenant B, and redirects the browser to `https://tenant-b.yourhrm.com/dashboard`. |
| AC-3 | The user attempts to switch to a tenant they do not belong to | They call `POST /api/v1/auth/switch-tenant` with an unauthorized `tenantId` | The system returns 403 Forbidden with "You do not have an active membership in this organization." |
| AC-4 | The user attempts to switch to a suspended tenant | They select the suspended tenant from the switcher | The system returns 403 Forbidden with a message indicating the target tenant is unavailable. The tenant switcher UI shows the tenant as grayed out with a "Suspended" badge. |
| AC-5 | The user switches from tenant A to tenant B | The new JWT for tenant B is issued | The JWT contains ONLY the roles and permissions for tenant B; no roles or permissions from tenant A are present. The refresh token for tenant A remains valid (the user can navigate back). |

## 4. Functional Requirements
- FR-1: The my-tenants endpoint SHALL be `GET /api/v1/auth/my-tenants`, returning all tenant memberships for the authenticated user.
- FR-2: The switch-tenant endpoint SHALL be `POST /api/v1/auth/switch-tenant` accepting `{ tenantId: uuid }`.
- FR-3: On tenant switch, the system SHALL issue a new JWT and new refresh token scoped to the target tenant.
- FR-4: The previous tenant's refresh token SHALL remain valid; the user maintains independent sessions per tenant.
- FR-5: The system SHALL verify the user has an active `user_tenant` membership in the target tenant before issuing tokens.
- FR-6: The system SHALL verify the target tenant's lifecycle state allows login (`active` or `trial`).
- FR-7: Tenant switch events SHALL be audited in both the source tenant and target tenant audit logs.
- FR-8: The frontend SHALL handle the subdomain change by redirecting the browser to the target tenant's subdomain URL.
- FR-9: The `GET /api/v1/auth/me` endpoint SHALL return the current user's profile, current tenant context, and a list of all tenant memberships.

## 5. Non-Functional Requirements
- NFR-1: Tenant switch response time SHALL be <= 400 ms at P95 (includes new token generation and membership verification).
- NFR-2: The my-tenants endpoint SHALL be cached in Redis per user (`user:{userId}:tenants`) with invalidation on membership changes.
- NFR-3: Cross-tenant switching SHALL NOT expose any data from the source tenant in the response.
- NFR-4: The switch mechanism SHALL work correctly behind a load balancer where the user may hit different API instances.

## 6. Business Rules
- BR-1: A single login session (user authenticated once) can switch between tenants without re-authentication; each switch issues a new JWT with the target tenant's context.
- BR-2: Roles are per-membership: the same user may be "Tenant Admin" in tenant A and "Employee" in tenant B. The JWT after switching reflects only the target tenant's roles.
- BR-3: If MFA is required by the target tenant and the user has not enrolled, switching to that tenant SHALL trigger mandatory MFA enrollment.
- BR-4: Impersonation sessions cannot use tenant switching; the system admin must end impersonation and initiate a new one for a different tenant.
- BR-5: The tenant list returned by `my-tenants` includes all memberships regardless of tenant status, but non-accessible tenants are flagged as such (e.g., `suspended`, `terminated`).

## 7. Data Requirements
- **`user_tenant` table:** queried by `user_id` to find all memberships with joined `tenant` data.
- **My-tenants response:** `[{ tenantId, subdomain, name, logoUrl, status, roles: string[], isCurrentTenant: boolean }]`.
- **Switch-tenant request:** `{ tenantId: uuid }`.
- **Switch-tenant response:** `{ accessToken: string, tenant: { tenantId, subdomain, name }, redirectUrl: string }` (new refresh token set via cookie).
- **Audit records:** `tenant_switch` event with `source_tenant_id`, `target_tenant_id`, `user_id`, IP, user agent.

## 8. UI/UX Notes
- Notion-like workspace switcher: a dropdown in the top-left of the app shell (next to the current tenant logo/name) showing all tenant memberships.
- Each tenant in the dropdown shows: tenant logo (small), tenant name, user's primary role, and status badge (if not active).
- Current tenant is highlighted with a checkmark.
- Suspended/terminated tenants are grayed out with a tooltip explaining unavailability.
- Clicking a different tenant triggers the switch and redirects to that tenant's subdomain seamlessly.
- On the login page, after authentication, if the user has multiple memberships, optionally show a tenant picker before landing on a specific tenant dashboard.
- Mobile: tenant switcher accessible from the top header bar, presented as a full-width dropdown.

## 9. Dependencies
- US-AUTH-001 (Login) for initial authentication.
- US-AUTH-002 (JWT/Refresh) for new token issuance on switch.
- US-AUTH-006 (RBAC) for loading the target tenant's roles and permissions.
- US-AUTH-007 (Tenant resolution) for the target tenant's subdomain URL.
- Multi-tenancy infrastructure for user-tenant membership data.

## 10. Assumptions & Constraints
- Tenant switching involves a browser redirect to the target tenant's subdomain; it is not a purely client-side operation.
- The source tenant's session (refresh token) remains valid after switching; the user can navigate back by visiting the source tenant's subdomain.
- The number of tenants a single user can belong to is practically unlimited but typically < 10.
- Custom domains are not supported in Phase 1; switching always uses the `{slug}.yourhrm.com` pattern.

## 11. Test Hints
- **Happy path:** Log in to tenant A, switch to tenant B, verify new JWT has tenant B's ID, roles, and permissions.
- **Unauthorized switch:** Attempt to switch to a tenant without membership; verify 403.
- **Suspended target:** Switch to a suspended tenant; verify 403 and appropriate error message.
- **Cross-tenant claim isolation:** After switch, verify JWT contains ZERO claims from the source tenant.
- **Source session preserved:** After switching to B, navigate back to A's subdomain; verify A's session still works.
- **MFA enforcement on switch:** Switch to a tenant requiring MFA without enrollment; verify MFA enrollment is triggered.
- **Audit trail:** Verify tenant switch is logged in both source and target tenant audit logs.
- **My-tenants caching:** Update membership; verify cache is invalidated and fresh data is returned.
- **Impersonation guard:** During impersonation, attempt switch; verify it is blocked.
