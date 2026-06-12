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

## Open questions

- Should deactivating a parent department cascade-deactivate all children, or block? Currently blocks (BR-6). The story text says "requires reassigning or deactivating all child departments first."
