---
id: US-AUTH-006
module: Authentication & Authorization
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-006: Role-based access control (RBAC) per tenant

## 1. Description
**As a** tenant admin,
**I want to** define roles with granular permissions and assign them to users within my tenant,
**So that** each user can access only the features and data appropriate to their responsibilities, maintaining security and compliance.

## 2. Preconditions
- The tenant is provisioned and in `active` or `trial` state.
- The tenant admin is authenticated with `Tenant Admin` or `Tenant Owner` role.
- Built-in roles (Tenant Owner, Tenant Admin, HR Officer, Manager, Employee, Recruiter, Auditor) are seeded during tenant provisioning.
- The `role`, `role_permission`, `user_tenant`, and `user_tenant_role` tables exist.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant admin navigates to the Roles management page | They view the list of roles | The system displays all built-in roles (marked as non-editable) and any custom roles created for this tenant, with their permission counts and user counts. |
| AC-2 | A tenant admin creates a custom role with selected permissions | They submit `POST /api/v1/tenant/roles` with `{ name, description, permissions[] }` | The system creates a new role scoped to the current tenant (`tenant_id` set), associates the selected permissions, and returns the created role. |
| AC-3 | A tenant admin assigns roles to a user's tenant membership | They update the user's roles via `PATCH /api/v1/tenant/users/{id}` with `{ roleIds[] }` | The system updates `user_tenant_role` records; the user's next JWT (on refresh) includes the updated `roles[]` and `permissions[]` claims. |
| AC-4 | A user with the "Employee" role attempts to access an HR-only endpoint (e.g., `GET /api/v1/tenant/payroll/runs`) | The authorization middleware evaluates the request | The system returns 403 Forbidden because the user lacks the `Payroll.View` permission. |
| AC-5 | A manager attempts to approve a leave request for an employee NOT in their team | The resource authorization handler checks team scope | The system returns 403 Forbidden because resource-level authorization validates the manager-report relationship, even though they have the `Leave.Approve.Team` permission. |
| AC-6 | A tenant admin attempts to delete a built-in role | They call `DELETE /api/v1/tenant/roles/{id}` for a built-in role | The system returns 400 Bad Request with "Built-in roles cannot be deleted." |
| AC-7 | A user belongs to two tenants with different roles | They access each tenant via their respective subdomains | The JWT for tenant A contains roles from tenant A only; the JWT for tenant B contains roles from tenant B only. Roles never leak across tenants. |

## 4. Functional Requirements
- FR-1: The system SHALL implement RBAC with permissions following the pattern `Module.Action[.Scope]` (e.g., `Leave.Approve.Team`, `Employee.View.All`, `Payroll.Run`).
- FR-2: Roles SHALL be scoped to a tenant (`role.tenant_id` = current tenant); built-in roles have `is_built_in = true` and cannot be edited or deleted.
- FR-3: Permissions SHALL be assigned to roles via the `role_permission` table.
- FR-4: Users SHALL be assigned roles through `user_tenant_role` (many-to-many between `user_tenant` and `role`).
- FR-5: Authorization SHALL be enforced at three layers: (1) Authentication middleware validates JWT and tenant claim, (2) Policy-based authorization checks role/permission claims on controllers, (3) Resource-based authorization handlers validate record-level access (e.g., manager's direct reports).
- FR-6: The CRUD endpoints for roles SHALL be: `GET/POST /api/v1/tenant/roles`, `GET/PUT/DELETE /api/v1/tenant/roles/{id}`.
- FR-7: Role and permission changes SHALL be audited in the tenant audit log.
- FR-8: The `Tenant Owner` role SHALL always retain full permissions and cannot be removed from the primary tenant owner.
- FR-9: System roles (System Super Admin, System Support, System Billing, System Compliance) SHALL exist only in the system tenant and cannot be created in regular tenants.
- FR-10: EF Core global query filters and PostgreSQL RLS SHALL ensure role and permission queries are scoped to the current tenant.

## 5. Non-Functional Requirements
- NFR-1: Permission evaluation SHALL add <= 5 ms overhead per request (permissions cached in JWT claims).
- NFR-2: Role and permission data for active users SHALL be cached in Redis with the key pattern `t:{tenantId}:user:{userId}:permissions` and invalidated on role changes.
- NFR-3: The system SHALL support at least 50 custom roles per tenant and 200+ distinct permissions.
- NFR-4: Authorization failures SHALL be logged with details (user, endpoint, missing permission) for security monitoring.

## 6. Business Rules
- BR-1: Roles are per-tenant-membership. The same person can be "Manager" in tenant A and "Employee" in tenant B.
- BR-2: Built-in roles are seeded during tenant provisioning and cannot be modified or deleted. Their permissions can be viewed but not changed.
- BR-3: Custom roles can combine any subset of the available permissions.
- BR-4: A user can have multiple roles; their effective permissions are the union of all assigned role permissions.
- BR-5: Removing a role from a user takes effect on the next token refresh (JWT is stateless within its lifetime).
- BR-6: The `Tenant Owner` role includes all permissions and at least one user must always hold it.
- BR-7: Deleting a custom role that is assigned to users requires confirmation; the role is removed from those users' assignments.

## 7. Data Requirements
- **`role` table:** `role_id` (uuid PK), `tenant_id` (FK, null for system roles), `name` (varchar 100, unique within tenant), `description` (varchar 500), `is_built_in` (boolean), `created_at`.
- **`role_permission` table:** `role_id` (uuid PK FK), `permission` (varchar 100 PK) -- composite PK.
- **`user_tenant_role` table:** `user_tenant_id` (uuid PK FK), `role_id` (uuid PK FK), `assigned_at`, `assigned_by`.
- **JWT claims:** `roles: ["Manager", "Recruiter"]`, `permissions: ["Leave.Approve.Team", "Recruitment.Manage"]`.
- **Permission catalog:** maintained in code/configuration as the source of truth for all available permissions.

## 8. UI/UX Notes
- Notion-like design for the Roles management page:
  - Card-based layout showing each role as a card with name, description, user count, and an "Edit" action.
  - Built-in roles display a lock icon and "Built-in" badge; clicking shows permissions read-only.
  - Custom role creation/editing: a clean form with a permission tree grouped by module (Leave, Attendance, Payroll, etc.) with checkboxes for each action.
- User management page: multi-select dropdown for assigning roles to a user, showing current roles as chips/tags.
- Permission denied: a clean 403 page with "You don't have permission to access this page" and a "Go to Dashboard" link.
- Mobile responsive: role cards stack vertically; permission tree collapses into expandable sections.

## 9. Dependencies
- US-AUTH-001 (Login) and US-AUTH-002 (JWT) for token issuance with role/permission claims.
- US-AUTH-007 (Tenant resolution) for tenant-scoped role queries.
- Tenant provisioning flow for seeding built-in roles.
- Multi-tenancy infrastructure (EF Core filters, RLS) for role data isolation.

## 10. Assumptions & Constraints
- Permissions are defined in the codebase (not user-configurable permission names); only the assignment of permissions to roles is configurable.
- ABAC (Attribute-Based Access Control) is not in scope for Phase 1; resource-level scoping (e.g., team scope) is handled via custom authorization handlers.
- Role changes take effect on next token refresh, not immediately on the current access token.
- The permission catalog will grow as new modules are added; the role management UI must handle this gracefully.

## 11. Test Hints
- **Happy path:** Create custom role, assign to user, verify user can access permitted endpoints and is blocked from others.
- **Built-in role protection:** Attempt to edit/delete built-in roles; verify rejection.
- **Permission union:** Assign two roles with overlapping permissions; verify effective permissions are the union.
- **Resource-level authorization:** Manager with `Leave.Approve.Team` tries to approve for non-report; verify 403.
- **Cross-tenant isolation:** Verify roles from tenant A do not appear in tenant B queries; verify JWT claims are tenant-scoped.
- **Role deletion with users:** Delete a custom role assigned to users; verify role is removed from their assignments and their next JWT reflects the change.
- **Tenant Owner protection:** Attempt to remove Tenant Owner role from the sole owner; verify rejection.
- **System role isolation:** Verify system roles cannot be created or assigned within regular tenants.
- **Cache invalidation:** Change a user's roles; verify Redis cache is invalidated and next refresh includes updated permissions.
