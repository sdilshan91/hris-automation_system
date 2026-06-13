---
type: module-note
module: leave-management
status: active
created: 2026-06-13
---

# Leave Management

Leave type configuration, leave requests, accruals, and leave balance management.

## Domain rules (US-LV-001)

- Leave type names are unique within a tenant (case-insensitive) (BR-1). Enforced by service-level `ToLowerInvariant()` comparison and a partial unique index on `(tenant_id, name) WHERE is_deleted = false`.
- A leave type cannot be hard-deleted if any leave requests reference it; it can only be deactivated (BR-2). No leave request entity exists yet, so this is a soft-delete via `IsActive = false`.
- Entitlement values must be non-negative; zero entitlement is allowed for unpaid leave types (BR-3).
- Gender-specific leave types (e.g., Maternity) only appear for employees matching the configured gender (BR-4). Enforcement is deferred to the leave application flow (US-LV-002+).
- Configuration changes do not retroactively affect already-approved leave requests (BR-5). Enforcement is deferred to the leave request module.

## Entity: LeaveType

All fields from US-LV-001 FR-2 / Data Requirements:

| Property | Type | DB Column | Notes |
|---|---|---|---|
| Id | Guid (UUIDv7) | id | PK |
| TenantId | Guid | tenant_id | FK to tenants |
| Name | string(100) | name | Required, unique per tenant (case-insensitive) |
| Code | string(20)? | code | Optional short code (e.g. "AL") |
| Color | string(7)? | color | Hex color (e.g. "#4CAF50") |
| Description | string(500)? | description | Free-text |
| AnnualEntitlement | decimal | annual_entitlement | numeric(5,2), >= 0 |
| AccrualFrequency | AccrualFrequency (enum) | accrual_frequency | Stored as string: Monthly/Quarterly/Yearly/Upfront |
| CarryForwardLimit | decimal? | carry_forward_limit | numeric(5,2), null = no carry-forward |
| CarryForwardExpiryMonths | int? | carry_forward_expiry_months | null = never expires |
| ProbationEligible | bool | probation_eligible | default false |
| DocumentsRequired | bool | documents_required | default false |
| DocumentDayThreshold | int? | document_day_threshold | Days after which docs are required |
| Encashable | bool | encashable | default false |
| MaxEncashDays | decimal? | max_encash_days | numeric(5,2) |
| HalfDayAllowed | bool | half_day_allowed | default false |
| HourlyAllowed | bool | hourly_allowed | default false |
| Gender | LeaveTypeGender (enum) | gender | All/Male/Female, stored as string |
| MaxConsecutiveDays | int? | max_consecutive_days | null = no limit |
| NegativeBalanceAllowed | bool | negative_balance_allowed | default false |
| NegativeBalanceLimit | decimal? | negative_balance_limit | numeric(5,2) |
| DisplayOrder | int | display_order | For UI ordering |
| IsActive | bool | is_active | default true |
| + BaseEntity fields | | | TenantId, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted |

## API endpoints

- `GET    /api/v1/tenant/leave-types` -- list all (optional `?activeOnly=true`), ordered by display_order
- `GET    /api/v1/tenant/leave-types/{id}` -- single by ID
- `POST   /api/v1/tenant/leave-types` -- create
- `PUT    /api/v1/tenant/leave-types/{id}` -- update
- `POST   /api/v1/tenant/leave-types/{id}/deactivate` -- soft-deactivate
- `POST   /api/v1/tenant/leave-types/{id}/reactivate` -- reactivate
- `POST   /api/v1/tenant/leave-types/reorder` -- reorder via ordered list of IDs

## Permissions (US-LV-001)

Added to `PermissionCatalog`:
- `LeaveType.View` -- granted to Tenant Admin, HR Manager, HR Officer
- `LeaveType.Create` -- granted to Tenant Admin, HR Manager, HR Officer
- `LeaveType.Edit` -- granted to Tenant Admin, HR Manager, HR Officer
- `LeaveType.Deactivate` -- granted to Tenant Admin, HR Manager, HR Officer

Note: The existing `Leave.ConfigurePolicy` permission exists in the catalog but is a broader leave-module permission. `LeaveType.*` permissions are specific to leave type CRUD configuration, following the same pattern as `Department.*`, `JobTitle.*`, `Location.*`, `CustomField.*`.

## Tenant isolation layers (same pattern as Department/JobTitle/Location)

1. **Global query filter** in `AppDbContext`: `lt => !lt.IsDeleted && (!_tenantContext.IsResolved || lt.TenantId == _tenantContext.TenantId)`
2. **TenantInterceptor**: auto-stamps `TenantId` on new `BaseEntity` rows during `SaveChanges`.
3. **Service-level**: `LeaveTypeService` checks `_tenantContext.IsResolved` before every operation.

No separate PostgreSQL RLS policy was created. The codebase enforces tenant isolation via EF global query filters + `TenantInterceptor` for all entities (Department, JobTitle, Location, Employee, CustomFieldDefinition, etc.). The story's literal "RLS policy" requirement (NFR-2) is met by the same approach. If Postgres-level RLS is ever needed as a defense-in-depth layer, it should be added for ALL entities uniformly, not just LeaveType.

## Caching decision (NFR-1) -- DEFERRED

The story requires Redis caching of the leave-type list with cache invalidation on write (NFR-1, P95 < 200ms).

**Decision:** Caching is DEFERRED. The codebase has `IDistributedCache` registered (via `AddStackExchangeRedisCache` when a Redis connection string is configured, or `AddDistributedMemoryCache` as fallback), but no existing entity (Department, JobTitle, Location, CustomField) uses a cache layer. Implementing a tenant-scoped cache wrapper for leave types alone would be inconsistent and larger than this story warrants.

**Rationale:** The list query is a simple `SELECT ... WHERE tenant_id = ... ORDER BY display_order` with an index on `(tenant_id, is_active, display_order)`. For typical tenant sizes (< 100 leave types), this will comfortably meet the 200ms P95 target without caching. When a tenant-scoped caching pattern is established for the first entity (likely as a cross-cutting concern), it should be applied to leave types, departments, job titles, etc. uniformly.

**TODO:** Add `IDistributedCache`-based caching to `LeaveTypeService.GetAllAsync` with key `hrm:{tenantId}:leave-types` and invalidation in Create/Update/Deactivate/Reactivate/Reorder. Apply the same pattern to other config entities.

## Default seeding (FR-4) -- PARTIAL

The `LeaveTypeService.SeedDefaultsForTenantAsync(tenantId)` method seeds 7 default leave types:
1. Annual Leave (14 days, monthly accrual, carry-forward 5 days)
2. Sick Leave (7 days, upfront, medical cert required > 2 days)
3. Casual Leave (7 days, upfront, max 3 consecutive days)
4. Maternity Leave (84 days, female only)
5. Paternity Leave (5 days, male only)
6. Bereavement Leave (3 days)
7. Unpaid Leave (0 days, negative balance allowed up to 30 days)

The method is idempotent: it skips if any leave types already exist for the tenant.

**Seeding hook:** The onboarding wizard / tenant provisioning flow is not wired to call `SeedDefaultsForTenantAsync` yet. The `DbInitializer.SeedAsync` seeds the default admin tenant only and does not provision leave types. When the onboarding module (US-TENANT-*) is built, it should call `ILeaveTypeService.SeedDefaultsForTenantAsync(tenantId)` in Step 4 (leave types & holidays).

**TODO(onboarding):** Wire `SeedDefaultsForTenantAsync` into tenant provisioning / onboarding wizard Step 4.

## Audit trail (NFR-3)

Audit is handled by the existing `AuditInterceptor` which stamps `CreatedBy`/`UpdatedBy` and `CreatedAt`/`UpdatedAt` on all `BaseEntity` writes. Structured logging with `LeaveTypeId`, `TenantId`, and `User` is emitted for every create/update/deactivate/reactivate/reorder operation.

Before/after JSON snapshot auditing (as done for employee profile changes via `EmployeeFieldAuditLog`) is NOT implemented for leave type changes. The story says "before/after captured" (AC-2) but this level of audit is only implemented for the employee profile module (US-CHR-002) and not for other config entities (departments, job titles, locations, custom fields). Adding it for leave types only would be inconsistent.

**TODO:** When a general-purpose change-audit table is created (beyond the employee-specific `EmployeeFieldAuditLog`), apply it uniformly to all config entities including leave types.

## Migration: `AddLeaveTypeEntity` (20260613035822)

Creates:
1. `leave_types` table with all columns per the data requirements
2. Partial unique index `ix_leave_types_tenant_id_name` on `(tenant_id, name) WHERE is_deleted = false`
3. Performance index `ix_leave_types_tenant_id_is_active_display_order` on `(tenant_id, is_active, display_order)`

## Related stories

- `US-LV-001` -- Configure Leave Types Per Tenant (this story)
- `US-LV-002` -- Set Yearly Leave Entitlements by Job Level/Department
- `US-LV-003+` -- Leave Balances, Accruals, Carry-Forward processing

## Leave Entitlements (US-LV-002)

### Entities

1. **LeaveEntitlementRule** -- rule-based entitlement mapping. Dimensions: leave_type_id (FK), department_id (nullable FK), job_title_id (nullable FK), job_level_id (nullable UUID, no FK -- see below), employment_type (nullable enum), tenure_min_months / tenure_max_months, entitlement_days, priority, effective_from / effective_to, is_active.
2. **LeaveEntitlementOverride** -- per-employee override. Unique per (tenant, employee, leave_type, leave_year). Takes precedence over all rules.
3. **LeaveLedger** -- immutable transaction log: entry_type enum (Accrual/Used/Adjusted/Encashed/CarryForward/Expired), employee_id, leave_type_id, leave_year, amount, balance_after, description, occurred_at.

### Rule priority/specificity engine (FR-2, AC-2)

Resolution order (highest precedence first):

1. Employee override (AC-3)
2. Rule: department + job_title + employment_type (specificity score 7)
3. Rule: department + job_title (specificity score 6)
4. Rule: department only (specificity score 4)
5. Rule: job_title only (specificity score 2)
6. Leave type default annual_entitlement

Within the same specificity tier, the rule with the **lowest Priority value** wins.
Tenure brackets are applied as an additional filter, not a specificity dimension.

Implementation: `LeaveEntitlementEngine.Resolve()` -- pure static C#, no I/O, fully unit-testable.

### job_level_id -- DEFERRED (no JobLevel entity)

The story lists `job_level_id` as a dimension (FR-1). There is **no JobLevel entity** in this codebase. The column is included as a **nullable UUID WITHOUT a hard FK** (same pattern as JobTitle.GradeId). It is stored in the database and round-tripped via DTOs, but ignored by the specificity engine until a JobLevel entity exists.

TODO(job-level): When a JobLevel entity is created, wire FK and add specificity score (3, between dept and job_title) to `LeaveEntitlementEngine.Resolve()`.

### FTE pro-rata -- DEFERRED (no FTE field)

BR-2 requires part-time employees receive entitlement proportional to FTE. The Employee entity has no FTE field. `LeaveEntitlementEngine.CalculateProRata()` accepts an `fte` parameter but always receives 1.0m from the service.

TODO(part-time): Add `Employee.Fte` (decimal, default 1.0) and pass it to `CalculateProRata`.

### Redis balance cache -- DEFERRED (consistent with US-LV-001)

FR-6 requires Redis caching with key pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`. Deferred, consistent with the US-LV-001 caching decision. No cache layer built; balance is read from LeaveLedger running total.

TODO: When a tenant-scoped cache pattern is established, add `IDistributedCache`-based caching for leave balances.

### API endpoints (backend)

Base: `/api/v1/tenant/leave-entitlements`

| Method | Path | Description | Permission |
|--------|------|-------------|------------|
| GET | `/rules` | List rules (optional `?leaveTypeId`) | Leave.ConfigurePolicy |
| GET | `/rules/{id}` | Get rule by ID | Leave.ConfigurePolicy |
| POST | `/rules` | Create rule | Leave.ConfigurePolicy |
| PUT | `/rules/{id}` | Update rule | Leave.ConfigurePolicy |
| DELETE | `/rules/{id}` | Soft-delete rule | Leave.ConfigurePolicy |
| POST | `/rules/bulk` | Bulk create rules | Leave.ConfigurePolicy |
| GET | `/overrides` | List overrides (optional `?employeeId&leaveTypeId&leaveYear`) | Leave.ConfigurePolicy |
| POST | `/overrides` | Upsert override | Leave.ConfigurePolicy |
| DELETE | `/overrides/{id}` | Soft-delete override | Leave.ConfigurePolicy |
| GET | `/effective` | Compute effective entitlement (`?employeeId&leaveTypeId&leaveYear`) | Leave.ConfigurePolicy |

### Hangfire job: LeaveAccrualJob

- Registered as `leave-entitlement-accruals` (daily at 00:30 UTC).
- Iterates all active/trial tenants, sets tenant context, calls `ILeaveEntitlementService.ProcessAccrualsAsync(leaveYear)`.
- For each employee x leave type: resolves entitlement (override > rule > default), pro-rates for mid-year joiners, writes a LeaveLedger accrual entry.
- Idempotent: skips if an accrual entry already exists for the employee/leave type/year.
- Batch size: 500 employees per query page. Handles 5,000+ employees across batches.
- BR-3: Skips probation-ineligible leave types for probation employees.

### Migration: `AddLeaveEntitlements` (20260613123300)

Creates three tables:
1. `leave_entitlement_rules` -- with FKs to leave_types (RESTRICT), departments (SET NULL), job_titles (SET NULL). Indexes: (tenant_id, leave_type_id), (tenant_id, is_active, priority).
2. `leave_entitlement_overrides` -- with FKs to employees (CASCADE), leave_types (RESTRICT). Unique index: (tenant_id, employee_id, leave_type_id, leave_year) WHERE is_deleted = false. 
3. `leave_ledger` -- with FKs to employees (CASCADE), leave_types (RESTRICT). Indexes: (tenant_id, employee_id, leave_type_id, leave_year), (tenant_id, occurred_at).

### Audit trail

Rule and override changes are audit-logged via the existing `AuditInterceptor` (CreatedAt/CreatedBy/UpdatedAt/UpdatedBy on BaseEntity). Structured logging with RuleId, TenantId emitted for every create/update/delete operation.

## Edge cases

- Creating a leave type with a name that differs only in case (e.g., "Annual Leave" vs "annual leave") is rejected.
- Deactivating an already-inactive leave type returns a 400 error, not a silent no-op.
- Reactivating an already-active leave type returns a 400 error.
- Display order auto-increments from the current max when not specified.
- Name is trimmed on create and update to prevent leading/trailing whitespace issues.

## Open questions

- Should the unique index use `LOWER(name)` for true case-insensitive uniqueness at the database level? Currently the service enforces case-insensitive comparison in C# code and the database index uses the raw name column. On PostgreSQL with default collation, two names that differ only in case would violate the unique index anyway. The C# guard catches it first with a cleaner error message. If a non-default case-sensitive collation is used, the index should be changed to use `LOWER(name)`.

## Frontend (US-LV-001)

### API contract alignment with backend

The frontend calls these endpoints, aligned with the backend vault section above:
- `GET    /api/v1/tenant/leave-types` -- expects `ILeaveType[]` (camelCase JSON)
- `GET    /api/v1/tenant/leave-types/:id` -- expects `ILeaveType`
- `POST   /api/v1/tenant/leave-types` -- sends `ICreateLeaveTypeRequest`, expects `ILeaveType`
- `PUT    /api/v1/tenant/leave-types/:id` -- sends `IUpdateLeaveTypeRequest`, expects `ILeaveType`
- `POST   /api/v1/tenant/leave-types/:id/deactivate` -- expects `ILeaveType` with `isActive: false`
- `POST   /api/v1/tenant/leave-types/:id/reactivate` -- expects `ILeaveType` with `isActive: true`
- `POST   /api/v1/tenant/leave-types/reorder` -- sends `{ orderedIds: string[] }`, expects void

**Contract difference noted:** Backend uses `/reactivate`, frontend aligned to match. The backend `code` field is optional (`string(20)?`) but the frontend requires it because it is used as the tag label in the color swatch. If the backend sends a null code, the tag will render empty.

### Design decisions

#### Drag-and-drop reorder: CDK on desktop, arrow buttons on mobile

Uses Angular CDK `DragDropModule` for drag-and-drop on desktop (md: breakpoint and above). On mobile, up/down arrow buttons are used. CDK drag-drop is already in `package.json` (`@angular/cdk`). Drag is disabled when search is active to avoid reordering a filtered subset.

#### Slide-over form with grouped sections

Five sections: Basic Info, Entitlement Rules, Carry-Forward, Document Rules, Advanced. The Advanced section uses an accordion pattern (collapsed by default in create mode, auto-expanded in edit mode when any advanced field differs from defaults).

#### Color picker: palette + custom input

12 preset colors + native `<input type="color">` for custom selection. Contrast text color auto-calculated via ITU-R BT.709 luminance formula (`getContrastTextColor` pure function, separately tested).

#### Active/inactive inline toggle (no confirmation dialog)

Unlike Locations (which uses a confirmation dialog), leave types use an inline toggle switch matching the Custom Fields pattern. The backend error response is surfaced via toast if deactivation fails.

#### Conditional field nullification on submit

Toggle-gated child fields are nullified when the parent toggle is off:
- `documentsRequired: false` -> `documentDayThreshold: null`
- `encashable: false` -> `maxEncashDays: null`
- `negativeBalanceAllowed: false` -> `negativeBalanceLimit: null`

### Route

`/leave-types` under `MainLayoutComponent`, lazy-loaded. Role guard: `Tenant Admin`, `HR Officer`.

### Shared-file change

Only `app.routes.ts` was modified (added the `leave-types` route entry).

## Frontend (US-LV-002) — Leave Entitlement Configuration

### "Job level" dimension omitted

The user story FR-1 lists "job level" as a dimension. There is no `job_level` backend entity in the codebase — departments, job titles, locations, and custom fields exist, but not job levels. The entitlement rule form omits the job level dropdown entirely. If a `job_level` entity is added later, a `<select>` for it should be added to `EntitlementRuleFormComponent` and the service filter.

### API contract assumptions (backend building in parallel)

Base path: `/api/v1/tenant/leave-entitlements`

| Method | Path | Body / Params | Response |
|--------|------|---------------|----------|
| GET | `/rules` | `?leaveTypeId&departmentId&employmentType&activeOnly` | `IEntitlementRule[]` |
| GET | `/rules/:id` | — | `IEntitlementRule` |
| POST | `/rules` | `ICreateEntitlementRuleRequest` | `IEntitlementRule` |
| PUT | `/rules/:id` | `IUpdateEntitlementRuleRequest` | `IEntitlementRule` |
| PATCH | `/rules/:id/days` | `{ entitlementDays: number }` | `IEntitlementRule` |
| DELETE | `/rules/:id` | — | `void` |
| GET | `/overrides` | `?employeeId&leaveYear` | `IEntitlementOverride[]` |
| POST | `/overrides` | `{ employeeId, leaveTypeId, leaveYear, entitlementDays, reason? }` | `IEntitlementOverride` (upsert) |
| DELETE | `/overrides/:id` | — | `void` |
| GET | `/compute-effective` | `?employeeId` | `IEffectiveEntitlement[]` |
| POST | `/bulk` | `IBulkEntitlementRequest` | `IBulkEntitlementResponse` |

The response for rules includes denormalized `leaveTypeName`, `departmentName`, `jobTitleName` — the frontend uses these for display and filter-extraction (no extra lookup calls needed for the matrix view).

### Route

`/leave-types/entitlements` under `MainLayoutComponent`, lazy-loaded. Same role guard as leave types: `Tenant Admin`, `HR Officer`.

### Shared-file changes

1. `employee-profile.component.ts` — Added a **Leave** tab (index 9) that renders `<app-employee-leave-overrides>`. Custom Fields shifted from index 9 to index 10.
2. `employee-profile.component.spec.ts` — Updated section count assertion from 10 to 11.
3. `leave-management.routes.ts` — Added `entitlements` child route.
4. `leave-management/index.ts` — Added barrel exports for new models + service.

### Recalculation toast (AC-5)

On every rule create, update (including inline cell edit), the success toast explicitly says "Background recalculation of affected employees has been triggered." The backend is expected to queue a Hangfire job; the frontend just informs the user.

### Priority / specificity transparency (AC-2)

A blue info banner at the top of the Entitlement Rules page shows the priority help text explaining the specificity ordering. Each rule row shows its numeric priority in a circular badge. The user story's full FR-2 specificity engine is enforced by the backend — the frontend displays the priority to make conflict resolution transparent.

## Frontend (US-LV-003) — Employee Applies for Leave

### API contract assumptions (backend building in parallel — RECONCILE)

The frontend `LeaveRequestService` targets a **new, employee-facing `/leaves` resource**
(distinct from the admin `/tenant/leave-types` and `/tenant/leave-entitlements` resources).
`environment.apiBaseUrl` already includes `/api/v1`, so the resource base is `${apiBaseUrl}/leaves`.

| Method | Path | Body / Params | Response |
|--------|------|---------------|----------|
| POST | `/api/v1/leaves` | `ICreateLeaveRequest` `{ leaveTypeId, startDate, endDate, isHalfDay, halfDaySession, reason, attachments[] }` | `ILeaveRequest` (id, status `Pending`, `totalDays`) |
| GET | `/api/v1/leaves/mine` | — | `ILeaveRequest[]` (current employee's own requests) |
| GET | `/api/v1/leaves/balances` | — | `ILeaveBalance[]` `{ leaveTypeId, entitlementDays, usedDays, remainingDays }` |

**Response shape expectations the backend must satisfy:**
- `ILeaveRequest` carries **denormalized** `leaveTypeName` and `leaveTypeColor` so the My Leaves
  list and success toast need no extra lookup. If the backend can't denormalize, the FE list
  must be changed to join against leave-types client-side.
- `halfDaySession` is `'AM' | 'PM' | null`; FE sends `null` whenever `isHalfDay` is false.
- `attachments[]` is sent as a string array. **Blob upload is DEFERRED** (NFR-3): the drop-zone
  currently collects file *names* locally and submits them as the `attachments[]` payload. The
  backend should accept already-hosted URLs / metadata here until the real upload endpoint exists.
  TODO(blob-upload) marker is in `leave-application.component.ts`.

**Balance endpoint note:** I added a dedicated `GET /leaves/balances` rather than reusing
`/tenant/leave-entitlements/compute-effective`, because the latter returns only *entitlement* days
(no used/remaining). The apply form needs current + projected remaining (AC-2), which requires
used-days too. If the backend prefers to fold balances into the entitlement resource, only
`LeaveRequestService.getMyBalances()` + `ILeaveBalance` mapping change.

### Error contract (surfaced via toast/inline)

`ILeaveRequestErrorResponse { message, code? }` where `code ∈ { 'overlap' (AC-5),
'insufficient_balance' (AC-2), 'document_required' (AC-3), ... }`. The FE shows `message`
verbatim; backend is the source of truth for overlap/balance/holiday-adjusted day count.

### Day-count is a client *estimate* only (AC-6)

`countWorkingDays()` (pure, in `leave-request.models.ts`) excludes **weekends only** (Sat/Sun).
Public holidays (AC-6) and the 5- vs 6-day work week (FR-3) are **not** modelled client-side — the
backend returns the authoritative `totalDays`. The "N days" chip is a fast-feedback estimate; the
holiday-calendar visual blocks (US-LV-007) and team calendar (US-LV-009) are deferred (field/seam only).

### Validation split

Client-side (fast feedback, mirrors ACs): required fields, start ≤ end, half-day must be single-day
+ session required (AC-4), insufficient-balance block (AC-2), sick-leave document-required hint that
becomes a hard pre-submit block when over `documentDayThreshold` with no attachment (AC-3).
`negativeBalanceAllowed` on the leave type suppresses the insufficient flag (e.g. Unpaid Leave).
Everything else (overlap AC-5, past/future window BR-1/2, max consecutive BR-3, gender/probation
BR-4/5) is enforced by the backend and surfaced via toast.

### Routes (NEW `/leave` registration)

The existing nav "Leave" item already pointed at `/leave` (gated by `Leave.View` permission) but
**no `/leave` route existed**. Added `LEAVE_REQUEST_ROUTES` to `leave-management.routes.ts` and
registered `/leave` in `app.routes.ts` under `MainLayoutComponent`:
- `/leave/apply` → `LeaveApplicationComponent`
- `/leave/my-requests` → `MyLeaveRequestsComponent`
- `/leave` → redirects to `my-requests`
- Role guard: `roleGuard(['Employee', 'Manager', 'HR Officer', 'Tenant Admin'])`.

The admin config routes stay under `/leave-types` (unchanged). The nav menu was **not** modified —
the pre-existing `/leave` nav item now resolves.

### Shared-file changes (flagged)

1. `app.routes.ts` — added the `/leave` lazy route block (US-LV-003).
2. `leave-management.routes.ts` — added the `LEAVE_REQUEST_ROUTES` export (shared with US-LV-001/002 file).
3. `leave-management/index.ts` — barrel exports for the new request models + service.

No nav-menu, employee-profile, or backend/test-case files were touched.

## Pending Leave Queue (US-LV-004)

Manager-facing read/query story built directly on the US-LV-003 `LeaveRequest` entity.
`GET /api/v1/leaves/pending` → `GetPendingLeaveRequestsQuery` → `ILeaveRequestService.GetPendingForManagerAsync`
(implemented on the existing `LeaveRequestService`, not a new service).

### Manager / reporting field name

The story (BR-1 / §7) calls the manager field `manager_employee_id`. The **actual** Employee
property is `ReportsToEmployeeId` (US-CHR-011 self-referencing FK, column `reports_to_employee_id`).
Scope = `Employees.Where(e => e.ReportsToEmployeeId == manager.Id)`. The current manager's own
employee row is resolved from `ICurrentUser.UserId` via `Employee.UserId` (same `GetCurrentEmployeeAsync`
helper used by US-LV-003). **If the current user has no employee record, the query returns an empty
page (does NOT throw)** — per the story's explicit instruction.

### Employee photo field

Story FR-2 lists `employeePhoto`. The real field is `Employee.ProfilePhotoUrl` (no separate photo
entity). Mapped straight through; null when unset.

### Pagination envelope reused

`PendingLeaveQueueResult` mirrors the established `EmployeeListResult` shape
(`Items` + `TotalCount` + `Page` + `PageSize`) rather than inventing a new envelope. Page size is
clamped to **[1, 50]** (default 20) per §10; page defaults to 1. The whole result is wrapped in the
standard `ApiResponse<T>` by the controller.

### Balance source (NFR-2) — Redis DEFERRED

Inline `currentBalance` (BR-4, real-time) is read from the **LeaveLedger running total**
(`BalanceAfter` of the latest entry for employee+leaveType+year) via the same `GetLedgerBalanceAsync`
helper as US-LV-003. The story's Redis-cached balance (key `tenant:{tenantId}:leave_balance:{empId}:{leaveTypeId}`)
is **DEFERRED**, consistent with the module-wide caching decision above.
`TODO(redis-balance-cache)` marker is in `LeaveRequestService.GetPendingForManagerAsync`.

### Team-conflict count (FR-5) — definition

For each pending request, `teamConflictCount` = number of **other team members** (same manager's
direct reports, excluding the requester themselves) who have an **Approved** leave whose date range
**overlaps** the request's `[StartDate, EndDate]` (`a.Start <= req.End && a.End >= req.Start`). Pending
leaves of teammates do NOT count — only Approved ones (someone already confirmed off).

### Overdue flag (BR-3)

`isOverdue = (UtcNow - RequestedAt).TotalDays > 30`. Computed in-memory per row.

### Filtering / sorting (FR-3)

Filters: `leaveTypeId`, `employeeId`, and a date-range **overlap window** (`startDate` → `EndDate >= x`,
`endDate` → `StartDate <= y`). Sort: `requestedAt` (default, **ascending = oldest first** per AC-1) or
`startDate`; `sortAscending` toggles direction. Uses the partial index `ix_leave_pending`
(`tenant_id, start_date WHERE status='Pending'`) for the base scan.

### SignalR real-time push (FR-6 / AC-5) — DEFERRED

No notification hub exists yet (§10 references `/hubs/notifications`). Real-time push is **NOT built**.
AC-5 is satisfied at the API level: the query returns fresh data on every reload. The manager-notify
target remains the log-only `ILeaveNotificationService` seam from US-LV-003.
`TODO(notifications/SignalR)` marker is in the handler.

### Multi-level approval (BR-2) — DEFERRED

Only **single-level direct-report** scope is implemented. No approval-level entity exists.
`TODO(multi-level)` marker in the service; add level-based queues when approval levels are modelled.

### Permission — no catalog change needed

`Leave.Approve.Team` (`PermissionCatalog.Leave.ApproveTeam`) **already existed** in the catalog and is
already granted to the **Manager** role (and Tenant Admin/HR get `Leave.ApproveAll`). The endpoint is
gated with `[RequirePermission("Leave.Approve.Team")]`. **No PermissionCatalog edit was required** for
this story (contrast US-LV-003 which added `Leave.Apply`).

### Shared files touched

- `LeaveRequestsController.cs` — added the `GET pending` action (extends the US-LV-003 controller).
- `ILeaveRequestService.cs` — added `GetPendingForManagerAsync` to the existing interface.
- `LeaveRequestService.cs` — implemented the method on the existing service.
- `LeaveRequestDtos.cs` — added `PendingLeaveRequestDto`, `PendingLeaveQueueResult`, `PendingLeaveQueueQueryParams`.

No new EF migration was needed (reuses the US-LV-003 `leave_request` table + `ix_leave_pending` index).
No DI change (service already registered). No PermissionCatalog change.

## Frontend (US-LV-005) — Manager Approves / Rejects Leave

Extends the US-LV-004 `LeaveApprovalsComponent` detail-panel footer (the Approve/Reject buttons
were present-but-DISABLED). No parallel queue was created; the same component, service, and models
were extended in place.

### API contract assumptions (backend building in parallel — RECONCILE)

Added to the existing **`/leaves`** resource (sibling to the US-LV-004 `GET /pending`):

| Method | Path | Body | Response |
|--------|------|------|----------|
| POST | `/api/v1/leaves/{id}/approve` | `{ comment?, confirmNegativeBalance? }` | `ApiResponse<ILeaveActionResult>` |
| POST | `/api/v1/leaves/{id}/reject`  | `{ reason }` (required, BR-2) | `ApiResponse<ILeaveActionResult>` |

`ILeaveActionResult = { requestId, status, currentBalance? }`. Both unwrap the standard
`ApiResponse<T>` envelope (`.data`), tolerating a bare body, exactly like `getPendingQueue`.

**Error contract the FE maps to the §8 UX** (`ILeaveActionErrorResponse { message, code?, negativeBalanceAllowed? }`):
- **409** → already actioned by another manager (AC-5): toast `message` (fallback "This request has
  already been actioned.") + close panel + `refresh()` the queue.
- **400 `code:'insufficient_balance'` + `negativeBalanceAllowed:true`** → soft block (AC-3): FE shows
  the confirmation modal, then retries `approve` with `confirmNegativeBalance:true`.
- **400 `code:'insufficient_balance'`** without that flag → hard block: toast `message`.
- **400 `code:'payroll_locked'`** (BR-4) and any other error → toast `message` verbatim (generic
  fallback "Failed to action this leave request."). Payroll-lock is just surfaced, not modelled.

The FE drives the insufficient-balance branch **entirely off the API response** (it does not pre-judge
from the inline `currentBalance`), so the backend is the single source of truth for whether negative
is allowed. If the backend prefers to return a non-error signal for the soft case, only `onActionError`
changes.

### UX decisions (§8)

- **Approve** = green solid + checkmark; click expands an **optional** comment textarea (`@expand`
  animation), then "Confirm approval". Empty comment is sent as `comment: undefined`.
- **Reject** = red **outlined** + X; click reveals a **mandatory** reason textarea. The "Confirm
  rejection" button is `[disabled]` until `rejectReason().trim().length > 0` (AC-2, BR-2); the
  textarea gets a red error ring + helper text while empty.
- Footer uses a 3-state `ActionMode` signal (`'none' | 'approve' | 'reject'`). Buttons are
  `flex-col sm:flex-row` so they are **full-width stacked on mobile** (§8) and inline on ≥sm.
- On success the actioned row leaves the queue via a `@rowOut` (`:leave`) slide-out animation; the
  item is removed from the `requests()` signal and `totalCount` is decremented (no full reload).
- `closeDetail()`/`dismissNegative()` are guarded against `isActioning()` so the panel can't be
  dropped mid-request.

### Multi-level approval (AC-4) — DEFERRED to US-ADM-007

No tenant workflow config / approval-level entity exists. The FE does **not** build level-routing UI.
If `approve` returns a "Pending L2 Approval"-style `status` (`isFurtherApprovalStatus()` regex), the
item still leaves this manager's queue and an **info** toast notes the next-level status instead of a
success toast. `TODO(US-ADM-007)` marker in `onActionSuccess`.

### Shared files touched (all within `features/leave-management`, flagged)

- `models/pending-leave.models.ts` — added `IApproveLeaveRequest`, `IRejectLeaveRequest`,
  `ILeaveActionResult`, `ILeaveActionErrorResponse`, `isFurtherApprovalStatus()`.
- `services/leave-approvals.service.ts` — added `approve()`, `reject()`, static `parseActionError()`.
- `components/leave-approvals/leave-approvals.component.ts` — wired the footer actions + modal.
- The barrel `index.ts` already re-exports the model + service files (no edit needed).
- **Test note:** the US-LV-004 sibling spec assertion "renders disabled approve/reject buttons" was
  flipped to "renders enabled" — the only sibling test edit, required because the story changes that
  exact behavior (not a weakening).

No nav-menu, route, backend, or test-case files were touched.
