---
type: module-note
module: authentication
updated: 2026-06-03
---

# Authentication & Authorization Module

## RBAC Implementation (US-AUTH-006)

### Permission Catalog
- Source of truth: `HRM.Domain/Authorization/PermissionCatalog.cs`
- Pattern: `Module.Action[.Scope]` (e.g. `Leave.Approve.Team`, `Employee.View.All`)
- 50+ permissions across 12 modules
- Use `PermissionCatalog.IsValid()` for O(1) validation
- Use `PermissionCatalog.ByModule` for UI permission tree grouping
- Use `PermissionCatalog.DefaultPermissionsFor(roleName)` during tenant provisioning

### Built-in Roles
- Defined in `PermissionCatalog.BuiltInRoles` (Tenant Owner, Tenant Admin, HR Manager, HR Officer, Manager, Employee, Recruiter, Auditor)
- Seeded per-tenant via `DbInitializer.SeedBuiltInTenantRolesAsync`
- `is_built_in = true` -- cannot be edited or deleted (enforced in `RoleService`)
- Tenant Owner gets ALL permissions; at least one user must always hold it

### System Roles
- Defined in `PermissionCatalog.SystemRoles` (System Super Admin, System Support, System Billing, System Compliance)
- Exist only in the system tenant; cannot be created in regular tenants (FR-9)

### Authorization Layers (FR-5)
1. **JWT + Tenant Middleware** -- `TenantResolutionMiddleware` resolves tenant; JWT validates identity
2. **Policy-based** -- `[RequirePermission("Payroll.View")]` attribute on controllers/actions
   - Backed by `PermissionPolicyProvider` (dynamic policy creation) + `PermissionAuthorizationHandler`
3. **Resource-based** -- `TeamScopeAuthorizationHandler` validates manager-report relationship
   - Usage: call `IAuthorizationService.AuthorizeAsync(User, resource, new TeamScopeRequirement("Leave.Approve.Team"))`
   - Automatically bypasses team check if user has `.All` variant

### Caching
- `IPermissionCache` interface in Application layer
- Default: `InMemoryPermissionCache` (ConcurrentDictionary)
- TODO: Replace with Redis (`t:{tenantId}:user:{userId}:permissions`) when Redis is wired in

### Audit
- Role/permission changes logged via `ILogger` with structured properties
- TODO (US-NTF-004): Replace with `AuditLog` entity when audit module is available

### Key Decisions
- Roles are NOT inherited from BaseEntity (no TenantId auto-stamp via interceptor) because `Role.TenantId` is nullable (system roles have null)
- The `TenantInterceptor` only stamps `BaseEntity` derivatives, so Role's TenantId is set explicitly in `RoleService.CreateRoleAsync`
- Permission evaluation in JWT adds ~0ms overhead per request since permissions are embedded as claims (no DB lookup needed)
- Cache invalidation happens on role change and user-role assignment change
