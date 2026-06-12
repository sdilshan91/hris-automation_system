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
## Employee Directory (US-CHR-003)

### Backend implementation

New dedicated `GET /api/v1/tenant/employees/directory` endpoint with richer search/filter/sort/export, separate from the existing basic list endpoint at `GET /api/v1/tenant/employees`.

New `GET /api/v1/tenant/employees/directory/export?format=Csv|Excel` endpoint for CSV and Excel exports.

Architecture: `GetEmployeeDirectoryQuery` -> `GetEmployeeDirectoryQueryHandler` -> `IEmployeeDirectoryService` -> `EmployeeDirectoryService`. Separate from the existing `IEmployeeService.GetAllAsync` to avoid breaking the original endpoint contract.

### Search strategy: ILIKE, not tsvector (deliberate deferral)

The story calls for PostgreSQL full-text search (`tsvector`, BR-5). We implemented ILIKE-based (`.ToLower().Contains()`) partial match across `first_name`, `last_name`, `email`, `employee_no`, and `phone`. This translates to `ILIKE '%term%'` on PostgreSQL and performs well up to a few thousand employees per tenant.

**Why not tsvector now:** Adding a tsvector generated column requires a migration that alters the `employees` table with a `GENERATED ALWAYS AS (to_tsvector('english', ...))` expression. Without a live DB to test this migration (and given the risk of partial-word matching semantics differing from ILIKE), we opted for correctness over hypothetical performance.

**Upgrade path to tsvector:**
1. Add a `search_vector tsvector GENERATED ALWAYS AS (to_tsvector('english', coalesce(first_name, '') || ' ' || coalesce(last_name, '') || ' ' || coalesce(email, '') || ' ' || coalesce(employee_no, '') || ' ' || coalesce(phone, ''))) STORED` column.
2. Create a GIN index: `CREATE INDEX ix_employees_search_vector ON employees USING gin(search_vector)`.
3. Change the EF query to use `EF.Functions.ToTsQuery` and `@@` operator.
4. Partial word matching requires `to_tsquery('english', term || ':*')` prefix matching.

### Location field

Added `Employee.Location` as a nullable string (max 200 chars). This is a free-text field -- the story references US-CHR-007 (Location entity) which does not exist yet. When US-CHR-007 lands:
1. Create a `Location` entity with tenant-scoped FK.
2. Replace `Employee.Location` (string) with `Employee.LocationId` (FK).
3. Update directory filters to use location IDs instead of string matching.

### Role-based field visibility (FR-9, BR-2/3/4)

Three visibility tiers based on the caller's permissions:
- **Full** (`Employee.View.All`): All fields including email, phone, dateOfJoining, employmentType.
- **Manager** (`Employee.View.Team`): Same as Full but scoped to reporting chain (see deferral below).
- **Basic** (`Employee.View.Own`): Strips email, phone, dateOfJoining, employmentType from both API responses and exports.

### Manager reporting-chain scope (deferred)

BR-2 requires managers see only employees in their reporting chain. The Employee entity has no `ManagerId` or `ReportsToEmployeeId` field, and the `Department.ManagerId` only links one manager per department without expressing indirect reports.

**What is implemented:** The `DirectoryFieldVisibility.Manager` tier applies field visibility (same as Full) but does NOT filter to reporting chain. All employees in the tenant are visible. This is explicitly flagged as a deferral.

**When a reporting hierarchy is added:**
1. Add `Employee.ReportsToEmployeeId` (nullable FK to self).
2. In `EmployeeDirectoryService.BuildFilteredQuery`, when visibility is `Manager`, add a recursive CTE or ancestor-walk filter to include only direct/indirect reports.
3. Update the query handler to pass the current user's employee ID for the chain lookup.

### Show Archived toggle (BR-1)

Uses `IgnoreQueryFilters()` when `showArchived=true`, which strips the `IsDeleted` filter AND the tenant filter. The query explicitly re-applies `WHERE tenant_id = @tenantId` to maintain tenant isolation. Only honored when the caller has `Employee.View.All` (HR Officer tier); the query handler silently ignores the flag for lower visibility tiers.

### Export (FR-8, BR-4)

- CSV: UTF-8 with BOM, RFC 4180 escaping. Columns match the role-based visibility tier.
- Excel: ClosedXML (.xlsx), single worksheet "Employee Directory", auto-fit columns, bold headers.
- Both formats respect the same filters as the directory query.
- Synchronous implementation. For very large tenants (>10k employees), an async Hangfire export path should be added (NFR-5 deferral).

### Migration: AddEmployeeDirectoryFields

- Adds `employees.location` column (varchar 200, nullable).
- Adds indexes: `ix_employees_tenant_id_employment_type`, `ix_employees_tenant_id_date_of_joining`, `ix_employees_tenant_id_location`.

### Cross-cutting change: Location on CreateEmployee

Adding the `Location` property to `Employee` required updating `CreateEmployeeCommand`, `CreateEmployeeCommandHandler`, `CreateEmployeeRequest`, and the controller mapping. This is a minimal change to keep the create flow consistent with the new field.

### Frontend (US-CHR-003)

#### API contract assumptions (frontend -> backend alignment)

The frontend's `EmployeeService.queryDirectory()` calls `GET /api/v1/tenant/employees` (not `/directory`) with query params:
- `search`, `departments` (csv), `jobTitles` (csv), `statuses` (csv), `employmentTypes` (csv), `location`, `dateOfJoiningFrom`, `dateOfJoiningTo`, `sort`, `sortDirection`, `page`, `pageSize`, `includeArchived`
- Expected response: `{ data: IEmployee[], total: number, page: number, pageSize: number }`
- Export: `GET /api/v1/tenant/employees/export?format=csv|excel&...same filters` returns `Blob`

If the backend uses `/api/v1/tenant/employees/directory` instead, update `EmployeeService.baseUrl` or add a separate `directoryUrl`. The story doc says `GET /api/v1/employees` but the vault notes above mention `/employees/directory`. The backend agent needs to confirm the canonical URL.

#### EmployeeStatus extended to include 'suspended' and 'terminated'

The original `EmployeeStatus` type from US-CHR-001 only had `'active' | 'probation'`. US-CHR-003 adds `'suspended' | 'terminated'` as stated in FR-2 and the multi-select filter options. The card view shows status-specific badge colors for all four statuses.

#### Enhanced existing employee-list component (no new component)

The existing `EmployeeListComponent` was enhanced in place rather than creating a separate `EmployeeDirectoryComponent`. The component now serves both the simple card list role (US-CHR-001) and the full directory (US-CHR-003). The old `getEmployees()` call is replaced by `queryDirectory()` which returns a paginated response.

#### Filter option dropdowns are empty until populated

Department and job title filter dropdowns are declared as `signal<string[]>([])`. In the current implementation these are not auto-populated from a backend endpoint -- they need either:
1. A dedicated `GET /api/v1/tenant/employees/filter-options` endpoint that returns distinct departments/jobTitles/locations, or
2. Reusing the existing `DepartmentService.getDepartments()` and `JobTitleService.getJobTitles()` calls.

When the backend provides this, call the endpoint in `loadFilterOptions()` and populate `departmentOptions` / `jobTitleOptions` signals.

#### URL state sync via Router.navigate with queryParams

Search/filter/sort/page state is persisted as URL query params using `router.navigate([], { queryParams, replaceUrl: true })`. On init, `restoreFromUrl()` reads `ActivatedRoute.snapshot.queryParamMap` and pre-fills signals. This enables deep-linking and browser back/forward (FR-6).

#### View mode responsive default

On screens < 768px, the component defaults to card view. The view mode is not persisted per-user (no localStorage) -- only via URL `?view=table|card` param.

## Organization Tree (US-CHR-006)

### Backend implementation

New `GET /api/v1/tenant/org-tree?view=department|reporting&parentId=&depth=&includeInactive=` endpoint.

Architecture: `GetOrgTreeQuery` -> `GetOrgTreeQueryHandler` -> `IOrganizationTreeService` -> `OrganizationTreeService`. Read-only; no schema change or migration required.

### Department hierarchy view (fully implemented)

- Loads all tenant departments in a single query; builds the tree in-memory.
- Employee counts per department are loaded via a batch `GroupBy` query.
- Lazy loading: `parentId` returns only children of that department; `depth` controls how many levels are expanded (default 2, max 10).
- `includeInactive=true` includes inactive departments and counts inactive employees.
- Department nodes expose the department manager's name via the `Title` field and their avatar via `AvatarUrl`.
- Tenant isolation via EF global query filter on both Department and Employee entities.
- Permission: `Department.View` (same as existing department endpoints).

### Reporting structure view (deferred to US-CHR-011)

`Employee.ReportsTo` FK does not exist. The reporting view is best-effort: it shows department managers as top-level nodes with their department's employees as children. This is explicitly flagged via `ReportingViewAvailable = false` in the response so the frontend can show a disclaimer.

When US-CHR-011 adds `Employee.ReportsToEmployeeId`:
1. Update `OrganizationTreeService.GetReportingTreeAsync` to build the tree from the `ReportsTo` chain instead of `Department.ManagerId`.
2. Support recursive lazy loading by manager ID.
3. Set `ReportingViewAvailable = true`.
4. Employees without a `ReportsTo` assignment appear under their department node (BR-3).

### No migration required

This story is pure read-only over existing Department and Employee data. No schema changes.

### Frontend (US-CHR-006)

#### API contract assumptions (frontend -> backend alignment)

The frontend's `OrgTreeService` calls:
- `GET /api/v1/org-tree?view=department|reporting&parentId=&depth=` -- returns `IOrgTreeNode[]` (flat array)
- `GET /api/v1/org-tree/search?q=&view=` -- returns `IOrgTreeSearchResult[]` with ancestor paths

Note: the user story doc says `GET /api/v1/org-tree` but the backend vault section above says `GET /api/v1/tenant/org-tree`. The frontend uses the environment `apiBaseUrl` prefix (`/api/v1`) + `/org-tree`. If the backend uses `/api/v1/tenant/org-tree` instead, update `OrgTreeService.baseUrl` to include `/tenant`.

#### Search: client-side first, server fallback deferred

The search endpoint (`/org-tree/search`) is defined as an assumption but the component currently performs client-side search across all loaded nodes. If the backend implements the search endpoint, the service method is ready to use. Server-side search becomes important only when the tree is large enough that not all nodes are loaded.

#### PNG export approach (no new dependency)

Export uses SVG foreignObject + canvas drawImage. This works for the component's own rendered HTML but may not capture all CSS perfectly (e.g., Tailwind utility classes not inlined). A more robust approach would use html2canvas or dom-to-image, but both are additional dependencies. The current approach is zero-dependency and handles the common case. If fidelity is insufficient, add `html2canvas` (free/MIT, ~40 kB gzipped).

#### PDF export deferred

The story asks for PNG or PDF (FR-7). PNG is implemented. PDF would require a library like `jspdf` + `html2canvas` (~100 kB combined). Deferred to avoid bundle size impact for a secondary feature.

#### Reporting Structure view: graceful empty state

The reporting view may return an empty array if `Employee.ReportsTo` FK is not modeled yet (US-CHR-011). The component shows a specific empty-state message: "No reporting structure available yet. Manager assignments may not be configured. Try the Department Hierarchy view." No error toast is shown for this case.

#### Zoom/pan implementation: CSS transform, no third-party lib

Pan and zoom are implemented via CSS `transform: scale() translate()` on the tree viewport div. Desktop only (hidden on mobile). Mouse drag for pan, scroll wheel for zoom. This avoids adding d3-zoom or a similar dependency. The zoom range is 30%-200%.

#### Mobile responsive: accordion/vertical list

On screens < 768px (`md:` breakpoint), the tree switches to a collapsible vertical accordion with left border indent lines per level, matching the Notion-inspired design language. No horizontal scrolling or canvas zoom on mobile.

#### Route guard includes Manager role

The `/org-tree` route uses `roleGuard(['Tenant Admin', 'HR Officer', 'Manager'])` because all three personas need to view the org chart per the user story.

## Locations (US-CHR-007)

### Backend API endpoints (implemented)

- `GET    /api/v1/tenant/locations` -- list all for tenant (optional `?activeOnly=true`), includes `employeeCount` per location
- `GET    /api/v1/tenant/locations/{id}` -- single by ID, includes `employeeCount`
- `POST   /api/v1/tenant/locations` -- create
- `PUT    /api/v1/tenant/locations/{id}` -- update
- `POST   /api/v1/tenant/locations/{id}/deactivate` -- soft-deactivate (blocks if active employees assigned)

Endpoint uses `/tenant/locations` matching the vault convention for tenant-scoped entities. Deactivate uses `POST` (not `PATCH`).

### Frontend implementation

#### Static reference data: countries + time zones (no external dependency)

Country list: ~195 ISO 3166-1 entries bundled in `location-data.constants.ts`. No flag icons (to keep bundle small); country stored as the full name string (e.g. "United States"), not the ISO code -- the backend `country` column is `varchar(100)`.

Time zone list: ~70 IANA identifiers in the same file. Common zones (16 entries) are flagged `isCommon: true` and displayed in a "Common" group at the top of the searchable dropdown. The selected value stored in the form and sent to the API is the IANA identifier string (e.g. "America/New_York").

Both lists are pure TypeScript constants with zero runtime cost. If the backend wants to serve these lists via API instead, the form can be adapted by replacing the static arrays with service calls.

#### Employee count badge navigation (AC-2, FR-7)

The employee count badge in each location row is clickable when count > 0 and navigates to `/employees?location={locationName}`. This aligns with US-CHR-003's `filterLocation` signal which reads the `location` query parameter.

Current limitation: the employee directory filters by location name (string match). When US-CHR-007 backend adds a proper `Location` entity with FK, the directory filter should be updated to use `locationId` instead of name. For now, name-based matching works because location names are unique within a tenant (BR-1).

#### Address section collapsible

The address fields (street, line2, city, state, country, postal code) are grouped in a collapsible section in the form. In create mode, the section starts collapsed. In edit mode, it auto-expands if any address field has data. This reduces visual noise for locations that only need a name and time zone.

#### Country and time zone selectors: custom searchable dropdowns

Both selectors use a custom `<input>` + dropdown-list pattern (not Angular Material autocomplete) to stay consistent with the Notion-inspired design and avoid adding `MatAutocompleteModule` to the bundle. The pattern: type to filter, click/mousedown to select, blur closes with 150ms delay (to allow click events to register).

### Employee.LocationId FK (cross-cutting change)

US-CHR-003 added a free-text `Employee.Location` string. US-CHR-007 adds a proper `Employee.LocationId` nullable FK to the `locations` table. Both fields coexist:
- `Employee.Location` (string) -- legacy free-text field from US-CHR-003, kept for backward compatibility.
- `Employee.LocationId` (Guid?, FK) -- structured reference to the `Location` entity. Used for deactivation guard (AC-3/FR-5) and employee count per location (FR-7).

The navigation property is named `Employee.LocationEntity` (not `Employee.Location`) to avoid collision with the legacy string property. The FK uses `ON DELETE SET NULL`.

**Migration note:** When employee create/update flows are extended to use the Location entity, they should set `LocationId` (and optionally sync the free-text `Location` string for display). The directory filter (US-CHR-003) currently uses the free-text `Location` field; it should be updated to use `LocationId` for structured filtering.

### Deactivation guard (AC-3, FR-5)

The deactivation guard checks `Employees.Count(e => e.LocationId == locationId && e.IsActive)`. This uses the FK, not the free-text string. Employees must be assigned via `LocationId` for the guard to work correctly. Until employee creation is updated to set `LocationId`, new employees assigned via the free-text field only will not block deactivation.

### Tenant isolation layers (same pattern as Department/JobTitle)

1. **Global query filter** in `AppDbContext`: `l => !l.IsDeleted && (!_tenantContext.IsResolved || l.TenantId == _tenantContext.TenantId)`
2. **TenantInterceptor**: auto-stamps `TenantId` on new `BaseEntity` rows during `SaveChanges`.
3. **Service-level**: `LocationService` checks `_tenantContext.IsResolved` before every operation.

### Location permissions (implemented)

Added to `PermissionCatalog` following the same pattern as Departments/Job Titles:
- `Location.View` -- granted to Tenant Admin, HR Manager, HR Officer, Manager
- `Location.Create` -- granted to Tenant Admin, HR Manager, HR Officer
- `Location.Edit` -- granted to Tenant Admin, HR Manager, HR Officer
- `Location.Deactivate` -- granted to Tenant Admin, HR Manager, HR Officer

The route guard uses `roleGuard(['Tenant Admin', 'HR Officer'])` matching the story's persona.

## Employee Status Management (US-CHR-009)

### Frontend implementation

#### Status badge color mapping (FR-7)

Extended `EmployeeStatus` type to include `'inactive'` (was missing from the original US-CHR-001/003 type). All 5 statuses now have color-coded badges matching the story spec:

| Status | Badge CSS class (profile) | Badge CSS class (list) |
|--------|---------------------------|------------------------|
| active | `badge-active` (green-50/green-700) | `status-active` (green-50/green-700) |
| probation | `badge-probation` (amber-50/amber-700) | `status-probation` (amber-50/amber-700) |
| suspended | `badge-suspended` (gray-100/gray-800) | `status-suspended` (gray-100/gray-800) |
| terminated | `badge-terminated` (red-50/red-700) | `status-terminated` (red-100/red-800) |
| inactive | `badge-inactive` (slate-100/slate-800) | `status-inactive` (slate-100/slate-800) |

The `getStatusBadgeClasses()` pure function in `employee.models.ts` uses the exact Tailwind classes from Section 7 (`bg-green-100 text-green-800` etc.) and is separately testable without TestBed.

#### Cross-cutting change: employee-list status badge colors

The employee-list component (US-CHR-003) had `suspended` styled as red and `terminated` as neutral -- these were swapped vs. the story spec. US-CHR-009 corrected both to match the story's color mapping (suspended=gray, terminated=red) and added the `inactive` badge class. This is a **shared-file change** that affects the US-CHR-003 directory view.

#### Status transition source of truth (BR-1, AC-1)

Valid transitions are fetched from the backend via `GET /api/v1/tenant/employees/:id/status/transitions`. The frontend does NOT hardcode the transition matrix. The dropdown in the status change modal only shows transitions returned by the backend.

#### Backend API contract assumptions (US-CHR-009)

- `GET /api/v1/tenant/employees/:id/status/transitions` -- returns `IStatusTransition[]` (targetStatus, label, sideEffects[])
- `POST /api/v1/tenant/employees/:id/status` -- body: `{ newStatus, effectiveDate, reason }`, header: `Idempotency-Key: <uuid>`. Returns `{ profile: IEmployeeProfile }` on success, 400 with `{ message }` on invalid transition.

The `IChangeStatusResponse` returns the full updated `IEmployeeProfile` so the client can update the badge and timeline in one round-trip without a separate GET.

#### Idempotency (NFR-3)

Each status change submission generates a new UUID via `crypto.randomUUID()` and sends it as the `Idempotency-Key` header. This prevents duplicate transitions if the user retries on a network timeout.

#### Employment history timeline reason display

Added optional `reason` field to `IEmploymentHistoryEntry`. Status change entries (`changeType === 'status_change' || 'status'`) render with inline status badges (previous -> new) and the reason in italics.

#### Modal responsive behavior (NFR-4)

On mobile (<640px), the modal overlay aligns to bottom and the modal card uses `rounded-b-none rounded-t-2xl` (bottom-sheet pattern) with full width. On desktop, it centers with `max-w-md`.

### Backend implementation (US-CHR-009)

#### State machine (FR-2, BR-1)

Hardcoded in `EmployeeStatusStateMachine` (Domain layer). The state machine is a pure static class with no dependencies, making it trivially testable and reusable from any layer. Transitions:

| From | Valid targets |
|------|--------------|
| Probation | Active, Terminated |
| Active | Suspended, Terminated, Inactive |
| Suspended | Active, Terminated |
| Inactive | Active, Terminated |
| Terminated | (terminal, no outbound) |

Invalid transitions return a structured error message: "Invalid status transition. {From} employees cannot be moved to {To}."

#### EmployeeStatus enum extended

Added `Suspended = 4` to the `EmployeeStatus` enum. The enum is stored as a string in PostgreSQL (via `HasConversion<string>()`), so adding a new value requires no schema migration for the `employees.status` column.

#### Idempotency approach (NFR-3)

Database-backed idempotency via the `idempotency_records` table (new). Scoped per tenant + operation name + key. Records expire after 24 hours. The approach is pragmatic:
- On first call: execute the operation, save the response JSON + status code.
- On duplicate call (same key): return the cached response without re-executing.
- Race condition on concurrent duplicate inserts is handled via unique constraint catch-and-ignore.
- No in-memory caching layer (simplicity first; the DB unique index provides correctness).

#### Future-dated changes (BR-4)

Stored in the `future_dated_status_changes` table. When effectiveDate > today, the change is persisted but NOT applied. A daily Hangfire job (`ApplyFutureDatedStatusChangesJob`, runs at 00:15 UTC) queries all pending records with `effectiveDate <= today` and applies them. If the transition is no longer valid at application time (e.g., the employee's status changed since scheduling), the record is marked `is_cancelled = true` with a log warning.

#### Side effects (FR-5, AC-3) -- CROSS-CUTTING AUTH CHANGE

On `terminated` or `suspended`: the linked User account's `IsActive` is set to `false` and all active RefreshTokens are revoked. This is a **cross-cutting change** that touches the User entity (auth module). The change is minimal (two fields: `User.IsActive = false` + `RefreshToken.RevokedAt = now`).

On `active` (reactivation from suspended/inactive): the linked User account's `IsActive` is re-enabled.

Leave accrual pause/resume and payroll exclusion are left as TODO hooks for their respective modules.

#### Probation reminder (FR-6, AC-4, BR-6)

Daily Hangfire job (`ProbationReminderJob`, runs at 08:00 UTC) checks employees in `Probation` status whose `date_of_joining + 90 days` falls within the next 7 days. Currently logs a structured `PROBATION_REMINDER` warning. Actual notification dispatch is deferred to the Notification module (TODO).

#### Permission: `Employee.ChangeStatus`

New permission in `PermissionCatalog`. Granted by default to Tenant Admin, HR Manager, and HR Officer roles. The `POST .../status` endpoint is gated with `[RequirePermission("Employee.ChangeStatus")]`.

#### API endpoints (backend)

- `POST /api/v1/tenant/employees/{id}/status` -- change status (with `Idempotency-Key` header)
- `GET /api/v1/tenant/employees/{id}/status/transitions` -- valid transitions query

#### Migration: `AddEmployeeStatusManagement` (20260612154506)

Creates two new tables:
1. `future_dated_status_changes` -- stores pending future-dated status changes
2. `idempotency_records` -- stores idempotency keys with response cache

No change to the `employees` table (the `Suspended` enum value is stored as a string).

## Bulk Employee Import (US-CHR-010)

### Frontend implementation

#### Route: `/employees/import`

Lazy-loaded under the existing employees route group. Placed before the `:id` wildcard route to avoid path collision. Role-guarded by the parent employees route (`Tenant Admin`, `HR Officer`).

#### Backend API contract assumptions (frontend -> backend alignment)

The frontend assumes these endpoints (backend agent building in parallel):
- `GET  /api/v1/employees/import/template?format=csv|xlsx` -- returns Blob (template download)
- `POST /api/v1/employees/import` -- multipart file upload. Body: `FormData` with `file` + optional `importUpToLimit=true`. Returns either:
  - `IImportResult { total, success, failed, errors[] }` for sync imports (<= 500 rows)
  - `IImportJobRef { jobId, status }` for async imports (> 500 rows)
  - HTTP 409 with `IPlanLimitWarning { code: 'plan_limit_exceeded', message, maxAllowed, currentCount, fileRecordCount, importableCount }` when plan limit would be exceeded
- `GET  /api/v1/employees/import/jobs/:jobId` -- returns `IImportJobStatus { jobId, status, progress (0-100), result (nullable) }` for polling
- `GET  /api/v1/employees/import/jobs/:jobId/error-report` -- returns Blob (CSV error report)

All endpoints are tenant-scoped via the tenantInterceptor `X-Tenant-Subdomain` header.

#### Async import polling approach (AC-4)

The component polls `GET /import/jobs/:jobId` every 3 seconds using `setInterval`. When `status === 'completed'` and `result` is present, polling stops and the results are displayed. When `status === 'failed'`, polling stops and an error toast is shown. Polling failures are silently ignored (non-fatal; next tick retries).

This is a pragmatic choice over WebSocket/SSE: simpler to implement, no additional infrastructure, and the 3-second interval is acceptable for a background job that runs for minutes. If the user navigates away, `ngOnDestroy` cleans up the interval.

#### Client-side error report CSV generation

When the import result is from a sync response (no `jobId`), the error report CSV is generated client-side from the `errors[]` array rather than calling the backend. This avoids an extra round-trip for data the frontend already has. When a `jobId` exists (async import), the backend endpoint is used instead.

#### Upload progress via HttpRequest + reportProgress

The `uploadImport` method uses `new HttpRequest('POST', ...)` with `reportProgress: true` instead of `http.post()`. This enables the component to show a percentage progress bar during file upload. The progress bar is distinct from the async job progress bar (which shows processing progress, not upload progress).

#### File validation is extension-based, not MIME-based

Client-side validation checks `.csv` and `.xlsx` extensions rather than MIME types. MIME type checking is unreliable across browsers (some report `.csv` as `text/plain`, others as `text/csv`, and drag-and-drop often loses MIME metadata). The backend should validate the actual file content.

### Backend implementation (US-CHR-010)

#### API endpoints

- `GET  /api/v1/tenant/employees/import/template?format=Csv|Excel` -- download template (FR-2, AC-1)
- `POST /api/v1/tenant/employees/import?importUpToLimit=false` -- upload and process (AC-2/AC-3/AC-4)
- `GET  /api/v1/tenant/employees/import/{jobId}/status` -- async job status (AC-4)
- `GET  /api/v1/tenant/employees/import/{jobId}/errors` -- error report CSV download (FR-8)

All gated by `Employee.Import` permission (new in PermissionCatalog).

#### Sync/async threshold (FR-7)

Files with <= 500 data rows are processed synchronously in-request. Files with > 500 rows are saved to temp disk, a `BulkImportJob` record is created (status: Pending), and a Hangfire background job is enqueued. The Hangfire job restores the tenant context before calling `ProcessImportJobAsync`. Threshold constant: `BulkEmployeeImportService.AsyncThreshold = 500`.

#### Batch commits (NFR-4)

Both sync and async paths process rows in batches of 100. Each batch is a separate `SaveChangesAsync` call. On batch failure, the failed entities are detached and errors are recorded per-row. Subsequent batches continue normally. This avoids all-or-nothing rollback for the entire file.

#### Plan-limit enforcement (FR-9, AC-5)

Pre-validated before processing. If the import would exceed `Tenant.MaxEmployees`:
- Without `importUpToLimit=true`: returns HTTP 409 with a message indicating available slots.
- With `importUpToLimit=true`: trims the row list to the available slot count and imports only those.

For async jobs, the plan limit is re-checked at processing time (it may have changed since queuing).

#### Employee number generation

Reuses the same pattern as `EmployeeService.GenerateEmployeeNoAsync`: queries the max `employee_no` across all employees (including soft-deleted, via `IgnoreQueryFilters`) for the tenant, parses the sequence number, and increments. For batch imports, the sequence is allocated once per batch to avoid per-row queries.

#### Per-row validation

Reference data (departments, job titles, locations) is loaded once per import in batch queries, then looked up by name from in-memory dictionaries. Email uniqueness is checked against both existing tenant employees and within the file itself (BR-2). Invalid rows are collected as `BulkImportRowError` objects with `rowNumber`, `field`, and `error`. The `Failed` count in the result is the number of unique failed rows (a row with multiple field errors counts as 1 failed row).

#### File parsing

- CSV: CsvHelper (new NuGet dependency, MIT license, `CsvHelper` v33). Supports RFC 4180 with trimming. Comment lines (starting with `#`) are skipped.
- Excel: ClosedXML (already in project). Searches first 5 rows for the header row containing `first_name`. Supports date cells stored as both strings and Excel date values.

#### Custom fields (FR-11) -- DEFERRED

The `custom_field_*` columns from the data requirements table are not implemented. They depend on US-CHR-012 (tenant custom-field configuration), which does not exist yet. The template and parser ignore custom field columns.

#### Completion notification -- DEFERRED

The Notification module is not built. Async job completion is logged but no in-app or email notification is dispatched. Marked with `TODO(notification)` in the service.

#### Migration: `AddBulkImportJobs` (20260612161924)

Creates the `bulk_import_jobs` table with:
- `error_details` column as `jsonb` (stores serialized `BulkImportRowError[]`)
- Composite index on `(tenant_id, status)` for efficient job queries
- Standard `BaseEntity` audit fields (`tenant_id`, `created_at`, `is_deleted`, etc.)

#### Audit (FR-10)

Every import (sync or async) creates a `BulkImportJob` record with file name, row count, success count, failure count, and the initiating user's email. This doubles as both the audit trail and the job status record.

#### Idempotency (NFR-3)

Import idempotency is achieved via email uniqueness within the tenant. Re-uploading the same file will not create duplicate employees because the email uniqueness check in per-row validation catches already-existing emails. No separate `Idempotency-Key` header mechanism is needed for the import endpoint.

#### InternalsVisibleTo

Added `<InternalsVisibleTo Include="HRM.Tests" />` to `HRM.Infrastructure.csproj` so unit tests can directly call `internal` methods (`ParseCsvRows`, `ParseExcelRows`, `ValidateRowsAsync`). This is a cross-cutting change.

### Design decisions

- The component uses inline template (no separate `.html` file) consistent with other core-hr components.
- Pure functions (`validateImportFile`, `generateErrorReportCsv`, type guards) are exported from the component file and tested in a separate top-level `describe` block without TestBed, avoiding `httpMock.verify()` conflicts.
- `triggerBlobDownload` is a non-private method on the component specifically for testability -- tests spy on it to prevent `anchor.click()` which would reload Karma and disconnect the browser.

## Open questions

- Should deactivating a parent department cascade-deactivate all children, or block? Currently blocks (BR-6). The story text says "requires reassigning or deactivating all child departments first."
- US-CHR-002: The `/employees/:id` route is currently behind `roleGuard(['Tenant Admin', 'HR Officer'])`. Employee self-service and Manager read-only access will need either a separate `/my-profile` route or the guard expanded to allow any authenticated user with backend-side ownership checks.
