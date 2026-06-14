---
type: module-note
module: attendance
status: active
created: 2026-06-14
---

# Attendance

Employee time-tracking. US-ATT-001 establishes the module scaffold: browser clock-in with
optional geolocation, tenant-configurable policy, and an open/closed punch model.

## Domain rules
- **One open punch per employee (BR-1/FR-2/AC-2).** An `attendance_log` row with `clock_out IS NULL`
  is "open". An employee may have at most one open record at a time. A second clock-in while one is
  open returns **409** with the exact message `"You have already clocked in. Please clock out first."`
  Enforced in the service via an `AnyAsync(clock_out == null)` check AND a PostgreSQL partial unique
  index `ix_attendance_log_open_unique` on `(tenant_id, employee_id) WHERE clock_out IS NULL AND is_deleted = false`.
  The story's "same calendar day" wording was implemented as "at most one open record" — simpler,
  matches BR-1 exactly, and avoids needing a tenant-timezone for day-boundary math (deferred).
- **Timestamps are UTC (FR-1/FR-7).** `clock_in`/`clock_out` are `timestamptz`, set to `DateTime.UtcNow`.
  Local-time display is the frontend's job.
- **Policy is per-tenant (`attendance_settings`, one row/tenant).** Created lazily with all
  enforcement OFF the first time a tenant clocks in. Drives: geolocation-required (BR-2/AC-3),
  geo-fence (FR-3), IP allowlist (BR-3/AC-5), photo-required (BR-6), grace period (BR-4, stored only).
- **Active-employee only (BR-5).** Terminated/Inactive employees are rejected with 403.
- **Permission:** clock-in is gated by `Attendance.CheckIn` (held by the Employee built-in role).
  The story named a `Attendance.Clock.Self` permission that does NOT exist in `PermissionCatalog`;
  reused the existing `Attendance.CheckIn` rather than inventing a new, unseeded permission. If the
  literal `Attendance.Clock.Self` name is later required, it must be added to the catalog + role seeds.

## Enforcement / error contract (status codes)
- 400: tenant unresolved · geo required but missing · outside geo-fence · photo required but missing
- 403: no employee linked to user · employee not active · IP not on allowlist
- 409: an open record already exists

## Deviations from the story's aspirational tech
- **No PostgreSQL RLS** (NFR-2). This codebase enforces tenant isolation via EF global query filters
  in `AppDbContext.OnModelCreating` + `TenantInterceptor` stamping `TenantId` on `BaseEntity`. The
  attendance entities follow that pattern. See [[core-hr]] / [[leave-management]] for the same approach.
- **No Redis (FR-6 deferred).** The `att:{tenant}:{emp}:{date}` dashboard cache key is NOT implemented.
  Revisit when a dashboard read endpoint (and a real cache) lands.
- **Idempotency (NFR-4)** reuses the existing `IdempotencyRecord` entity (operation name `"ClockIn"`,
  5s expiry window). A replay with the same `Idempotency-Key` returns the cached response (no dup row).

## Edge cases
- Lat/long must be supplied together (validator) and stored as `numeric(10,7)`.
- Geo-fence uses a Haversine great-circle distance vs `geo_fence_radius_meters`; skipped if the
  tenant has no allowed-location lat/long configured even when `geo_fence_enabled`.
- IP/user-agent captured in the **controller** from `HttpContext` and passed on the command — the
  service layer has no HttpContext access. Same pattern as the `Idempotency-Key` header.

## Testing note
Integration tests use the **InMemory provider through the real DI/MediatR pipeline**, NOT
Testcontainers — the verify gate runs `dotnet test` with no Postgres and Docker is unavailable in
the agent sandbox; a Testcontainers test would red the gate. PG-specific schema (partial unique
index, text[], numeric) is validated by the separate `migrations` CI job. (Same rationale already
documented for leave-management integration tests.)

## US-ATT-002 — Clock-out + work-hours auto-calculation
- **Closes the open record (BR-1/AC-2).** Clock-out requires an open (`clock_out IS NULL`) record.
  No open record → **404** with code `no_active_clock_in` and message
  `"No active clock-in found. Please clock in first or submit a regularization request."`
  NOTE: the implementer's task brief quoted "...Please clock out first..." for AC-2, which is
  semantically wrong (you have nothing to clock out). The story file AC-2 says "clock in first";
  used the story wording. Reconcile FE copy against the story, not the brief.
- **Work-hours calc is a pure domain helper** `AttendanceCalculator.Calculate(...)` in
  `HRM.Domain/Entities/AttendanceCalculator.cs` — used by both the service and the auto-clock-out
  job so the rules are identical. Minute-accurate (truncates partial minutes, never negative).
- **Calculation policy lives on `attendance_settings` at the TENANT level** (US-ATT-005 shift entity
  doesn't exist yet): `StandardWorkMinutes`=480, `MinimumWorkMinutes`=240, `AutoBreakMinutes`=60,
  `AutoBreakThresholdMinutes`=360, `OvertimeThresholdMinutes`=0. **TODO(US-ATT-005):** move these to
  the shift entity; tenant settings become the fallback for employees with no assigned shift.
- **Status precedence** (stored in `attendance_log.status`, varchar(20)): ANOMALY (system-close OR
  gross span > 16h, FR-7/BR-6/BR-5) > SHORT_DAY (net < minimum, BR-4) > OVERTIME (net beyond
  standard+threshold, BR-3) > COMPLETE. Overtime minutes stored separately in `overtime_minutes`.
- **Auto-break (FR-3/BR-2):** deduct `AutoBreakMinutes` from gross only when gross >
  `AutoBreakThresholdMinutes`. Overtime is computed on the NET (post-break) total.
- **Clock-out is server-side UTC (FR-1), single atomic SaveChanges (NFR-3).** Geo on clock-out is
  optional unless `RequireGeolocation` (reuses the same tenant flag as clock-in; AC-5/FR-6).
- **FR-5 (Redis cache) skipped** — no Redis in this codebase (same as US-ATT-001). **NFR-4 RLS not
  used** — EF global filters + TenantInterceptor.
- **BR-5 auto-clock-out job:** `AutoClockOutJob` (HRM.Api/Jobs), recurring `5 0 * * *` (00:05 UTC).
  Per active/trial tenant, sets tenant context, closes every record left open from a prior UTC day
  by stamping `clock_out` at that clock-in day's 23:59:59 UTC, computes hours with
  `isSystemClosed: true` → always `Status=ANOMALY` for regularization. **Tenant-timezone deferred:**
  uses UTC end-of-day since no tenant-timezone infra exists yet (same deferral as the day-boundary
  note below).
- **Status endpoint extended:** `ClockStatusDto.LastCompleted` (a `ClockOutResultDto`) carries the
  most-recently-CLOSED session summary so the FE can re-render the post-clock-out summary card after
  a reload. Independent of `IsClockedIn`.

## Clock-out API contract (for FE reconciliation)
- `POST /api/v1/attendance/clock-out`, perm `Attendance.CheckIn`, body `{ latitude?, longitude? }`.
  IP/user-agent captured server-side. Success **200** `ApiResponse<ClockOutResultDto>`:
  `{ id, employeeId, clockIn, clockOut, totalWorkMinutes, overtimeMinutes, status }`.
  Failures: 400 (tenant unresolved / geo required-but-missing), 403 (no employee linked),
  404 `no_active_clock_in`.

## US-ATT-003 — Attendance regularization request (forgot clock-in/out)
- **New entity `AttendanceRegularization` (`BaseEntity`).** Employee submits a request to correct a
  missed punch for a past day. Created with `Status=PENDING`; the manager approve/reject and the
  actual `attendance_log` mutation are **US-ATT-004** — submission does NOT touch the log (BR-5).
  Fields per story §7: employee_id, nullable attendance_log_id, date (`DateOnly`/`date`),
  regularization_type, requested_clock_in/out (`timestamptz`, UTC), reason (`text`), status,
  workflow_instance_id (nullable, left null). Type/status constants live in
  `HRM.Domain/Entities/RegularizationType.cs` (`MISSED_CLOCK_IN`/`MISSED_CLOCK_OUT`/`MISSED_BOTH`;
  `PENDING`/`APPROVED`/`REJECTED`/`CANCELLED`).
- **AC-2/BR-5 log linking.** On submit, if an `attendance_log` exists for the date (matched on the
  UTC calendar day of `clock_in`), its id is stored in `attendance_log_id`. The log is **not**
  mutated — proven by a test asserting `clock_out`/`status` stay null after submit.
- **Lookback (AC-3/FR-6/BR-2)** is tenant-configurable: new `AttendanceSettings.RegularizationLookbackDays`
  (default 7). Earliest allowed date = `today - (N-1)` (today inclusive → exactly N eligible days).
  Reject message is the **exact** story string `"Regularization requests can only be submitted for
  the last {N} days."` (code `lookback_exceeded`, 400). "Today" is **UTC** (same deferral as the
  rest of the module — no tenant-timezone infra yet).
- **Duplicate-pending (AC-4/BR-3):** one PENDING request per employee per date. Service `AnyAsync`
  check + PG partial unique index `ix_attendance_regularization_pending_unique` on
  `(tenant_id, employee_id, date) WHERE status = 'PENDING' AND is_deleted = false`. Exact message
  `"A pending regularization request already exists for this date."` (code `duplicate_pending`, 409).
- **Future date (BR-4):** rejected 400 (code `future_date`). **Reason (BR-7):** >= 10 chars after
  trim — validator. **FR-5 time consistency** (clock-in < clock-out, both on the regularized UTC
  day, conditional presence by type) — validator (throws `ValidationException` → 400).
- **Permission decision: ADDED `Attendance.Regularize.Self` to `PermissionCatalog`** (constant
  `Attendance.RegularizeSelf`), the flat `AllPermissions` list, and the **Employee** built-in role
  seed. Unlike ATT-001/002 (which reused the pre-seeded `Attendance.CheckIn` because the story's
  invented name wasn't in the catalog), here I added the literal name the story specifies — it is the
  self-service regularize perm and belongs to the catalog. `DbInitializer` reconciles role
  permissions on startup so the Employee role picks it up; `TenantOwner` gets it via `AllPermissions`.
  Endpoints `POST`/`GET /api/v1/attendance/regularizations` are gated by it.
- **Deferred / placeholder dependencies (do NOT block; flagged for owning stories):**
  - **Approval Workflow Engine (FR-3/AC-1, US-ADM-007):** none exists. `workflow_instance_id` is a
    nullable column, left null; `TODO(US-ADM-007)` in the entity + service. Wires up when the engine
    lands. The "PENDING on submit" record IS the AC-1 deliverable for now.
  - **Notifications (FR-4, US-NTF):** no infra — DEFERRED, not stubbed. `TODO(US-NTF)` in the service
    where the approver notification would fire. FR-4 is **not** in the AC table, so no AC is failed.
  - **Payroll lock (AC-5/FR-7/BR-6, US-PAY):** no Payroll module. Added a minimal **placeholder**
    `PayrollLockPeriod` entity (`tenant_id, start_date, end_date`, inclusive `Covers(date)`). The
    regularization check rejects with the exact story message `"This date falls within a locked
    payroll period. Please contact HR."` (code `payroll_period_locked`, 409). When Payroll lands it
    OWNS this concept (close/lock lifecycle); this entity is the seam. Tests seed a locked period.
  - **RLS (NFR-2) / Redis:** not used — EF global query filters + `TenantInterceptor` (same as the
    rest of the module). NFR-1 (P95) and NFR-4 (mobile) are FE/runtime concerns, untouched here.
- **Migration:** `20260614154902_Attendance_Regularization` (`dotnet ef`, has `[Migration]`). Adds
  `attendance_regularization` + `payroll_lock_period` tables and the `regularization_lookback_days`
  column. EF folded the non-unique `(tenant,emp,date)` lookup index into the partial unique one
  (same columns) — the query still hits an index.

## API contract — regularization (for FE reconciliation)
- `POST /api/v1/attendance/regularizations`, perm `Attendance.Regularize.Self`. Body:
  `{ date: "yyyy-MM-dd", regularizationType: "MISSED_CLOCK_IN"|"MISSED_CLOCK_OUT"|"MISSED_BOTH",
  requestedClockIn?: "HH:mm", requestedClockOut?: "HH:mm", reason: string(>=10) }`. **Contract
  reconciliation (2026-06-14):** the corrected times are nullable **wall-clock "HH:mm" (24h)**
  strings paired with the separate `date`, NOT ISO-UTC instants (the original ISO contract did not
  match what the FE sends). The **backend** combines `date + HH:mm` into the stored UTC
  `timestamptz`. **TODO(tenant-timezone):** the wall-clock is treated as UTC for now — no
  tenant-timezone infra (same deferral as ATT-001/002). Validator checks: HH:mm format, conditional
  presence by type, combined clock-in < clock-out, combined date+time not in the future vs now (UTC).
  Success **201** `ApiResponse<RegularizationDto>`. Failures: 400 (`future_date` /
  `lookback_exceeded` / validation), 403 (no employee / inactive), 409 (`duplicate_pending` /
  `payroll_period_locked`).
- `GET /api/v1/attendance/regularizations`, same perm. **200** `ApiResponse<RegularizationDto[]>`,
  the acting employee's own requests, newest date first (drives the §8 history status pills).
- `RegularizationDto` (response): `{ id, employeeId, attendanceLogId?, date, regularizationType,
  requestedClockIn?, requestedClockOut?, reason, status, createdAt }`. The response
  `requestedClockIn/Out` are the **stored UTC `DateTime?` (full ISO timestamps)**, NOT HH:mm — the FE
  treats them as opaque display strings (it converts/formats for the §8 history pills). Asymmetric by
  design: request is HH:mm, response is ISO.

## US-ATT-005 — Shift management and assignment per employee
- **Entities:** `Shift` (BaseEntity, table `shift`), `ShiftRotationStep` (table `shift_rotation_step`,
  child of a ROTATING shift), `EmployeeShift` (BaseEntity, table `employee_shift`, effective-dated).
  Three shift types in `ShiftType` (`SINGLE`/`ROTATING`/`FLEXIBLE`). `WorkingDays` is a PostgreSQL
  `integer[]` (1=Mon..7=Sun). Rotation is stored as a real child table (NOT jsonb) so FR-7 resolution
  is queryable; each step points at a concrete non-rotating shift for N days within the cycle.
- **Permission decision:** ADDED concrete `Attendance.Shift.Manage` (constant `Attendance.ManageShift`)
  to `PermissionCatalog`. The story names the HR wildcard `Attendance.*.All`, which is not a catalog
  entry — followed the prior ATT-003/004 precedent of adding a concrete string rather than a wildcard.
  Granted to Tenant Admin / HR Manager / HR Officer (the roles already holding `Attendance.Edit`);
  Tenant Owner gets it via `AllPermissions`. All shift endpoints are gated by it.
- **Default shift (BR-1/FR-5):** `DbInitializer` now seeds a per-tenant default ("General Shift",
  Mon–Fri 09:00–17:00, 60-min break, 15-min grace, `IsDefault=true`) and runs an **idempotent
  reconcile across ALL tenants on every startup** (`ReconcileAllTenantsAsync`) so tenants provisioned
  before this release also get one. The same reconcile pass adds **missing built-in role permissions**
  (`ReconcileBuiltInRolePermissionsAsync`) — note: before this story `DbInitializer` did NOT reconcile
  role permissions (it only seeded roles that didn't yet exist), so the ATT-004 catalog comment
  claiming a reconcile was aspirational. It is now real (add-only; never strips bespoke grants).
- **Effective-dating (AC-3/BR-2):** on assign, if the new `effectiveFrom` is after an open
  assignment's start, the open one is closed at `effectiveFrom - 1 day`; if an open assignment starts
  on/after the new date it is soft-deleted (superseded) so there is never an overlapping active pair.
  Future-dated assignment keeps the current shift active until the future date. Bulk path loads all
  open assignments once and one `SaveChanges` (NFR-2, up to 500).
- **Delete (AC-4/FR-6):** blocked when active assignments exist on today; EXACT message
  `"This shift is assigned to {N} employees. Please reassign them before deleting."` code
  `shift_in_use`, 409. {N} counts distinct employees with an active assignment today.
- **Rotation resolution (FR-7/AC-5):** day-index = (date − referenceStartDate) mod cycleLength
  (positive modulo); steps consumed in order by duration; resolves to the concrete step shift.
  SINGLE/FLEXIBLE return themselves. Validator requires step durations to sum to the cycle length.
- **Night shift (§10):** end < start is allowed; only start == end (zero-duration, BR-7) is rejected.
- **Clock-out wiring (OPTIONAL, NOT done):** left the US-ATT-002 `AttendanceSettings` tenant-level
  fallback as the source for clock-out work-hours math. The shift entity carries break/grace/minimum
  but NOT the calculator's StandardWorkMinutes / AutoBreakThresholdMinutes / OvertimeThresholdMinutes,
  so a clean mapping needs extra fields on `Shift`. Deferred with a `TODO(US-ATT-005 follow-up)` in
  `AttendanceCalculator`. Existing ATT-002 tests untouched.
- **Deferred tech:** **No Redis** (NFR-4 shift cache) and **no PostgreSQL RLS** (NFR-3) — tenant
  isolation is EF global query filters + `TenantInterceptor`, same as the rest of the module.
- **Migration:** `20260614164322_Attendance_Shifts` (`dotnet ef`, `[Migration]` attr in Designer).
  Creates `shift` / `shift_rotation_step` / `employee_shift` with partial unique indexes
  `ix_shift_tenant_name_unique` (BR-1) and `ix_shift_tenant_default_unique` (one default/tenant).
- **API contract** (FE + QA built against the same paths): base `/api/v1/attendance`, perm
  `Attendance.Shift.Manage`, `ApiResponse<T>` envelope. `GET shifts`, `POST shifts`,
  `PUT shifts/{id}`, `DELETE shifts/{id}` (204 / 409 shift_in_use), `POST shifts/{id}/clone`
  ("Copy of {orig}"), `POST shifts/{id}/assign` ({ employeeIds, effectiveFrom }), `GET
  employees/{employeeId}/shift?date=`. Times are "HH:mm" strings (null for FLEXIBLE); duplicate name
  → 409 `duplicate_name`. RotationDto kept the pinned shape; step `order` is 0-based.

## Related stories
- `US-ATT-001` — Employee clock-in from browser with optional geolocation (this scaffold)
- `US-ATT-002` — Employee clock-out + work-hours auto-calculation (this story)
- `US-ATT-003` — Regularization request (forgot clock-in/out) — PENDING record + placeholders
- `US-ATT-004` — Manager approve/reject of regularization (will mutate the linked/new log, BR-5)
- `US-ATT-005` — Shift management + assignment (Shift/EmployeeShift entities, rotation resolution)

## Open questions
- Tenant timezone for "today"/day-boundary semantics (deferred; "one open record" sidesteps it for now).
- Clock-out story (closes the open record), lateness/grace-period evaluation (BR-4), dashboard read + cache (FR-6).
- Shift assignment dependency (US-ATT-005) — not yet built; preconditions about shifts are not enforced.
