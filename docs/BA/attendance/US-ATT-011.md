---
id: US-ATT-011
module: Attendance
priority: Must Have
persona: Tenant Admin
status: draft
sprint: backlog
created: 2026-07-15
acceptance_criteria_count: 5
---

# US-ATT-011: Location-Aware Working Calendar & Location-Scoped Attendance Policy

## 1. Description
**As a** Tenant Admin,
**I want to** configure the working calendar and attendance policy per branch/location, with the resolved working-calendar surfaced as a single source of truth consumed by leave, attendance, overtime, and payroll,
**So that** a multi-country tenant (e.g. a Colombo branch on Mon–Fri, a Dubai branch on Sun–Thu, and a French branch on a 4-day week) is handled correctly, while a single-branch tenant needs zero location configuration and simply inherits the tenant defaults.

## 2. Preconditions
- The Tenant Admin is authenticated with a valid tenant context (subdomain resolved).
- The Tenant Admin has the `Attendance.*.All` permission.
- The Attendance module is enabled for the tenant.
- At least one Shift exists (US-ATT-005) and at least one Location exists (US-CHR-007) if per-location configuration is intended. A single-branch tenant may have no locations configured.
- A tenant default shift (`Shift.IsDefault = true`) exists (created during provisioning, per US-ATT-005 BR-1).

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin is editing a Location and active shifts exist in the tenant | They set the Location's `DefaultShiftId` to an active shift and save | The Location persists `DefaultShiftId`; setting it to a shift belonging to another tenant or a soft-deleted/inactive shift is rejected (cross-tenant reference never resolves) |
| AC-2 | An employee has no personal (effective-dated) shift assignment | The system resolves the employee's working days | The working-day set resolves by precedence **Employee shift → Location default shift → Tenant default shift → code default (Mon–Fri, `{1,2,3,4,5}`)** — a multi-branch tenant gets its Location's shift, and a single-branch tenant (empty Location tier) falls through to the tenant default |
| AC-3 | A Tenant Admin defines a location-scoped attendance-policy override for a Location (OT multipliers, thresholds/caps, grace period, geofence) | An employee assigned to that Location is evaluated | The location override applies to that Location's employees; where no override exists, the tenant-level attendance policy applies. At most one override may exist per (tenant, location) |
| AC-4 | A tenant has turned the effective-dated `ExcludeHolidaysFromWorkingDays` policy **on** (it is **off by default** — see BR-9) | Payroll computes the working-days count for a month containing public holidays | A public holiday is **not a working day**: it is excluded from BOTH the pro-ration denominator AND the paid-days numerator (single-basis), so the OT hourly base and the LOP daily rate both rise. With the flag off — the default, and every existing tenant — holidays count as working days and every figure is unchanged |
| AC-5 | The `FteScaledOvertimeBase` policy flag is off (default) | A part-time employee's overtime hourly base is computed | The OT hourly base is NOT scaled by FTE; when the flag is on, the part-timer's OT hourly base scales by their FTE (`standardHours * Fte`) |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: The system SHALL add a nullable `Location.DefaultShiftId` (FK → `Shift`) exposed on Location create/edit, validated to reference an active, same-tenant shift.
- FR-2: The system SHALL resolve an employee's working-day set through a four-tier chain — Employee effective-dated shift → Location default shift → Tenant default shift (`Shift.IsDefault`) → code default (Mon–Fri) — implemented in a single resolver (`ShiftScheduleResolver`) that is the sole source of truth for "is date D a working day for employee E."
- FR-3: The resolver SHALL be batched (dictionary lookups keyed by employee/location) with no per-employee round-trips.
- FR-4: The system SHALL support a nullable-`LocationId` override layer over the tenant `AttendanceSettings`, resolved by the same four-tier precedence (employee's Location override → tenant default), carrying OT multipliers, thresholds/caps, grace period, geofence coordinates/radius, and the two policy flags below.
- FR-5: The system SHALL add an `ExcludeHolidaysFromWorkingDays` flag (default **false**) governing whether public holidays reduce the payroll working-days count. It SHALL live on a **per-tenant, effective-dated** `TenantPayrollCalendarPolicy` (mirroring `TenantFnFPolicy`): payroll reads the version whose `EffectiveFrom` is on or before the pay period, so a change applies from the tenant's chosen date forward and NEVER rewrites a completed run. When no policy row exists the code-default is **false**.
- FR-6: The system SHALL add a `FteScaledOvertimeBase` flag (default **false**) governing whether a part-timer's OT hourly base scales by FTE.
- FR-7: Leave, attendance, overtime, and payroll SHALL all consume this resolved calendar/policy (and the unified location-scoped `IHolidayProvider`) rather than hardcoding a weekday, weekend, or fixed constant.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: Working-day / policy resolution for a batch of up to 5,000 employees SHALL complete within 5 seconds (no N+1 queries).
- NFR-2: PostgreSQL RLS and EF Core global query filters SHALL enforce tenant isolation on `Location.DefaultShiftId`, the `AttendanceSettings` location-override rows, and every cross-entity FK introduced here; a cross-tenant reference SHALL never resolve.
- NFR-3: Resolution SHALL be deterministic and auditable — the tier that supplied the result (employee/location/tenant/code) is derivable for a given employee and date.
- NFR-4: Configuration reads SHALL be cache-friendly (shift/location/policy lookups may be cached per tenant with invalidation on write).

## 6. Business Rules
- BR-1: A cross-tenant reference (`DefaultShiftId`, override `LocationId`) never resolves — tenant isolation is enforced at the query layer, so a foreign id simply is not found.
- BR-2: The resolver is the single source of truth for the working calendar; no consuming module (leave, attendance, overtime, payroll) may hardcode a weekday or weekend.
- BR-3: Precedence is strict and total: Employee override wins over Location, Location over Tenant, Tenant over the code default (Mon–Fri).
- BR-4: A single-branch tenant leaves the Location tier empty and behaves exactly as before (tenant defaults untouched) — location awareness is additive, never required.
- BR-5: At most one `AttendanceSettings` override row may exist per (tenant, location); the tenant default is the row with a null `LocationId`.
- BR-6: `ExcludeHolidaysFromWorkingDays` defaults **off**; `FteScaledOvertimeBase` defaults off. Both are backward-compatible by default.
- BR-9: **`ExcludeHolidaysFromWorkingDays` is off by default and effective-dated — a deliberate money decision, not an oversight.**
  Defaulting it ON would have raised the OT hourly base (and the LOP daily rate) for **every existing tenant on their next payroll
  run**, which contradicts the F&F precedent that a policy change applies next-cycle and is NEVER retroactive (see
  `TenantFnFPolicy`). A tenant opts in by creating a policy version with a chosen `EffectiveFrom`; runs before that date keep the
  behaviour they were computed under.
- BR-10: When the flag is on, a public holiday is **not a working day** — it is excluded from the pro-ration **denominator** AND
  the paid-days **numerator** alike. Excluding it from the denominator only would break the single-basis pro-ration invariant
  (`PayrollRunProcessor`: "shift/shift … calendar/calendar") and over-pay mid-month joiners/leavers.
- BR-11: Holidays are **location-scoped** (`IHolidayProvider(employee.LocationId)`, the same source overtime uses per BUG-286):
  a holiday at one Location must not shrink another Location's working days.

## 7. Data Requirements
**Location (added column):**
| Field | Type | Notes |
|-------|------|-------|
| default_shift_id | uuid | Nullable FK → shift; same-tenant, active; delete behaviour Restrict |

**attendance_settings (location-override layer + new flags):**
| Field | Type | Notes |
|-------|------|-------|
| location_id | uuid | Nullable; null row = tenant default, set row = that location's override; unique `(tenant_id, location_id)` where not deleted |
| exclude_holidays_from_working_days | boolean | Default true |
| fte_scaled_overtime_base | boolean | Default false |
| (existing) | — | OT multipliers, thresholds/caps, grace period, geofence carried per row |

**Resolver output:** per-employee working-day set (ISO days 1=Mon..7=Sun) for a given `asOf` date, plus the effective `AttendanceSettings` for that employee.

## 8. UI/UX Notes
- On the Location edit form (US-CHR-007), add a "Default Shift" searchable dropdown (active same-tenant shifts only) with a helper note: "Employees at this location without a personal shift use this working calendar."
- Provide an attendance-policy override editor scoped to a Location (OT multipliers, thresholds/caps, grace period, geofence, plus the two flags) with a clear "Inherits tenant default" state when no override exists and a "Reset to tenant default" action.
- Surface the two flags as labelled toggles with inline explanations of their payroll/OT impact and their default state.
- Single-branch tenants should see the tenant-level policy editor unchanged; the location override UI is only relevant once more than one location exists.

## 9. Dependencies
- US-ATT-005 (Shift management) — supplies the shift model and the tenant default shift; `Location.DefaultShiftId` is the Location tier of shift resolution.
- US-CHR-007 (Manage office locations) — Location carries `DefaultShiftId` and the optional probation override.
- US-CHR-013 (Employee FTE & work arrangement) — supplies `Employee.Fte` consumed by the `FteScaledOvertimeBase` flag.
- US-LV-007 (Holiday calendar) — the location-scoped holiday calendar feeds the unified `IHolidayProvider` used by the denominator flag.
- Enables the fixes tracked as BUG-284 (leave work-week), BUG-285 (OT weekend basis), and BUG-286 (OT holiday location scope).

## 10. Assumptions & Constraints
- The four-tier chain reuses the proven tax-country resolution pattern (`Employee.LocationId → Location.CountryCode → Tenant.DefaultCountryCode → fallback`); no new resolution mechanism is invented.
- `Employee.LocationId` is already wired (BUG-113, PR #261); `ShiftScheduleResolver` already exists and is a live payroll dependency (PR #282) — this story extends both, it does not build them from scratch.
- Migrations are CLI-generated only; every new tenant-scoped column/table adds its dormant RLS `tenant_isolation` policy in the same migration.
- Out of scope: per-location currency, multi-country payroll, and configurable pay frequency (separate decisions).

## 11. Test Hints
- Multi-branch: a Dubai (Sun–Thu) employee with no personal shift resolves Sunday as a workday and Friday as weekend; a Colombo (Mon–Fri) employee in the same tenant resolves the opposite.
- Single-branch fall-through: a tenant with no location config resolves the tenant default (Mon–Fri) — proves the empty Location tier.
- 4-day EU tenant (`{1,2,3,4}`): working-day counts are correct.
- Location policy override: a Dubai override with `WeekendOvertimeMultiplier = 3.0` resolves for a Dubai employee while a Colombo employee still gets the tenant default 2.0.
- `ExcludeHolidaysFromWorkingDays`: a month with 2 public holidays reduces the payroll denominator by 2 when on; holidays count when off.
- `FteScaledOvertimeBase`: a 0.5-FTE employee's OT base equals a full-timer's when off, and is scaled when on.
- Tenant isolation: setting `DefaultShiftId` or an override `LocationId` to another tenant's row is rejected; a cross-tenant reference never resolves (Critical Rule #1).
- One-override-per-location: creating a second `AttendanceSettings` override for the same (tenant, location) is rejected.
