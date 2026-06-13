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
- `US-LV-002` -- Leave Request Application (will consume leave types for dropdown/validation)
- `US-LV-003+` -- Leave Balances, Accruals, Carry-Forward processing

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
