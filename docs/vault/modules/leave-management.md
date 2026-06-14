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

> **Backend update (US-LV-007):** the `IHolidayProvider` seam that US-LV-003's day-count consumed
> is **no longer a NoOp**. `LeaveRequestService` (both `CreateAsync` and `GetBalancePreviewAsync`)
> now calls the DB-backed `HolidayProvider`, which returns active **public** holidays for the tenant
> (scoped to the employee's `LocationId`) in the requested range. AC-6 (holidays excluded from
> `totalDays`) is now actually enforced — see the **Holiday Calendar (US-LV-007)** section below.

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

## Frontend (US-LV-006) — Employee Leave Balance Dashboard

The Employee's default landing view for the Leave module (§10). New standalone
`LeaveDashboardComponent` (signals + OnPush) + sibling `LeaveDashboardService` +
`leave-dashboard.models.ts`. Mirrors the US-LV-003/004/005 structure exactly
(Angular Material + Tailwind, ngx-toastr, no Bootstrap, no NgModules).

### API contract assumptions (backend building in parallel — RECONCILE)

Added to the existing employee-facing **`/leaves`** resource (sibling to US-LV-003's
`/mine`, `/balances`). `apiBaseUrl` already includes `/api/v1`.

| Method | Path | Params | Response |
|--------|------|--------|----------|
| GET | `/api/v1/leaves/my-balance` | `?year={year}` | `ILeaveBalanceSummary[]` |
| GET | `/api/v1/leaves/my-ledger` | `?leaveTypeId={id}&year={year}` | `ILeaveLedgerEntry[]` (ordered by occurredAt) |
| GET | `/api/v1/leaves/my-upcoming` | — | `ILeaveRequest[]` (approved + future-pending) |

`ILeaveBalanceSummary = { leaveTypeId, leaveTypeName, color, entitlement, used,
pending, balance, carryForward, expired, isArchived? }`.
- **`balance` is authoritative from the backend** (BR-1: entitlement + carryForward
  − used − expired + adjustments). `computeBalance()` is a pure helper used only to
  cross-check / unit-test the identity; the UI renders the backend `balance` verbatim.
- **`pending` is shown separately and never deducted from `balance`** (BR-2). Rendered
  in its own row (amber) on the card.
- **`isArchived: true`** → deactivated type with a remaining balance, rendered in a
  collapsed "Archived (n)" section (BR-3). Distinct from US-LV-003's `ILeaveBalance`
  (that one has only entitlement/used/remaining for the apply-form preview); this is a
  richer summary, so a **new** model + endpoint was added rather than overloading it.

**History (FR-6) reuses `GET /leaves/mine`** (US-LV-003 `LeaveRequestService.getMyLeaveRequests`)
— no new call. The component keeps only terminal-state rows (Approved/Rejected/Cancelled)
and offers an All/Approved/Rejected/Cancelled filter pill-group.

### Charting — custom SVG arc (no new dependency, §10)

`package.json` has **no** charting lib (ngx-charts/Chart.js not installed). Per §10's
"custom SVG is acceptable" carve-out, the circular indicator is a hand-rolled two-circle
SVG using `stroke-dasharray`/`stroke-dashoffset`. The fraction is `usedFraction()`
(used/entitlement, clamped [0,1], 0 when entitlement is 0 to avoid divide-by-zero for
unpaid/zero-entitlement types). **No heavy charting dependency was added.**

### Accessibility (NFR-4)

Color is never the sole indicator: every card shows numeric entitlement/used/pending/
remaining text, and the SVG arc carries `role="img"` + an explicit `aria-label`
("{type}: {used} of {entitlement} days used, {balance} remaining, {pending} pending").
The ledger slide-over is `role="dialog" aria-modal`. Year + history pill-groups use
`role="group"` + `aria-pressed`.

### Routing — dashboard is the Leave landing (§10)

`leave-management.routes.ts` `LEAVE_REQUEST_ROUTES`: added a `dashboard` child and
**changed the empty `/leave` redirect from `my-requests` → `dashboard`** so the nav
"Leave" item (already `/leave`, gated by `Leave.View`) lands the Employee on the
dashboard. The `/leave` parent guard (`roleGuard(['Employee','Manager','HR Officer',
'Tenant Admin'])`) is unchanged; no new app.routes.ts entry and **no nav-menu edit** —
the existing `/leave` item now resolves to the dashboard.

### DEFERRED (seam + TODO only)

- **Redis-cached balance source (FR-5)** — backend concern; FE just consumes whatever
  `my-balance` returns.
- **Real-time balance push** — balances re-fetch on load / year-change only.
  `TODO(realtime-balance-push)` marker in `loadYear()`.

### Shared files touched (all within `features/leave-management`, flagged)

1. `leave-management.routes.ts` — added `dashboard` child route; flipped `/leave`
   empty-redirect `my-requests` → `dashboard`.
2. `index.ts` — barrel exports for `leave-dashboard.models` + `leave-dashboard.service`.

No `app.routes.ts`, nav-menu, employee-profile, backend, or test-case files were touched.

## Leave Balance Dashboard (US-LV-006) — Backend

Pure **read/aggregation** story over existing US-LV-002..005 data. **No new entity, no
migration, no permission-catalog change.** New read service `ILeaveDashboardService` /
`LeaveDashboardService` (kept separate from `ILeaveRequestService` so the write/decision
service stays focused; the dashboard is read-only). Three MediatR queries + handlers:
`GetMyLeaveBalanceQuery(year?)`, `GetMyLeaveLedgerQuery(leaveTypeId, year?)`,
`GetMyUpcomingLeavesQuery`.

### Endpoints (all `[RequirePermission("Leave.View.Own")]`, self-service)

| Method | Path | Query |
|--------|------|-------|
| GET | `/api/v1/leaves/my-balance` | `?year={year}` (defaults to current leave year) |
| GET | `/api/v1/leaves/my-ledger` | `?leaveTypeId={id}&year={year}` |
| GET | `/api/v1/leaves/my-upcoming` | — |

Same permission as `/mine` + `/balance-preview` (the existing self endpoints). The acting
employee is resolved from `ICurrentUser.UserId` via `Employee.UserId` (same
`GetCurrentEmployee` pattern as US-LV-003/004/005). FE contract above matches verbatim;
the backend DTO additionally carries `adjustments` and `leaveYear` (additive, harmless).

### Balance formula (BR-1) — entitlement from engine, components from ledger

`Balance = Entitlement + CarryForward − Used − Expired + Adjustments`, computed
**component-wise** from `LeaveLedger` grouped by `EntryType` — distinct from the
`BalanceAfter` running-total approach used by US-LV-003/004/005 (which answers "what's left
*right now*" for an apply/approve check). The dashboard needs the **broken-out components**
the story's FR-2 lists, so it sums the ledger by type instead of reading the last
`BalanceAfter`.
- **Entitlement** is resolved by the **US-LV-002 entitlement engine** (reused via
  `ILeaveEntitlementService.ComputeEffectiveEntitlementAsync` → `ProratedEntitlementDays`:
  override > rule > default, pro-rated). It is NOT re-derived from the ledger Accrual sum —
  so `Accrual` entries are intentionally **not** added into the balance (they realise the
  engine entitlement; adding both would double-count). This means a dashboard balance can
  differ from the raw ledger `BalanceAfter` if a tenant's accrual entries don't equal the
  engine's resolved entitlement (e.g. mid-year rule change before re-accrual). The engine is
  the source of truth for "granted", the ledger for "consumed/adjusted".
- **Used** also folds in `Encashed` (both are consumption); exposed as a positive magnitude.
  `Expired` likewise positive-magnitude; `Adjustments` is **signed**.

### Leave-year boundary (BR-4) — calendar year reused

No tenant fiscal-year config entity exists, so the established **calendar-year** convention
from US-LV-002..005 is reused (`year ?? DateTime.UtcNow.Year`; pending matched on
`StartDate.Year`). `TODO(tenant-settings)` marker in `ResolveLeaveYear`. The `year` param
serves BR-5 (previous-year selector) without any extra caching.

### Pending (BR-2) and archived (BR-3)

- **Pending** = sum of `TotalDays` of the employee's `Pending` requests for the type/year,
  surfaced in its own field and **never** subtracted from `Balance`.
- **Active** types are always returned. A **deactivated** type is included with
  `IsArchived = true` **only** if it still carries a non-zero balance for the year; otherwise
  it is dropped. (Active-types query: `lt.IsActive || ledgerTypeIds.Contains(lt.Id)`, then the
  zero-balance archived rows are filtered out.)

### History (FR-6) — reuses `GET /leaves/mine`

No new history query/endpoint. The existing `GetMyLeaveRequestsQuery` (US-LV-003) already
returns ALL of the employee's requests (incl. Approved/Rejected/Cancelled) newest-first; the
FE filters terminal states client-side. Not extended — reuse only.

### AC-5 empty state

A new joiner with no leave types configured returns an **empty list** (never throws). With
active types but no ledger, the balance equals the engine entitlement (typically the type
default) — the FE shows the empty/"being set up" state based on its own emptiness check.

### Redis balance cache (FR-5/NFR-1) — DEFERRED

Consistent with the module-wide decision (US-LV-001..005): balances are computed from the
ledger on every request. `TODO(redis-balance-cache)` marker on `LeaveDashboardService` (key
`tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`, invalidate on ledger writes).

### Tenant + self isolation

All three queries are tenant-scoped by the EF global query filters and additionally filtered
to the resolved employee's own id — an employee can only read their own balance/ledger/
upcoming. Integration tests assert both (Tenant A ≠ Tenant B; caller ≠ teammate).

### Shared files touched (flagged)

- `LeaveRequestsController.cs` — added the three `my-*` GET actions (extends the US-LV-003
  controller; no change to existing actions).
- `DependencyInjection.cs` (Infrastructure) — registered `ILeaveDashboardService`.

## Frontend (US-LV-007) — Holiday Calendar Management Per Tenant

New standalone `HolidayCalendarComponent` (signals + OnPush) + `HolidayService` +
`holiday.models.ts`. Dual view (Calendar month-grid + List) with slide-over add/edit form
and a CSV-import slide-over with client-side preview. Angular Material + Tailwind, ngx-toastr,
no NgModules, no Bootstrap — mirrors US-LV-001..006 structure.

### API contract assumptions (backend building in parallel — RECONCILE)

New tenant-config **`/holidays`** resource. `environment.apiBaseUrl` already includes `/api/v1`,
so the base is `${apiBaseUrl}/holidays`.

| Method | Path | Params / Body | Response |
|--------|------|---------------|----------|
| GET | `/api/v1/holidays` | `?year={year}` (and optional `&locationId={id}`) | `IHoliday[]` |
| GET | `/api/v1/holidays` | `?from={date}&to={date}` (FR-6, leave-day calc) | `IHoliday[]` |
| POST | `/api/v1/holidays` | `ICreateHolidayRequest` | `IHoliday` |
| PUT | `/api/v1/holidays/{id}` | `IUpdateHolidayRequest` | `IHoliday` |
| POST | `/api/v1/holidays/{id}/deactivate` | — | `IHoliday` (isActive:false) |
| POST | `/api/v1/holidays/{id}/reactivate` | — | `IHoliday` (isActive:true) |
| POST | `/api/v1/holidays/import` | multipart `file` (CSV) | `IHolidayImportResult` |

- **`IHoliday` shape:** `{ id, name, date('YYYY-MM-DD' date-only), type('public'|'restricted'|
  'optional'), locationId?, locationName?(denormalized for the List view), description?,
  isRecurring, isActive }`. The denormalized `locationName` lets the List view show the location
  without a second lookup; if the backend can't denormalize, the FE must join client-side.
- **`date` is date-only** (`YYYY-MM-DD`, no time / no timezone). The FE never `new Date()`-parses
  it for display — it slices the string — to avoid TZ off-by-one. Backend must serialize the
  holiday date as a plain ISO date string, NOT a full timestamp.
- **`IHolidayImportResult`** = `{ total, imported, skipped, errors:[{row,message}] }`. The FE shows
  "Imported X of Y. Z skipped." Duplicates are expected to be **skipped server-side** (BR-1), which
  the FE surfaces via `skipped`.
- **Error contract** `IHolidayErrorResponse { message, code? }`, code ∈ `{ 'duplicate_date' (BR-1),
  'payroll_locked' (BR-4) }`. FE shows `message` verbatim via toast; **payroll-lock is only surfaced,
  not modelled** (deferred per story).

### Calendar UI — custom CSS-grid month view (no new dependency, §10)

`package.json` has **no** calendar/charting lib. Per §10 (only free/OSS calendar components, no
heavy dependency) the month grid is a hand-rolled **42-cell CSS grid** (`buildMonthGrid` pure fn,
Sunday-first, 6 rows). Holiday markers are color-coded dots/chips: **public=blue (#2563eb),
restricted=orange (#ea580c), optional=green (#16a34a)** per §8. No `@angular/material` datepicker or
3rd-party calendar was added.

### Type colors

`ea580c` (orange-600) is used for "restricted" rather than amber, to keep clear separation from the
green "optional". Centralised in `HOLIDAY_TYPE_OPTIONS` + `getHolidayTypeColor/Badge/Label` pure
helpers (unit-tested) so calendar markers, list badges, and the legend stay consistent.

### Location filter semantics

Filtering by a location shows that location's holidays **plus** tenant-wide holidays
(`locationId === null`), since a null-location holiday applies to everyone (BR-2). Selecting a
location the FE has no rows for still shows tenant-wide holidays. The filter is **client-side** over
the already-loaded year (the service also supports a server-side `locationId` param if preferred).

### Active-only calendar markers

The calendar grid + mobile list-by-month show **active** holidays only (`activeFilteredHolidays`),
while the **List view shows all** (active + deactivated, dimmed) so admins can reactivate. Deactivate/
reactivate is an inline toggle (matching the leave-type list), not a confirm dialog.

### CSV import — client-side preview before confirm (AC-3)

Reuses the US-CHR-010 drag-drop + preview-table UX, but adds **client-side parse + validation**
(`parseHolidayCsv` pure fn) so the preview/duplicate-flagging happens **before** the multipart POST
(the employee bulk-import validated server-side only). Columns: `name,date,type` (header auto-detected
+ skipped). Rows are flagged invalid (missing/!date/!type) or duplicate (`(date,type)` colliding with
another row OR an existing loaded holiday — BR-1). Confirm is disabled unless `validCount > 0`. The
actual create is delegated to `POST /holidays/import` (the backend remains source of truth for
persistence + payroll-lock). The file-read is behind an overridable `readFileText()` seam (returns
`file.text()`) so unit tests resolve deterministically instead of fighting the async `FileReader`.

### Mobile (NFR-4)

Desktop renders the interactive month grid; below `md:` the calendar collapses to a **compact
list-by-month** view (`groupByMonth` pure fn → 12 buckets). The List view's table is horizontally
scrollable on narrow screens.

### Route + guard

`leave-types/holidays` child added to `LEAVE_MANAGEMENT_ROUTES`. It inherits the existing
`roleGuard(['Tenant Admin','HR Officer'])` from the `leave-types` parent in `app.routes.ts` — so
**no `app.routes.ts` edit and no nav-menu edit** were needed (matches the leave-config screens'
guard exactly).

### DEFERRED (seam + TODO only, per story)

- **Recurring auto-generation (FR-3/BR-5)** — backend Hangfire job; FE only sends `isRecurring`.
- **Tenant-onboarding holiday seeding (FR-5)** — separate onboarding module.
- **Redis caching (NFR-1)** — backend; consistent with the module-wide caching decision.
- **Payroll-lock (BR-4)** — only the API error is surfaced via toast, not modelled FE-side.

### Shared files touched (all within `features/leave-management`, flagged)

1. `leave-management.routes.ts` — added the `holidays` child to `LEAVE_MANAGEMENT_ROUTES`.
2. `index.ts` — barrel exports for `holiday.models` + `holiday.service`.

Cross-feature **read-only import**: the component imports `LocationService` + `ILocation` from
`core-hr/locations` to populate the location dropdown/filter — no edit to that feature. No
`app.routes.ts`, nav-menu, backend, or test-case files were touched. 64 new FE tests; full suite
1010 green.

New files: `ILeaveDashboardService`, `LeaveDashboardService`, the three queries + handlers,
`LeaveDashboardDtos.cs`, and Unit/Integration test classes. No entity/migration/permission
changes. Existing 743 tests unaffected; 16 new tests (759 total green).

## Frontend (US-LV-008) — Carry-Forward & Expiry surfacing

Light FE story — the heavy logic is two backend Hangfire jobs
(`ProcessLeaveYearEndJob`, `ProcessCarryForwardExpiryJob`) + Redis invalidation, all
DEFERRED to backend. FE scope: an HR-facing preview report + a surgical surfacing of
carry-forward/expiry on the US-LV-006 dashboard.

### New: Carry-Forward Preview report (AC-5)

New standalone `CarryForwardPreviewComponent` (signals + OnPush) + sibling
`CarryForwardPreviewService` + `carry-forward-preview.models.ts`. Read-only Notion-like
table (§10 — does NOT lock/commit). Year selector at top (closing year), filters by
department / employee (text) / leave type, color coding **carry-forward = blue
(`text-blue-600`)**, **forfeited = gray strikethrough (`text-neutral-400 line-through`)**
per §8. Loading skeletons + dual empty state (nothing-to-process vs no-filter-match).
Desktop table + mobile card list (360px+). Summary strip (rows / Σcarry-forward /
Σforfeited).

**Filter options are derived client-side from the loaded rows** (the rows are denormalized
with `departmentName` + `leaveTypeName`), so the component does NOT import the
department/employee/leave-type lookup services — keeps it self-contained, mirrors how the
holiday list derives its filters. Pure helpers (`distinctDepartments`, `distinctLeaveTypes`,
`matchesEmployeeTerm`, `sumTotals`, `buildPreviewYearOptions`) live in the models file and
are separately unit-tested.

### API contract assumption (backend building in parallel — RECONCILE)

`GET /api/v1/leaves/carry-forward-preview?year={year}` → `ICarryForwardPreviewRow[]`
(`apiBaseUrl` already includes `/api/v1`; resource base `${apiBaseUrl}/leaves`).
Row shape (FR-5):
`{ employeeId, employeeName, departmentName?, leaveTypeId, leaveTypeName,
projectedCarryForward, projectedForfeiture }`. `departmentName` optional/denormalized; if the
backend can't denormalize it, that row just won't appear in the department-filter options.

### "Run carry-forward now" button — OMITTED (no trigger endpoint)

§10 mentions a manual re-trigger with a confirmation dialog, but the contract exposes **no
manual-trigger endpoint** and the jobs run on a Hangfire schedule. Per the brief, the button
is **omitted** rather than inventing backend behavior. A seam-note in
`CarryForwardPreviewService` says: if a trigger endpoint is added, add `runNow(year)` gated
behind a confirmation dialog.

### Dashboard surfacing (§8) — surgical extension of US-LV-006 card

Extended the existing `LeaveDashboardComponent` balance card only (no restructure):
- Replaced the single combined `+X carried forward · Y expired` footnote with **two separate
  line items** — carry-forward (blue) and expired (gray strikethrough), rendered as a small
  `<dl>` below a top border. Test ids `carry-forward-value` / `expired-value`.
- Added an **amber expiring-soon indicator** ("N carry-forward day(s) expiring on <date>"),
  shown **only when** `carryForward > 0` AND the new optional
  `ILeaveBalanceSummary.carryForwardExpiry` (date-only `'YYYY-MM-DD'`) is present.

**Expiry date is a TODO seam.** The US-LV-006 `my-balance` endpoint does **not** yet return a
carry-forward expiry date. Added the optional `carryForwardExpiry?: string | null` field
(additive, harmless) so the amber indicator lights up once the backend supplies it; until
then the field is undefined and only the blue/gray line items render.
`TODO(carry-forward-expiry-date)` marker on the field — the backend year-end job records the
`carry_forward` ledger entry with a `carry_forward_expiry_months` window (FR-1/BR-3); the
projection should surface the resulting date.

### Route + guard

`leave-types/carry-forward-preview` child added to `LEAVE_MANAGEMENT_ROUTES`. Inherits the
existing `roleGuard(['Tenant Admin','HR Officer'])` from the `leave-types` parent in
`app.routes.ts` — so **no `app.routes.ts` edit and no nav-menu edit** were needed (matches the
leave-config screens' guard exactly, same as US-LV-007 holidays).

### Shared files touched (all within `features/leave-management`, flagged)

1. `leave-management.routes.ts` — added the `carry-forward-preview` child to
   `LEAVE_MANAGEMENT_ROUTES`.
2. `index.ts` — barrel exports for `carry-forward-preview.models` + `carry-forward-preview.service`.
3. `models/leave-dashboard.models.ts` — added optional `carryForwardExpiry?` to
   `ILeaveBalanceSummary` (additive).
4. `components/leave-dashboard/leave-dashboard.component.ts` — extended the balance card
   markup (separate CF/expired line items + amber expiring-soon indicator).
5. `components/leave-dashboard/leave-dashboard.component.spec.ts` — updated the US-LV-006 spec
   with 4 new tests for the line items + expiring-soon indicator (no existing assertion
   weakened; the old combined-footnote had no test that needed changing).

No `app.routes.ts`, nav-menu, employee-profile, backend, or test-case files were touched.

## Frontend (US-LV-009) — Team Leave Calendar View

New standalone `TeamLeaveCalendarComponent` (signals + OnPush) + sibling
`TeamCalendarService` + `team-calendar.models.ts`. Mirrors US-LV-001..008 structure
(Angular Material + Tailwind, no NgModules, no Bootstrap). **No new dependency** — the
month grid is a hand-rolled CSS grid reusing the US-LV-007 holiday-calendar approach
(`buildMonthGrid` pure fn, 42-cell Sunday-first). No `angular-calendar`/charting lib
added (checked `package.json` first, none present — §10).

### Three views via a segmented control (FR-5, §8)

- **Month (AC-1):** CSS-grid month calendar; each leave is a color-coded block per
  employee on every covered date; today's cell ring-highlighted; public-holiday cells get
  a light-gray (`#f1f5f9`/slate-100) background (FR-7); hover `title` tooltip with detail.
- **Week (AC-3):** Gantt-style horizontal bars — 11rem employee-name Y-axis column +
  7 day columns (X-axis). One row per employee with coverage in the visible week
  (`buildWeekRows` pure fn, sorted by name). Horizontally scrollable on narrow screens.
- **List (AC-4, mobile default):** chronological groups by **start date** (`buildListGroups`),
  each an employee card (name, leave type/"On leave", half-day or N-days, status badge).
- **Mobile (§8):** `defaultView()` reads `window.innerWidth < 768` → defaults to **list**;
  guarded for non-browser (`typeof window`).

### Scope-aware rendering (BR-1, BR-2, AC-1, AC-2) — the load-bearing decision

The component renders **whatever the API returns** and never assumes manager-only fields:
- `ITeamCalendarEntry` makes `leaveTypeName`, `color`, `status` **optional**.
- **Employee scope:** the API suppresses pending + leave-type detail; entries arrive with
  none of those fields → rendered generically as **"On leave"** (`entryLabel` fallback),
  neutral slate color (`#64748b`), **no** status badge, **no** leave-type filter chips, and
  the **status filter chip group is hidden** (`hasStatusScope` = any entry has a status).
- **Manager scope:** entries carry detail → per-type color legend, status badges
  (Approved=green, Pending=amber + 70% opacity blocks), leave-type + status filter chips.
- The FE does **not** request or infer hidden data. The `status` query param is only sent
  when a status filter is active (which only the manager-scope UI exposes).

### Color resolution + legend (§8)

`buildLegend` builds a name→color index: prefers the API-provided `color` (manager scope),
else assigns a stable Notion-muted palette color per distinct leave-type name (first-seen
order). Employee scope (no type detail) → a single generic "On leave" legend item. The
legend always appends a "Public holiday" gray swatch. `resolveEntryColor` = API color →
palette index → neutral.

### Half-day differentiation (BR-5)

Half-day entries get: a diagonal stripe overlay on the block/bar (`leave-block-half`/
`gantt-bar-half` CSS), plus an **AM/PM pill** in the month block (falls back to "½" when
session is unknown), and a "Half day (AM/PM)" line in the list view.

### Data loading / range

Loads on init + on month/week/employee/status change. `activeRange()` fetches the active
month **±7 days** so multi-day leaves spilling across month boundaries and the week view
have data. Leave-**type** filter is **client-side** (by denormalized name); employee +
status filters are passed **server-side** (re-fetch) AND applied client-side. `goToToday()`,
`changeMonth`, `changeWeek` (week nav re-fetches if it crosses into another month).

### API contract assumptions (backend building in parallel — RECONCILE)

New employee/manager-facing endpoint on the existing **`/leaves`** resource. `apiBaseUrl`
already includes `/api/v1`, so the base is `${apiBaseUrl}/leaves/team-calendar`.

| Method | Path | Params | Response |
|--------|------|--------|----------|
| GET | `/api/v1/leaves/team-calendar` | `?from={date}&to={date}` `[&employeeId][&leaveTypeId][&status]` | `ITeamCalendarResponse` |

- **Combined payload** `{ entries: ITeamCalendarEntry[]; holidays: ITeamCalendarHoliday[] }`.
  `TeamCalendarService.normalize` **also tolerates a bare `entries[]` body** (→ holidays:[])
  and a null body, so the backend may return either shape without breaking the FE.
- **`ITeamCalendarEntry`** = `{ employeeId, employeeName, leaveTypeName?, color?, startDate,
  endDate, status?('Approved'|'Pending'), totalDays, isHalfDay?, halfDaySession?('AM'|'PM') }`.
  Manager-only fields (`leaveTypeName`/`color`/`status`) are **optional** — the backend MUST
  omit them for employee scope (BR-1) and MUST omit Pending entries entirely for employee
  scope (AC-2).
- **`ITeamCalendarHoliday`** = `{ date('YYYY-MM-DD' date-only), name }`. Dates are **date-only
  strings** — the FE slices strings (never `new Date()`-parses) to avoid TZ off-by-one,
  consistent with US-LV-007. Backend must serialize holiday/leave dates as plain ISO dates.
- **Error contract** `ITeamCalendarErrorResponse { message, code? }` — surfaced verbatim in the
  error state with a Try-Again button.
- The backend is the source of truth for **scope/access control** (which employees, which
  statuses, detail suppression) — the FE only renders. The backend should reuse the US-LV-004
  `ReportsToEmployeeId` direct-report scope for managers + department scope for employees, and
  the US-LV-007 `HolidayProvider` for the `holidays` payload (location-aware per the caller).

### DEFERRED (seam + TODO only, per story §10)

- **Redis caching (NFR-1)** — backend; consistent with the module-wide deferred-Redis decision.
- **Real-time updates** — calendar re-fetches on nav/filter change only.
- **Drag-and-drop leave creation** — §10 explicitly excludes it; the calendar is read-only
  (leave is applied via US-LV-003's form). No create/edit affordance on cells.

### Shared files touched (all within `features/leave-management`, flagged)

1. `leave-management.routes.ts` — added the `team-calendar` child to `LEAVE_REQUEST_ROUTES`
   (the `/leave` parent already guards `roleGuard(['Employee','Manager','HR Officer','Tenant
   Admin'])` — the most permissive leave route, same as the dashboard). **No `app.routes.ts`
   edit and no nav-menu edit** were needed.
2. `index.ts` — barrel exports for `team-calendar.models` + `team-calendar.service`.

No `app.routes.ts`, nav-menu, employee-profile, backend, or test-case files were touched.
**76 new FE tests** (models pure fns, service HTTP + normalize + parseError, component view
toggle / month blocks+color / today+holiday bg / half-day / week Gantt / list grouping /
filter bar / status-scope hide / employee-scope graceful render / mobile-default-list /
error+nav). Full suite **1086 green, 0 regressions**.

## Holiday Calendar (US-LV-007) — Backend

New `Holiday` entity + the calendar CRUD/CSV/recurrence service, and — the load-bearing part —
the **real `IHolidayProvider`** that finally wires US-LV-003's holiday-exclusion seam.

### Entity / migration

`Holiday : BaseEntity` → table **`holiday`** (`HolidayConfiguration`). Columns: name(100), date(date),
type(varchar 20, `HolidayType` enum {Public,Restricted,Optional} stored **as string** like other leave
enums), location_id(uuid, nullable FK → `locations`), description(text), is_recurring, is_active,
+ BaseEntity audit/is_deleted. Migration `20260614045054_AddHolidayEntity` (CLI-generated, has the
`[Migration]` attribute, **no erroneous `xmin` AddColumn** — Holiday carries no concurrency token).
FK onDelete = **SetNull** (deleting a location makes its holidays tenant-wide rather than cascading).

### BR-1 unique-date — two partial indexes (NULL-aware)

A single unique `(tenant_id, date, location_id)` index does **not** stop two tenant-wide holidays on
the same date, because PostgreSQL treats NULLs as distinct. BR-1 is therefore split into **two partial
unique indexes** (mirroring how LeaveType used a partial index for soft-delete-aware uniqueness):
1. `ix_holiday_tenant_date_location_unique` on `(tenant_id, date, location_id)` where
   `location_id IS NOT NULL AND is_deleted = false` — location-scoped rows.
2. `ix_holiday_tenant_date_nolocation_unique` on `(tenant_id, date)` where
   `location_id IS NULL AND is_deleted = false` — tenant-wide rows.
Plus the §7 range index `(tenant_id, date)`. The service also enforces BR-1 at write time
(`DuplicateExistsAsync`) for a clean error before the DB index trips, same as LeaveType name-uniqueness.

### Real IHolidayProvider (AC-2 / FR-6) — the seam swap

`HolidayProvider` (Infrastructure/Services) replaces `NoOpHolidayProvider` in DI. It returns active
**PUBLIC** holiday dates in a range for the current tenant (restricted/optional are NOT auto-excluded).
**Location filtering (BR-2):** when a `locationId` is passed, tenant-wide (`location_id IS NULL`) **plus**
that-location holidays are returned; when null, only tenant-wide — so a New-York-only holiday does not
shorten a London employee's leave. `LeaveRequestService` already passed `employee.LocationId` into the
seam, so location-aware exclusion works end-to-end. `NoOpHolidayProvider` is **retained** (used by the
existing US-LV-003/004/005 unit/integration tests, which construct the service with their own no-op —
the DI default swap does **not** touch them; confirmed 0 regressions).

### CSV import (FR-4, AC-3, NFR-3)

`POST /api/v1/holidays/import` multipart → `ImportCsvAsync`. **Reuses CsvHelper** (the same library as
US-CHR-010 bulk employee import) with the same skip-`#`-comment-lines + tolerant header parsing.
Columns: name, date(YYYY-MM-DD), type. Per-row validation; **duplicates (same date, both DB-existing and
within-file) are flagged in the result** rather than silently skipped (AC-3). Cap = 100 rows (NFR-3).
Imported rows are tenant-wide (`location_id = null`); location-scoped CSV import is out of scope.

### Recurring job (FR-3, BR-5)

`HolidayRecurrenceJob` (Hangfire recurring, registered `holiday-recurrence-generation`, **Cron 1 Dec**
≈30 days before year-end — mirrors `LeaveAccrualJob` registration). Iterates Active/Trial tenants, sets
tenant context per scope, calls `GenerateRecurringForYearAsync(tenantId, nextYear)`. **Idempotent:**
skips a holiday when next-year's `(date, location)` entry already exists, so re-runs are safe.
Feb-29 recurring holidays clamp to Feb-28 on non-leap target years (`AddYearSafe`).

### Permission choice

Story §2 allows `Leave.Configure` **or** `Tenant.Admin`. No `Leave.Configure` constant existed (only
the broader `Leave.ConfigurePolicy` for entitlement rules). To match the per-resource pattern used by
`LeaveType.*`, `Location.*`, `Department.*`, I added a **`Holiday.*`** permission group
(`View/Create/Edit/Deactivate/Import`) to `PermissionCatalog`, granted to Tenant Admin / HR Manager /
HR Officer; `Holiday.View` is also granted to Manager + Employee so the calendar/leave-apply flows read
it. (Chose new per-resource constants over reusing the broad `Leave.ConfigurePolicy`, for consistency.)

### Deferrals (seam + TODO only — NOT built)

- **Redis cache (NFR-1):** query DB directly, consistent with the module-wide deferred-Redis decision.
  `TODO(redis-holiday-cache)` in `HolidayService`.
- **Tenant-onboarding seeding (FR-5):** `SeedHolidaysForTenantAsync(tenantId, countryCode)` is provided
  + idempotent + reads embedded country JSON templates (`HolidayTemplates/holidays.{US,LK}.json`,
  embedded resources in the Infrastructure csproj), but is **UNWIRED** — no onboarding wizard exists.
  `TODO(onboarding)`, mirrors how `LeaveTypeService.SeedDefaultsForTenantAsync` was left unwired.
- **Payroll-period lock on delete (BR-4):** soft-deactivate only; no payroll module. `TODO(payroll)`.
- **Optional-holiday-as-leave-type accounting (BR-3):** out of scope. `TODO(optional-holiday)`.

### Endpoints

`GET /api/v1/holidays?from&to&year&locationId&activeOnly` (from/to range FR-6 takes precedence over
year AC-4), `GET /{id}`, `POST`, `PUT /{id}`, `POST /{id}/deactivate`, `POST /import`.

### Shared files touched (flagged)

- `AppDbContext.cs` — `DbSet<Holiday>` + the Holiday global query filter.
- `DependencyInjection.cs` (Infrastructure) — registered `IHolidayService`; **swapped the
  `IHolidayProvider` default from `NoOpHolidayProvider` → `HolidayProvider`** (the seam wiring).
- `PermissionCatalog.cs` — new `Holiday.*` group + role grants.
- `Program.cs` (Api) — registered `HolidayRecurrenceJob` + the recurring-job schedule.
- `HRM.Infrastructure.csproj` — embedded `HolidayTemplates/*.json`.
- `IHolidayProvider.cs` / `NoOpHolidayProvider.cs` — XML-doc updates only (point at the real impl);
  `LeaveRequestService.cs` already consumed the seam (no logic change needed).

New code: `Holiday`, `HolidayType`, `HolidayConfiguration`, the migration, `IHolidayService` +
`HolidayService` + `HolidayProvider`, Holidays feature (commands/queries/validators/DTOs/handlers),
`HolidaysController`, `HolidayRecurrenceJob`, country templates, and Unit/Integration tests.
**49 new tests** (CRUD, BR-1 dup, CSV valid/dup/limit, provider range+location, the **AC-2 Mon–Fri
with-Wed-holiday = 4 days** test, recurring idempotency, validators, tenant isolation, end-to-end
leave-excludes-holiday). Full suite **806 green, 0 regressions**.

## Carry-Forward & Expiry (US-LV-008) — Backend

Two Hangfire jobs over the existing LeaveLedger + LeaveType infrastructure. **Reused** the existing
`LedgerEntryType` values `CarryForward`/`Expired`/`Encashed` (no new ledger types) and the existing
`LeaveType.CarryForwardLimit` / `CarryForwardExpiryMonths` / `Encashable` / `MaxEncashDays` /
`NegativeBalanceAllowed` fields (no duplicate fields added).

### New tracking entity + migration

`LeaveCarryForwardTracking : BaseEntity` → table **`leave_carry_forward_tracking`**. Columns:
employee_id (FK CASCADE), leave_type_id (FK RESTRICT), from_year, to_year, carried_days(numeric 7,2),
expiry_date(date, nullable), expired_days(numeric 7,2), status(varchar 20) + BaseEntity. **Status** is
a string constant set (`CarryForwardTrackingStatus.Active/Expired/Consumed`) stored as text (mirrors
the leave-enum-as-string convention), not a DB enum. Migration `20260614065457_AddLeaveCarryForwardTracking`
(CLI-generated, has `[Migration]` attr, **no erroneous `xmin` AddColumn**). One row per
(tenant, employee, leave_type, from_year, to_year), enforced by partial unique index
`ix_leave_carry_forward_tracking_unique` (`is_deleted = false`) — this is the **idempotency anchor**
(NFR-3). A `(tenant_id, status, expiry_date)` index supports the monthly expiry scan. Global query
filter + DbSet added to AppDbContext mirroring LeaveLedger.

**Why a tracking row even when nothing is carried:** a Consumed-status zero-carry row is still written
so the year-end job's idempotency check is stable (a pair is "done" once tracked) and the preview/job
have a consistent anchor.

### Shared pure calculation — preview cannot diverge from the job

`LeaveCarryForwardCalculator` (internal static, pure — mirrors `LeaveEntitlementEngine`) is the
**single source of truth** the year-end job AND the preview API both call:
- `AppliesTo(leaveType)` — BR-6: false when `CarryForwardLimit is null`, `NegativeBalanceAllowed`, or
  `AnnualEntitlement <= 0`. A configured limit of **0 IS applicable** (forfeit-all path, AC-4) — not skipped.
- `Compute(unused, limit)` — BR-1 carried = MIN(unused, limit); BR-2 forfeited = unused − carried;
  negative/zero unused carries & forfeits nothing.
- `ComputeExpiryDate(toYear, months)` — BR-3: first day of new year + months; null when months null.
- `RemainingCarriedDays(carried, usedInNewYear, alreadyExpired)` — BR-4 FIFO: carried days consumed
  first, then less anything already expired (idempotent). **Derived, not a stored counter.**

The story's "preview must match the job" Test Hint is structurally guaranteed because both paths flow
through `Compute`. Tests assert the preview row numbers equal the ledger entries the job writes.

### unused_balance reuses the US-LV-006 ledger formula

`unused_balance = Entitlement + CarryForward − Used − Expired + Adjustments`, summed component-wise
from `LeaveLedger` grouped by `EntryType` (Used folds in Encashed; Accrual NOT re-added because the
engine value already represents the grant — same rationale as US-LV-006 `LeaveDashboardService`).
Entitlement comes from the **US-LV-002 engine** via `ILeaveEntitlementService.ComputeEffectiveEntitlementAsync`
(`ProratedEntitlementDays`).

### Year-end job (`ProcessLeaveYearEndJob`, Cron `0 2 1 1 *` — 1 Jan)

Closing year = `UtcNow.Year − 1` (runs on 1 Jan). Per active/trial tenant → set tenant context →
batch-page active employees (500/page, NFR-1) × applicable leave types. Writes a **CarryForward**
ledger entry (positive, `transaction_date` = 1 Jan of new year) and an **Expired** ledger entry
(negative, year-end date) per FR-6, plus a tracking row. **BR-5 encashable branch:** when the leave
type is `Encashable`, the forfeitable balance is written as an **Encashed** entry instead of Expired,
capped at `MaxEncashDays` when set — any residue beyond the cap still Expires. Idempotent skip when a
tracking row already exists for the pair/year.

### Monthly expiry job (`ProcessCarryForwardExpiryJob`, Cron `0 3 1 * *` — 1st of month)

Per tenant → for each **Active** tracking row whose `expiry_date <= asOf`: compute FIFO-remaining
carried days (`RemainingCarriedDays`) from new-year Used/Encashed ledger vs. `carried_days` less
`expired_days`; if > 0 write an **Expired** entry (in the new leave year) for the remainder and bump
`expired_days`; set status to **Expired** (days expired) or **Consumed** (FIFO already used them all).
Terminal status ⇒ never re-processed (idempotent double-expiry guard).

### Preview API + permission

`GET /api/v1/leaves/carry-forward-preview?year={year}` → `GetCarryForwardPreviewQuery` →
`LeaveCarryForwardService.PreviewYearEndAsync`. Read-only, commits nothing. Returns per-employee×type
projected carry/forfeit (rows with zero carry+forfeit are omitted). **New dedicated
`LeaveCarryForwardController`** (not the self-service `LeaveRequestsController`), gated
`[RequirePermission("Leave.ConfigurePolicy")]` — the same leave-config permission US-LV-002
entitlement endpoints use (chose it over the per-resource `LeaveType.*`/`Holiday.*` style because this
is policy/config, not a CRUD resource). **No PermissionCatalog change needed.**

### Deferrals (seam + TODO only — NOT built)

- **Redis cache invalidation after processing (FR-7/AC-3):** `TODO(redis-balance-cache)` in the
  service — consistent with the module-wide deferred-Redis decision.
- **Tenant fiscal-year boundary (§10):** calendar year reused (`year ?? UtcNow.Year`, closing =
  `Year−1` in the job). `TODO(tenant-settings)` — same convention as US-LV-006/007.
- **Manual HR re-trigger confirmation (UI):** frontend concern, not built backend-side.

### Shared files touched (flagged)

- `AppDbContext.cs` — `DbSet<LeaveCarryForwardTracking>` + global query filter.
- `DependencyInjection.cs` (Infrastructure) — registered `ILeaveCarryForwardService`.
- `Program.cs` (Api) — registered both jobs (scoped) + the two recurring schedules.

No PermissionCatalog, no LeaveLedger/LeaveType changes. **32 new tests** (calculator BR-1/BR-2/AC-4/
BR-6/BR-3/FIFO; service year-end 5-carry/3-expire, zero-limit, skip-types, encashable+cap,
idempotency, expiry+FIFO, double-expiry guard, preview-matches-job, preview-commits-nothing;
integration year-end ledger+tracking, idempotent re-run, tenant isolation A≠B, preview query +
tenant scope). Full suite **838 green, 0 regressions**.

## Team Leave Calendar (US-LV-009) — Backend

Pure **read/query** story over the existing US-LV-003 `LeaveRequest`, `Employee`, `LeaveType`,
and US-LV-007 `Holiday` data. **No new entity, no migration.** One MediatR query
`GetTeamLeaveCalendarQuery(Params)` + handler delegating to a **new method on the existing
`ILeaveRequestService`** (`GetTeamLeaveCalendarAsync`) — reuses the same `GetCurrentEmployeeAsync`
+ `ReportsToEmployeeId` direct-report resolution as US-LV-004, so scoping logic stays in one place.

### Scope-role resolution (AC-1/AC-2, BR-1/BR-2/BR-3) — three-way

Resolved per request from the caller (no extra config entity):
1. **HR / "All" scope** — caller has `Leave.View.All` (`ICurrentUser.Permissions.Contains(...)`). Returns
   ALL employees' Approved + Pending leaves in the tenant, full detail. Already granted to Tenant
   Admin/HR Manager/HR Officer/Auditor in the catalog — **no PermissionCatalog change needed**
   (`Leave.View.All` pre-existed; contrast US-LV-007 which added `Holiday.*`).
2. **Manager scope** — caller has ≥1 direct report (`Employees.Where(e => e.ReportsToEmployeeId ==
   me.Id)`). Returns those direct reports' Approved + Pending, full detail (leave type, colour, status).
3. **Employee scope** — otherwise. Returns own-**department** colleagues' (`e.DepartmentId ==
   me.DepartmentId`) **Approved-only** leaves, with detail SUPPRESSED.

Resolution is HR → Manager → Employee (a manager who also has `Leave.View.All` gets All). The resolved
scope ("All"/"Manager"/"Employee") is echoed in the response so the FE can adapt.

### BR-1 employee-view suppression — enforced server-side (the data-leak guard)

The single most important rule: employee scope must **not** leak pending requests or leave types.
Implemented so the API itself never returns the hidden fields (NOT reliant on the FE hiding them):
- Employee-scope query filters to `Status == Approved` only (Pending/Rejected/Cancelled never selected).
- In the DTO projection, when `fullDetail == false` the handler sets `LeaveTypeName = null`,
  `Color = null`, `Status = null`. The employee row carries only `employeeId/name`, dates, totalDays,
  and half-day flags — enough to render "on leave", nothing more.
- The FR-6 `status` and `leaveTypeId` filters are **ignored** for employee scope (a `status=Pending`
  filter still returns nothing). Tested explicitly (`Employee_NeverSeesAnyAwaitingEntries_EvenWithStatusFilter`).

### BR-4 / status set

Cancelled AND Rejected are both excluded everywhere. Only Approved (+ Pending for manager/HR) are
returned. The leave query uses the `[StartDate, EndDate]` overlap window vs. the requested `[from, to]`
(`lr.StartDate <= to && lr.EndDate >= from`), same overlap predicate as the US-LV-004 pending queue.

### Holidays (FR-7) — reuse US-LV-007 `IHolidayService.GetAllAsync`, separate collection

Rather than the `IHolidayProvider` seam (which returns only a `Set<DateOnly>`), FR-7 reuses
**`IHolidayService.GetAllAsync(from, to, …)`** which returns full `HolidayDto`s (name/date/type) — the
richer shape the FE needs for background highlights. Returned as a **separate `Holidays` collection**
in `TeamLeaveCalendarDto` (not merged into the leave entries — cleaner for the FE to render as a
background layer vs. leave bars). Filtered to **Public** holidays scoped to the caller's `LocationId`
(tenant-wide + own-location), consistent with US-LV-007 BR-2. `IHolidayService` was added as an
**optional** (nullable, default-null) constructor param on `LeaveRequestService` so the existing
US-LV-003/004/005 unit tests (which `new` the service with the original 6 args) keep compiling without
edits; DI always supplies it in production.

### Half-day passthrough (BR-5)

`IsHalfDay` + `HalfDaySession` are passed straight through on every entry (all scopes) so the FE can
render a half-block/AM-PM indicator. Not suppressed for employee scope (it's not type/status detail).

### Endpoint + authorization

`GET /api/v1/leaves/team-calendar?from&to&employeeId&leaveTypeId&status` on the existing
`LeaveRequestsController`. **No per-action `[RequirePermission]`** — only the class-level `[Authorize]`
applies, because the three roles that may call it (Employee `Leave.View.Own`, Manager
`Leave.View.Team`, HR `Leave.View.All`) hold *different* permissions and the handler does the row-level
scoping. Gating on any single permission would wrongly exclude one of the three. The status filter
(FR-6) is honoured for manager/HR scope only.

### Deferrals (seam + TODO only — NOT built)

- **Redis caching (NFR-1, P95 < 300ms):** query the DB directly, consistent with the module-wide
  deferred-Redis decision. The `(tenant_id, employee_id, status, start_date)` index (§7) and the
  `(tenant_id, date)` holiday index serve the range scans.
- **PostgreSQL RLS literal (NFR-2):** tenant isolation is via EF global query filters + `TenantInterceptor`
  (same mechanism as all prior leave stories) — no per-entity RLS policy. See the module-wide note above.

### Shared files touched (flagged)

- `LeaveRequestsController.cs` — added the `GET team-calendar` action (extends the US-LV-003 controller).
- `ILeaveRequestService.cs` — added `GetTeamLeaveCalendarAsync` to the existing interface.
- `LeaveRequestService.cs` — implemented the method; added optional `IHolidayService` ctor param.

New files: `TeamLeaveCalendarDtos.cs`, `GetTeamLeaveCalendarQuery` + handler, and Unit/Integration test
classes. No new entity/migration/permission/DI changes (`IHolidayService`/`ILeaveRequestService` already
registered; the optional ctor param resolves from the existing registration). **15 new tests**
(manager Approved+Pending+detail, manager≠other-manager, employee dept-Approved-only + suppression +
no-pending-even-with-filter, HR ViewAll all-teams, Cancelled/Rejected excluded, holidays-in-range,
half-day passthrough, leave-type filter, range overlap, no-employee empty, invalid range; integration
manager happy-path+holidays, tenant A≠B, employee-scope suppression e2e). Full suite **853 green, 0
regressions**.
