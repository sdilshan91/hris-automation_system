# Design Spec — Tenant + Location Configurable Working Calendar & Policy

**Date:** 2026-07-14
**Status:** Approved (design) — implementation plan + BA authoring to follow
**Author:** orchestrator (with user)

## 1. Problem

Several HR/payroll behaviours that legitimately vary by **country/branch** are currently
hardcoded, and — worse — two modules ignore configuration that already exists. A single
tenant can run a Colombo branch (Mon–Fri, Poya holidays), a Dubai branch (Sun–Thu, Fri–Sat
weekend), and a French branch (4-day week) at the same time, but today:

- **Leave day-counting hardcodes Mon–Fri** (`WorkingDaysCalculator.DefaultWorkWeek`) and never
  consults the shift system → wrong balance deduction, wrong half-day gate, wrong preview for
  any non-Mon–Fri population.
- **Overtime hardcodes Sat/Sun** for the weekend multiplier (`OvertimeMultiplierResolver:36`)
  and its holiday check ignores `LocationId` → Friday OT underpaid, Sunday OT overpaid, and a
  New-York holiday grants a London employee the holiday multiplier.
- **No LOCATION tier for the working calendar.** `Shift.IsDefault` is one-per-tenant, so a
  multi-branch tenant cannot express two default work-weeks without hand-assigning a shift to
  every employee.
- **Values promised-but-unbuilt:** fiscal-year-start (`Tenant.FiscalYearStartMonth`) is read by
  nothing; part-time FTE proration has a code seam but no `Employee.Fte` field; probation period
  is hardcoded 90 days despite US-CHR-009 BR-6 promising per-tenant config; the
  `Employee.LocationId` link itself was never wired (BUG-113).

Goal: make the working calendar and its dependent policies **tenant-configurable with optional
location and employee overrides**, reduce hardcoded values to genuine engineering invariants, and
fix the money/entitlement bugs that the missing configuration is currently causing.

## 2. Decisions (locked with user)

| # | Decision | Choice |
|---|----------|--------|
| D1 | Resolution model | **Four-tier chain:** Employee override → Location default → Tenant default → code default. Same shape as the existing tax-country chain. |
| D2 | Single vs multi-branch | **Both must work.** Single-branch tenants use tenant defaults untouched (location tier empty); multi-branch tenants override per Location. |
| D3 | Location working-calendar anchor | **`Location.DefaultShiftId`** (FK → `Shift`), not a bare `WorkingDays int[]` — reuses the whole shift model (hours, breaks, grace). |
| D4 | Scope | **One epic spec, four ordered phases.** SP1 (foundation) gates SP2–SP4. Written as one document but implementable/shippable phase-by-phase. |
| D5 | Eligibility/policy scope | Probation period & leave entitlement = **Tenant default + optional Location override** (nullable `LocationId` tier). |
| D6 | FTE-scaled OT base | **Configurable** policy flag (Tenant + Location), default **off**. |
| D7 | Holidays in payroll working-days denominator | **Configurable** policy flag (Tenant + Location), default **on** (exclude holidays; aligns with leave). |
| D8 | Out of scope | Statutory per-country tax catalog (separate decision), configurable pay frequency, per-location currency / multi-country payroll. |

## 2a. Current-state corrections (verified against merge history 2026-07-14)

Two items this design originally treated as unbuilt are **already done** — the plan is scoped
around these facts:

- **Employee↔Location link — DONE (BUG-113, PR #261, 2026-07-12).** `CreateEmployeeCommand`,
  its handler, `EmployeeDto`, and `UpdateEmployeeProfileRequest` all carry `Guid? LocationId`.
  **Phase 1 no longer needs to wire this** — location-awareness is already unblocked at the
  employee level. (Follow-up ISSUE at TEST-FINDINGS ~line 6164: bulk-import still writes only the
  legacy free-text `Location` — parked, product decision.)
- **Payroll pro-rata is already shift-aware (PR #282).** `PayrollRunProcessor.ProRataPaidDays`
  counts shift working-days via `ShiftScheduleResolver` (both numerator and denominator). So the
  `ShiftScheduleResolver` already **exists and is a live payroll dependency** — Phase 1 *extends*
  it with the Location tier, it does not build it from scratch.
- **Fiscal-year-in-reports partly tracked (ISSUE-176, LOW).** Statutory YTD reports already have
  a filed finding for ignoring `FiscalYearStartMonth`; Phase 4 supersedes it and extends the fix
  to the leave engine.

Net effect: the foundation is ~half-built. The genuinely-remaining work is the **Location tier**,
the **leave + overtime consumers** (still hardcoded), the **holiday-denominator flag**, the new
**employee attributes**, and the **fiscal/probation** wiring.

## 3. Core principle

Every calendar/policy value resolves through **one** chain, read by **every** module — no module
hardcodes a weekday, weekend, or fixed constant again:

```
Employee override  →  Location default  →  Tenant default  →  code default
```

This extends the proven tax-country pattern
(`Employee.LocationId → Location.CountryCode → Tenant.DefaultCountryCode → fallback`) rather than
inventing a new mechanism. `ShiftScheduleResolver` becomes the single source of truth for "is
date D a working day for employee E," and a parallel location-override layer over
`AttendanceSettings` is the single source of truth for attendance/OT/payroll policy.

**Rejected alternatives:** bare `Location.WorkingDays int[]` (can't carry hours/breaks/grace); a
new calendar entity (unnecessary — `Shift` already models it); a generic EAV settings table
(the codebase deliberately uses typed columns).

## 4. Phases

### Phase 1 — Foundation (prerequisite; gates the rest)

- ~~Wire Employee↔Location link~~ — **already done (BUG-113, PR #261).** `Employee.LocationId`
  flows through create/edit; no work here.
- **Add `Location.DefaultShiftId`** (nullable FK → `Shift`), with CRUD + validation (shift must
  belong to same tenant, be active).
- **Extend the existing `ShiftScheduleResolver`** (already a live payroll dependency, PR #282) to
  a four-tier chain: `EmployeeShift` (effective-dated) → `Location.DefaultShiftId` → tenant
  `Shift.IsDefault` → code default (Mon–Fri). Keep it batched (dictionary lookups keyed by
  employee/location — no N+1). This is the linchpin every consumer reads.

### Phase 2 — Consumer correctness (rides on Phase 1; fixes money/entitlement bugs)

- **Leave** (`LeaveRequestService`): day-count, half-day gate, and balance preview consume the
  resolver instead of `DefaultWorkWeek`.
- **Overtime** (`OvertimeMultiplierResolver`): weekend-vs-weekday multiplier decided by the
  resolved working-day set, not `Sat/Sun`.
- **Overtime holidays** (`OvertimeService.IsPublicHolidayAsync`): use the location-scoped
  `IHolidayProvider` instead of the unfiltered query.
- **Unify holiday lookups:** leave, attendance, overtime, and payroll all call
  `IHolidayProvider(employee.LocationId)` — one provider, no divergent inline queries.
- **Location-scoped attendance/payroll policy:** introduce a nullable-`LocationId` override layer
  over the tenant `AttendanceSettings`, resolved by the four-tier chain. This carries the OT
  multipliers/thresholds/caps, grace period, geofence, plus the two new flags below.
- **`ExcludeHolidaysFromWorkingDays`** flag (D7, default on): controls whether public holidays
  count in the payroll working-days denominator (`workingDays * 8` hourly-rate base). Default
  aligns payroll with leave (holidays excluded).

### Phase 3 — Employee attributes

- **`Employee.Fte`** (decimal, default `1.0`): wire into `LeaveEntitlementEngine.CalculateProRata`
  (replaces the hardcoded `1.0m` at all three call sites) and into the OT hourly-rate base when
  D6 is enabled.
- **`FteScaledOvertimeBase`** flag (D6, default off): when on, the OT hourly base uses
  `standardHours * Fte` for part-timers.
- **`Employee.WorkArrangement`** enum (OnSite / Hybrid / Remote): Remote ⇒ **geofence-exempt** at
  clock-in (today geofencing is a single tenant coordinate, so remote workers cannot clock in).

### Phase 4 — Calendar/fiscal & policy location-scoping

- **Wire `Tenant.FiscalYearStartMonth`** into the leave-year boundary — accrual job, year-end
  job, carry-forward expiry, and pro-rata — plus leave/attendance reporting. Leave-year becomes
  calendar-or-fiscal per tenant, fulfilling US-LV-002/006/008.
- **Probation period:** read from **Tenant default + optional Location override**
  (`Tenant.ProbationPeriodDays`, `Location.ProbationPeriodDays?`) instead of hardcoded 90 days
  in `EmployeeStatusService`.
- **Entitlement & probation policy:** add a nullable `LocationId` tier to their resolution
  precedence (Location override wins over tenant), per D5.

## 5. Data-model changes (summary)

| Entity | Change |
|---|---|
| `Employee` | wire `LocationId` (BUG-113); add `Fte` (decimal, default 1.0), `WorkArrangement` (enum) |
| `Location` | add `DefaultShiftId` (FK→Shift, nullable); add `ProbationPeriodDays?` (nullable override) |
| `Tenant` | add `ProbationPeriodDays` (default 90); **use** `FiscalYearStartMonth` (already exists) |
| `AttendanceSettings` | add location-override layer (nullable `LocationId`); add `FteScaledOvertimeBase`, `ExcludeHolidaysFromWorkingDays` flags |
| `LeaveEntitlementRule` | add nullable `LocationId` to resolution precedence |
| `ShiftScheduleResolver` | four-tier resolution (Employee → Location → Tenant → code default) — the linchpin |
| `IHolidayProvider` | become the sole holiday lookup for leave/attendance/OT/payroll |

Migrations: **CLI-generated only** (`dotnet ef migrations add …`) — never hand-written (repo rule).
Every new tenant_id column/table adds its dormant RLS `tenant_isolation` policy in the same
migration (RLS rule).

## 6. Values that STAY fixed (explicit non-goals)

Rounding mode (2dp away-from-zero) & `numeric(18,2)` precision; ISO day encoding (1=Mon..7=Sun);
the `"BASIC"` component code; synthetic LOP/adjustment/OT component GUIDs; the ~13-month YTD
lookback window. These are engineering/accounting invariants — making them configurable is
speculative flexibility with no business driver.

## 7. Testing strategy

Postgres integration tests (InMemory masks Postgres — repo lesson), each asserting correct
money/entitlement:

- **Gulf tenant** (Sun–Thu shift, Fri–Sat weekend): leave balance deduction + OT weekend
  multiplier both correct.
- **4-day EU tenant** (`{1,2,3,4}`): working-day counts correct.
- **Single-branch tenant** (no location config): everything falls through to tenant default —
  proves the empty-location tier.
- **Multi-branch holiday**: a NY-only holiday does NOT grant a London employee holiday OT.
- **Part-timer** (`Fte = 0.5`): leave proration halved; OT base scaled only when
  `FteScaledOvertimeBase` on.
- **Remote employee**: geofence-exempt clock-in succeeds.
- **Fiscal-year tenant** (Apr–Mar): leave-year boundary, accrual, and carry-forward expiry
  anchor to April.

### 7.1 Validation coverage (must be in the TCs, not just behaviour tests)

Every new configurable field gets explicit **negative/validation** test cases alongside the
behaviour tests — these are where a config-heavy change leaks bugs:

| Field | Validation rules to test |
|---|---|
| `Employee.LocationId` | must reference an **existing, active, same-tenant** Location; null allowed; cross-tenant Location id → 400/tenant-isolation reject. |
| `Location.DefaultShiftId` | must be a **same-tenant, active** Shift; null allowed; setting a soft-deleted/other-tenant shift → reject. |
| `Employee.Fte` | range `0 < Fte <= 1.0`; reject `0`, negatives, `> 1.0`; precision (2dp) enforced. |
| `Employee.WorkArrangement` | only defined enum values (OnSite/Hybrid/Remote); unknown → reject. |
| `Tenant.ProbationPeriodDays` / `Location.ProbationPeriodDays?` | positive integer within sane bounds; location override null → falls back to tenant; reject `0`/negative/absurd. |
| `AttendanceSettings` location override | override `LocationId` must be same-tenant; OT multipliers `>= 1.0`; thresholds/caps non-negative; only one override row per (tenant, location). |
| `LeaveEntitlementRule.LocationId` | same-tenant; null = tenant-wide; location + other dimensions resolve by documented precedence. |
| `FiscalYearStartMonth` | integer `1..12` (already validated — assert it still is after wiring it into leave). |
| Shift `WorkingDays` | ISO days `1..7`, non-empty, no duplicates (assert existing validation holds under new callers). |

Tenant-isolation negative tests are mandatory for **every** new cross-entity FK
(`LocationId`, `DefaultShiftId`, override `LocationId`) — a cross-tenant reference must never
resolve (Critical Rule #1).

## 8. BA mapping (produced from this spec)

- **New USs:** (1) location-aware working calendar (`Location.DefaultShiftId` + four-tier
  resolver + location-scoped attendance policy); (2) Employee FTE + work-arrangement.
- **Update USs:** US-ATT-005 (location default shift), US-ATT-006 (OT weekend/holiday basis +
  location-scoped multipliers + FTE-scaled base flag), US-ATT-008/001 (location grace/geofence,
  remote exemption), US-CHR-001/002 (LocationId wiring, FTE, work-arrangement), US-CHR-007
  (`DefaultShiftId`, probation override), US-CHR-009 (configurable probation), US-LV-002/003/006/008
  (resolver-driven work-week, fiscal leave-year, location entitlement tier).
- **Implementation-gap findings → `docs/QA/TEST-FINDINGS.md`:** BUG-113 (Employee-Location link),
  leave work-week ignores shifts, OT weekend basis, OT holiday location scope, fiscal-year-start
  unwired, probation-period hardcode, FTE proration unbuilt.

## 9. Sequencing

```
SP1 (foundation)  ──►  SP2 (consumers + location policy)  ──►  SP4 (fiscal/probation location tier)
                  └──►  SP3 (employee FTE/arrangement)  ──────────┘
```

SP1 is the hard gate. SP2 and SP3 can proceed in parallel after SP1. SP4 depends on both the
resolver (SP1) and the FTE/location tiers (SP2/SP3).
