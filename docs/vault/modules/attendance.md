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

## Related stories
- `US-ATT-001` — Employee clock-in from browser with optional geolocation (this scaffold)

## Open questions
- Tenant timezone for "today"/day-boundary semantics (deferred; "one open record" sidesteps it for now).
- Clock-out story (closes the open record), lateness/grace-period evaluation (BR-4), dashboard read + cache (FR-6).
- Shift assignment dependency (US-ATT-005) — not yet built; preconditions about shifts are not enforced.
