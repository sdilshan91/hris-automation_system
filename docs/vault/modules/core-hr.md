---
type: module-note
module: core-hr
status: active
created: 2026-06-11
---

# Core HR

Organization structure: departments, employees, positions, and org-tree hierarchy.

## Domain rules

- Department names are unique within a tenant but may duplicate across tenants (BR-1).
- Department codes are unique within a tenant. Format: alphanumeric, hyphens, underscores.
- A department can have at most one manager (BR-2).
- A department's parent must belong to the same tenant (BR-3). Enforced by EF global query filter.
- Root departments have `parent_department_id = null` (BR-4).
- Deactivated departments cannot be assigned to new employees (BR-5).
- Deleting a parent department requires deactivating/reassigning all active child departments first (BR-6).
- Circular parent-child references are detected server-side before persisting (FR-5). The cycle-detection algorithm walks the ancestor chain from the proposed parent up to root, checking if the moved department appears.
- Soft-delete via `is_active = false`; the EF global query filter also checks `is_deleted = false`.

## Architecture decisions

### manager_id without FK constraint (deferred to US-CHR-001)

The `Department.ManagerId` column is a **nullable UUID stored without a hard foreign key constraint** to the Employee table. This is because the Employee entity does not exist yet; US-CHR-001 will create it. When US-CHR-001 lands, the following must happen:

1. Add an FK from `departments.manager_id` to `employees.id` (with `ON DELETE SET NULL`).
2. Add a navigation property `Department.Manager` of type `Employee`.
3. Update `DepartmentConfiguration` to declare `.HasOne(d => d.Manager)`.
4. Implement the AC-5 employee-count check in `DepartmentService.DeactivateAsync` (currently a TODO).

This decision avoids creating a stub Employee entity that would conflict with US-CHR-001's design.

### Unique index filters

The unique indexes on `(tenant_id, name)` and `(tenant_id, code)` use a PostgreSQL partial index: `WHERE is_deleted = false`. This allows soft-deleted departments to have names/codes that overlap with active ones, preventing naming conflicts after deactivation.

### Tenant isolation layers

Department tenant isolation is enforced at three layers:
1. **Global query filter** in `AppDbContext`: `d => !d.IsDeleted && (!_tenantContext.IsResolved || d.TenantId == _tenantContext.TenantId)`
2. **TenantInterceptor**: auto-stamps `TenantId` on new `BaseEntity` rows during `SaveChanges`.
3. **Service-level**: `DepartmentService` checks `_tenantContext.IsResolved` before every operation.

## Edge cases

- A department set as its own parent is rejected at both validator and service levels.
- Deep hierarchy cycles (A -> B -> C -> A) are caught by walking the ancestor chain (max 50 levels safety limit).
- Deactivating an already-inactive department returns a 400 error, not a silent no-op.
- Creating a child under an inactive parent is rejected.
- The tree query (`GetTree`) only returns active departments; inactive departments are excluded from the hierarchy visualization.

## Related stories

- `US-CHR-004` -- Create and Manage Departments
- `US-CHR-001` -- Create and Manage Employees (deferred; will add manager FK)
- `US-CHR-005` -- Create and Manage Job Titles and Positions
- `US-CHR-006` -- Organization Tree Visualization (consumes department hierarchy data)

## Permissions

Added to `PermissionCatalog`:
- `Department.View` -- granted to Tenant Admin, HR Manager, HR Officer, Manager
- `Department.Create` -- granted to Tenant Admin, HR Manager, HR Officer
- `Department.Edit` -- granted to Tenant Admin, HR Manager, HR Officer
- `Department.Deactivate` -- granted to Tenant Admin, HR Manager, HR Officer

## Job Titles (US-CHR-005)

### Backend domain rules

- Job title names (`title_name`) are unique within a tenant but may duplicate across tenants (BR-1).
- A job title can exist without a linked grade (BR-2).
- Deactivated job titles are hidden from assignment dropdowns but visible in admin views (BR-3).
- Employment types (Full-Time, Part-Time, Contract, Intern) are exposed as an enum reference list, not a full entity (FR-6, Phase 1 constraint).
- Soft-delete via `is_active = false`; the EF global query filter also checks `is_deleted = false`.
- Deactivating an already-inactive job title returns a 400 error, not a silent no-op.

### grade_id without FK constraint (deferred to Payroll module)

The `JobTitle.GradeId` column is a **nullable UUID stored without a hard foreign key constraint** to the Grade table. The Grade entity does not exist yet; it belongs to the Payroll module. When the Grade entity lands, the following must happen:

1. Add an FK from `job_titles.grade_id` to `grades.id` (with `ON DELETE SET NULL`).
2. Add a navigation property `JobTitle.Grade` of type `Grade`.
3. Update `JobTitleConfiguration` to declare `.HasOne(j => j.Grade)`.
4. Update `JobTitleDto` and `ToDto()` to include grade name.

Search for `TODO(payroll)` in `JobTitle.cs` and `JobTitleConfiguration.cs`.

### Employee count stub (deferred to US-CHR-001)

The `EmployeeCount` field in `JobTitleDto` always returns **0** because the Employee entity does not exist yet. When US-CHR-001 lands:

1. Replace the hardcoded `EmployeeCount = 0` in `JobTitleService.ToDto()` with a real count query: `_dbContext.Employees.CountAsync(e => e.JobTitleId == id && e.IsActive)`.
2. Implement the deactivation block in `JobTitleService.DeactivateAsync` -- search for `TODO(US-CHR-001)`.
3. The AC-5 acceptance criterion ("warns and blocks") will only pass once Employee exists.

### Tenant isolation layers (same pattern as Department)

1. **Global query filter** in `AppDbContext`: `j => !j.IsDeleted && (!_tenantContext.IsResolved || j.TenantId == _tenantContext.TenantId)`
2. **TenantInterceptor**: auto-stamps `TenantId` on new `BaseEntity` rows during `SaveChanges`.
3. **Service-level**: `JobTitleService` checks `_tenantContext.IsResolved` before every operation.

### API endpoints (backend)

- `GET    /api/v1/tenant/job-titles` -- list all for tenant (optional `?activeOnly=true`)
- `GET    /api/v1/tenant/job-titles/{id}` -- single by ID
- `POST   /api/v1/tenant/job-titles` -- create
- `PUT    /api/v1/tenant/job-titles/{id}` -- update
- `POST   /api/v1/tenant/job-titles/{id}/deactivate` -- soft-deactivate
- `GET    /api/v1/tenant/job-titles/employment-types` -- reference list (FR-6)

### Frontend (prior notes)

#### grade_id placeholder (deferred)

The Grade entity does not exist yet. The `gradeId`/`gradeName` fields are declared on `IJobTitle` but the form renders the Grade field as a **disabled placeholder** with dashed border and "Not linked" text. No grade API calls are made.

When the Grade entity lands:
1. Add a searchable dropdown in `JobTitleFormComponent` bound to a grade list endpoint.
2. Add `gradeId` to `ICreateJobTitleRequest` / `IUpdateJobTitleRequest`.
3. Remove the `grade-placeholder` div and the TODO comment.

#### Employee count column (deferred to US-CHR-001)

The Employee entity does not exist yet. The `employeeCount` field is declared on `IJobTitle` but the list table renders it as **"---"** (em-dash) in a badge. The deactivation flow does not client-side-block based on employee count (unlike departments) because the count is not yet reliable; the backend will return `has_active_employees` error code if applicable (AC-5), which is handled via a warning toast.

When US-CHR-001 lands:
1. Render the actual `jt.employeeCount` value instead of the dash.
2. Optionally add client-side blocking in `deactivateJobTitle()` like departments do.
3. The clickable badge navigating to the directory (mentioned in UI/UX notes) is also deferred until US-CHR-003.

### Job Title permissions (US-CHR-005)

Added to `PermissionCatalog`:
- `JobTitle.View` -- granted to Tenant Admin, HR Manager, HR Officer, Manager
- `JobTitle.Create` -- granted to Tenant Admin, HR Manager, HR Officer
- `JobTitle.Edit` -- granted to Tenant Admin, HR Manager, HR Officer
- `JobTitle.Deactivate` -- granted to Tenant Admin, HR Manager, HR Officer

## Employees (US-CHR-001)

### Backend domain rules

- Employee numbers (`employee_no`) auto-generated per tenant with pattern "EMP-NNNN" (FR-2, BR-1). Sequence isolated per tenant via `IgnoreQueryFilters()` + `WHERE tenant_id = ...` to include soft-deleted records and avoid reuse.
- Employee email is unique within a tenant but may duplicate across tenants (FR-3, BR-2). Enforced by partial unique index `WHERE is_deleted = false`.
- Default status on creation is `Active` unless explicitly set to `Probation` (BR-3).
- `date_of_joining` cannot be more than 90 days in the future (BR-4). Validated by FluentValidation.
- Minimum age is 16 years (validated from `date_of_birth`).
- Plan-level employee limit enforced from `Tenant.MaxEmployees` (nullable; null = unlimited). Blocks with 403 when limit reached (AC-5, FR-5).
- Profile photo upload: MIME validation (JPEG/PNG/WebP), max 5 MB, virus scan via `IVirusScanner`, EXIF stripping stub (AC-4, FR-6, NFR-3).
- Custom fields stored as JSONB; no schema validation until US-CHR-012 adds tenant custom-field configuration.
- Soft-delete via `is_deleted = true`; EF global query filter also checks `!IsDeleted`.

### Deferred FKs wired by US-CHR-001

1. **Department.manager_id FK**: Now a real FK to `employees.id` with `ON DELETE SET NULL`. Navigation property `Department.Manager` and `Department.Employees` collection added. DepartmentConfiguration updated.
2. **JobTitle employee count**: `JobTitleService.ToDto()` now queries real employee count instead of returning 0. `GetAllAsync` uses batch query via `GroupBy`.
3. **Deactivation guards**: Both `DepartmentService.DeactivateAsync` and `JobTitleService.DeactivateAsync` now check `_dbContext.Employees.CountAsync(...)` and block if active employees are assigned.

### Tenant.MaxEmployees

Added nullable `int? MaxEmployees` to the Tenant entity. Default is null (unlimited). TODO(subscription): move to a proper Subscription/Plan entity when the billing module is built.

### File storage and virus scanning seams

- `IFileStorage` (Application interface) with `LocalFileStorage` (Infrastructure, dev provider). Path convention: `{tenantId}/core-hr/{employeeId}/profile/{filename}`.
- `IVirusScanner` (Application interface) with `AllowWithLogVirusScanner` (Infrastructure, stub that logs warnings). TODO(prod): wire ClamAV.
- Both registered via DI in `DependencyInjection.AddInfrastructure`.

### API endpoints (backend)

- `GET    /api/v1/tenant/employees` -- paginated list (optional `?activeOnly=true&search=...&page=1&pageSize=20`)
- `GET    /api/v1/tenant/employees/{id}` -- single by ID
- `POST   /api/v1/tenant/employees` -- create
- `POST   /api/v1/tenant/employees/{id}/profile-photo` -- upload profile photo

### Employee permissions (US-CHR-001)

Already existed in `PermissionCatalog`:
- `Employee.View.Own`, `Employee.View.Team`, `Employee.View.All`
- `Employee.Create`, `Employee.Edit`, `Employee.Edit.Own`, `Employee.Delete`, `Employee.Export`

## Employee Profile (US-CHR-002)

### Backend API endpoints (implemented)

- `GET /api/v1/tenant/employees/{id}/profile` -- comprehensive profile with emergency contacts, employment history, and `rowVersion` (xmin concurrency token).
- `PATCH /api/v1/tenant/employees/{id}/profile` -- section-based update. Request body: `UpdateEmployeeProfileRequest` with optional section objects (`personalInfo`, `contactInfo`, `employmentInfo`, `emergencyContacts`, `customFields`). `rowVersion` is required for optimistic concurrency.
- 409 returned on stale `rowVersion` (concurrency conflict). Message: "This record was modified by another user. Please refresh and try again."
- 403 returned when Employee role attempts to update restricted sections (`personalInfo`, `employmentInfo`, `customFields`).
- 403 returned for Manager role (read-only access to all sections).

**Note:** The frontend assumed per-section endpoints (`PATCH .../sections/:section`), but the backend implements a single PATCH endpoint with optional section objects. The frontend should adapt to send all changed sections in a single request. This is simpler, reduces round-trips, and ensures a single concurrency check per save.

### Frontend API contract assumptions

The backend agent is building the matching API in parallel. The frontend assumes these endpoints:

- `GET /api/v1/employees/:id/profile` -- returns `IEmployeeProfile` which extends `IEmployee` with `xmin`, `personalEmail`, address fields, and sub-entity arrays (`emergencyContacts`, `education`, `workHistory`, `dependents`, `employmentHistory`).
- `PATCH /api/v1/employees/:id/sections/:section` -- per-section update. Request body: `{ xmin: string, data: Record<string, unknown> }`. Response: `{ xmin: string, profile: IEmployeeProfile }`. Sections: `personal-info`, `contact`, `emergency-contacts`, `employment`, `education`, `work-history`, `dependents`, `custom-fields`.
- 409 returned on stale `xmin` (concurrency conflict).
- 403 returned when Employee role attempts to PATCH restricted sections (`personal-info`, `employment`, `custom-fields`).

### Field-level permissions (frontend enforcement)

The `isSectionEditable(section, role)` function in `employee.models.ts` governs whether the Edit button renders for a section. This is a UI-only gate; the backend must also enforce restrictions (AC-5).

| Role | Editable sections |
|------|-------------------|
| HR Officer / Tenant Admin | All |
| Employee | contact, emergency-contacts, education, work-history, dependents |
| Manager | None (read-only) |

### Design decisions

- Section navigation uses `MatTabGroup`-style custom tabs on desktop (styled with Tailwind, not Material tabs, to keep the bundle lean) and a native `<select>` dropdown on mobile (<768px). This avoids the Angular Material tab dependency and keeps the component self-contained.
- Employment history timeline is rendered as a custom vertical timeline with CSS pseudo-elements, not a third-party library.
- Skeleton loading uses CSS `@keyframes shimmer` animation on placeholder divs, consistent with the Notion-inspired design language.
- The profile route is `:id` under the employees path (`/employees/:id`), lazy-loaded. The parent `roleGuard` allows HR Officer + Tenant Admin; Employee and Manager self-service routes will need a separate entry point (e.g., `/my-profile`) wired to their own employee ID.

### Backend architecture decisions (US-CHR-002)

#### Concurrency token: RowVersion (uint) mapped to PostgreSQL xmin

The `Employee.RowVersion` property is a `uint` mapped to the PostgreSQL `xmin` system column via EF Core's `IsConcurrencyToken()`. On PostgreSQL, xmin auto-updates on every row write (it holds the inserting/updating transaction ID). EF Core checks the original value on UPDATE and throws `DbUpdateConcurrencyException` if stale.

For unit tests using InMemory provider, xmin is not auto-managed. The tests simulate concurrency by manually setting `RowVersion` to a different value and passing a stale token. This tests the service's catch/handling logic even though the InMemory provider doesn't enforce xmin natively.

#### Separate EmployeeFieldAuditLog table (not reusing AuditLog)

The existing `AuditLog` entity tracks security events (login, MFA, etc.) with a simple string `Detail` field. US-CHR-002 requires JSONB before/after snapshots per section, which is a different shape. A dedicated `employee_field_audit_logs` table avoids polluting the security audit stream and allows JSONB queries on field-level changes. Columns: `before_snapshot jsonb`, `after_snapshot jsonb`, `section`, `employee_id`.

#### Emergency contacts: full replace on update

Emergency contacts are managed as a set: the PATCH endpoint receives the full list and replaces all existing contacts. This is simpler than individual CRUD on contacts and avoids orphan issues. The audit log captures the before/after list.

#### Deferred entities: education, work history, dependents, documents

The user story lists these as profile sections (AC-1), but no entities exist for them yet. Rather than creating stub entities, the backend defers them. `EmployeeProfileDto` includes `EmergencyContacts` and `EmploymentHistory` (which are implemented); education, work history, dependents, and documents sections will be added by future stories. The frontend should render placeholder/empty sections for these.

#### Field-level permission via ICurrentUser.Permissions

Role classification is done by inspecting `ICurrentUser.Permissions` rather than role names, matching the existing RBAC pattern:
- `Employee.Edit` -> HR Officer (full access)
- `Employee.Edit.Own` -> Employee (contact + emergency contacts only)
- `Employee.View.Team` without Edit -> Manager (read-only)

The PATCH controller endpoint uses `[Authorize]` (not `[RequirePermission]`) because the attribute only supports single-permission AND logic. The service enforces OR logic: callers need either `Employee.Edit` or `Employee.Edit.Own`.

## Open questions

- Should deactivating a parent department cascade-deactivate all children, or block? Currently blocks (BR-6). The story text says "requires reassigning or deactivating all child departments first."
- US-CHR-002: The `/employees/:id` route is currently behind `roleGuard(['Tenant Admin', 'HR Officer'])`. Employee self-service and Manager read-only access will need either a separate `/my-profile` route or the guard expanded to allow any authenticated user with backend-side ownership checks.
