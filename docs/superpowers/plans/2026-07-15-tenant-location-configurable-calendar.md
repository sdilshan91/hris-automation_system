# Tenant + Location Configurable Working Calendar & Policy — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the working calendar and its dependent policies tenant-configurable with optional Location and Employee overrides, and fix the leave/overtime money-and-entitlement bugs caused by today's hardcoded Mon–Fri / Sat–Sun assumptions.

**Architecture:** One four-tier resolution chain — **Employee → Location → Tenant → code default** — read by every module. `ShiftScheduleResolver` (already a live payroll dependency) is extended with the Location tier and becomes the single source of truth for "is date D a working day for employee E." A nullable-`LocationId` override layer over `AttendanceSettings` does the same for attendance/OT/payroll policy. `IHolidayProvider(locationId)` becomes the sole holiday lookup.

**Tech Stack:** ASP.NET Core 10, EF Core (PostgreSQL, snake_case), MediatR CQRS, FluentValidation, xUnit integration tests (Postgres via real EF — **not** InMemory for calendar math), Angular 20 (FE touch-ups only where a new field must be settable).

## Global Constraints

- **Migrations are CLI-generated only:** `dotnet ef migrations add <Name> --project HRM.Infrastructure --startup-project HRM.Api`. Never hand-write a migration file.
- **Every new tenant-scoped column/table adds its dormant RLS `tenant_isolation` policy in the same migration** (enforced by `RlsIsolationPostgresTests`).
- **Tenant isolation is non-negotiable:** every new cross-entity FK (`Location.DefaultShiftId`, override `LocationId`, `LeaveEntitlementRule.LocationId`) must reject a cross-tenant reference. A tenant-isolation negative test is mandatory per FK.
- **Never weaken/skip/delete a test to go green** (test-integrity-guard hook).
- **Calendar/money tests run on real Postgres** (InMemory masks the bug — repo lesson). Headless FE: `npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox`.
- **Values that stay fixed (do not touch):** rounding mode (2dp away-from-zero), `numeric(18,2)`, ISO day encoding (1=Mon..7=Sun), the `"BASIC"` component code, synthetic LOP/OT GUIDs.
- **Default policy values:** `FteScaledOvertimeBase` default **false**; `ExcludeHolidaysFromWorkingDays` default **true**; `Employee.Fte` default **1.0**; `Tenant.ProbationPeriodDays` default **90**.

**Dependency-verified current state (2026-07-14):** Employee↔Location link is DONE (BUG-113, #261). `ShiftScheduleResolver` EXISTS and is used by payroll (#282). `Shift.WorkingDays` already expresses Sun–Thu (`{7,1,2,3,4}`) and 4-day (`{1,2,3,4}`) weeks. This plan extends, it does not build from scratch.

---

## Phase 1 — Foundation (gates Phases 2–4)

### Task 1.1: Add `Location.DefaultShiftId` (entity + EF config + migration)

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/Location.cs`
- Modify: `src/backend/HRM.Infrastructure/Persistence/Configurations/LocationConfiguration.cs`
- Create (via CLI): migration `Location_DefaultShiftId`
- Test: `src/backend/HRM.Tests/Integration/LocationDefaultShiftTests.cs`

**Interfaces:**
- Produces: `Location.DefaultShiftId` (`Guid?`), nav `Location.DefaultShift` (`Shift?`).

- [ ] **Step 1: Write the failing test** — a Location can persist a same-tenant `DefaultShiftId`, and the FK is nullable.

```csharp
[Fact]
public async Task Location_persists_DefaultShiftId_to_a_same_tenant_shift()
{
    await using var ctx = NewPostgresContext(TenantA);
    var shift = await SeedShiftAsync(ctx, TenantA, name: "Gulf Sun-Thu", workingDays: new[] {7,1,2,3,4});
    var loc = new Location { Name = "Dubai", TimeZone = "Asia/Dubai", DefaultShiftId = shift.Id };
    ctx.Locations.Add(loc);
    await ctx.SaveChangesAsync();

    var reloaded = await ctx.Locations.Include(l => l.DefaultShift).SingleAsync(l => l.Id == loc.Id);
    Assert.Equal(shift.Id, reloaded.DefaultShiftId);
    Assert.Equal(new[] {7,1,2,3,4}, reloaded.DefaultShift!.WorkingDays);
}
```

- [ ] **Step 2: Run it — expect FAIL** (`Location` has no `DefaultShiftId`). `dotnet test --filter LocationDefaultShiftTests`
- [ ] **Step 3: Add the property + nav to `Location.cs`:**

```csharp
/// <summary>Optional per-location default shift; the Location tier of the working-calendar
/// resolution chain (Employee → Location → Tenant → code default). Null = fall through to tenant default.</summary>
public Guid? DefaultShiftId { get; set; }
public Shift? DefaultShift { get; set; }
```

- [ ] **Step 4: Configure the FK in `LocationConfiguration.cs`** (Restrict delete — a shift in use by a location must not cascade-delete the location):

```csharp
builder.HasOne(l => l.DefaultShift)
    .WithMany()
    .HasForeignKey(l => l.DefaultShiftId)
    .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5: Generate the migration (CLI only), add the dormant RLS policy for the new column's table if not already present (locations already has one — verify), then update DB:**

```bash
cd src/backend
dotnet ef migrations add Location_DefaultShiftId --project HRM.Infrastructure --startup-project HRM.Api
dotnet ef database update --project HRM.Infrastructure --startup-project HRM.Api
```

- [ ] **Step 6: Run test — expect PASS. Then commit.**

```bash
git add src/backend/HRM.Domain/Entities/Location.cs src/backend/HRM.Infrastructure/Persistence/Configurations/LocationConfiguration.cs src/backend/HRM.Infrastructure/Persistence/Migrations/*Location_DefaultShiftId* src/backend/HRM.Tests/Integration/LocationDefaultShiftTests.cs
git commit -m "feat(core-hr): add Location.DefaultShiftId (working-calendar location tier)"
```

### Task 1.2: Extend `ShiftScheduleResolver` to the four-tier chain

**Files:**
- Modify: `src/backend/HRM.Infrastructure/Services/ShiftScheduleResolver.cs`
- Test: `src/backend/HRM.Tests/Integration/ShiftScheduleResolverLocationTierTests.cs`

**Interfaces:**
- Consumes: `Location.DefaultShiftId` (Task 1.1); `Employee.LocationId` (existing).
- Produces: `ResolveWorkingDaySetsAsync(IReadOnlyList<Guid> employeeIds, DateOnly asOf)` now resolves per employee in precedence **EmployeeShift → Location.DefaultShift → tenant `Shift.IsDefault` → code default (Mon–Fri = `{1,2,3,4,5}`)**. Signature unchanged; behaviour extended. Batched: one query for employee shifts, one for the employees' `LocationId`s, one for those locations' `DefaultShiftId`s, one for the tenant default — no per-employee round-trips.

- [ ] **Step 1: Write the failing test** — an employee with no personal shift, at a Location whose `DefaultShiftId` is Sun–Thu, resolves to Sun–Thu (not the tenant Mon–Fri default).

```csharp
[Fact]
public async Task Resolver_uses_location_default_shift_when_employee_has_no_personal_shift()
{
    await using var ctx = NewPostgresContext(TenantA);
    var tenantDefault = await SeedShiftAsync(ctx, TenantA, "General", new[]{1,2,3,4,5}, isDefault:true);
    var gulf = await SeedShiftAsync(ctx, TenantA, "Gulf", new[]{7,1,2,3,4});
    var dubai = await SeedLocationAsync(ctx, TenantA, "Dubai", defaultShiftId: gulf.Id);
    var emp = await SeedEmployeeAsync(ctx, TenantA, locationId: dubai.Id); // no EmployeeShift
    var resolver = new ShiftScheduleResolver(ctx);

    var sets = await resolver.ResolveWorkingDaySetsAsync(new[]{emp.Id}, new DateOnly(2026,7,1));

    // Friday(5)=weekend for Gulf, Sunday(7)=workday
    Assert.DoesNotContain(5, sets[emp.Id]);   // Friday not a working day
    Assert.Contains(7, sets[emp.Id]);         // Sunday IS a working day
}

[Fact]
public async Task Resolver_falls_through_to_tenant_default_when_location_has_no_default_shift()
{
    // single-branch tenant path: location tier empty → tenant Mon-Fri default
    ...
    Assert.Contains(5, sets[emp.Id]);   // Friday IS a working day (Mon-Fri)
    Assert.DoesNotContain(7, sets[emp.Id]);
}
```

- [ ] **Step 2: Run — expect FAIL** (resolver ignores Location today).
- [ ] **Step 3: Insert the Location tier** between the EmployeeShift lookup and the tenant-default fallback. Sketch:

```csharp
// after building employeeShiftByEmp (existing), before tenant-default fallback:
var locIdByEmp = await _ctx.Employees
    .Where(e => employeeIds.Contains(e.Id))
    .Select(e => new { e.Id, e.LocationId })
    .ToDictionaryAsync(x => x.Id, x => x.LocationId);

var locDefaultShiftId = await _ctx.Locations
    .Where(l => l.DefaultShiftId != null && locIdByEmp.Values.Contains(l.Id))
    .Select(l => new { l.Id, l.DefaultShiftId })
    .ToDictionaryAsync(x => x.Id, x => x.DefaultShiftId!.Value);

// per employee resolution order:
//   1. employeeShiftByEmp[empId]                       (existing effective-dated pick)
//   2. locDefaultShiftId[ locIdByEmp[empId] ]          (NEW location tier)
//   3. tenantDefaultShift (Shift.IsDefault)            (existing)
//   4. CodeDefaultWorkingDays => new[]{1,2,3,4,5}      (existing empty-set behaviour REPLACED, see note)
```

- [ ] **Step 3a: Change the final fallback** from "empty set = every calendar day" to the explicit Mon–Fri code default `{1,2,3,4,5}`, so an unconfigured employee is treated Mon–Fri (matches the spec's code-default tier and the old leave default). Add a `public static readonly int[] CodeDefaultWorkingDays = {1,2,3,4,5};`.
- [ ] **Step 4: Run — expect PASS (both tests).**
- [ ] **Step 5: Commit.**

```bash
git commit -am "feat(attendance): ShiftScheduleResolver four-tier working-day resolution (Employee→Location→Tenant→code)"
```

### Task 1.3: Expose `DefaultShiftId` in Location CRUD + validator

**Files:**
- Modify: `src/backend/HRM.Application/Features/Locations/DTOs/LocationDto.cs` (request + response)
- Modify: `src/backend/HRM.Application/Features/Locations/Commands/*` (Create/Update handlers)
- Create: `src/backend/HRM.Application/Features/Locations/Validators/LocationDefaultShiftValidator.cs`
- Modify: `src/backend/HRM.Infrastructure/Services/LocationService.cs`
- Test: add to `LocationDefaultShiftTests.cs`

**Interfaces:**
- Consumes: `Location.DefaultShiftId`.
- Produces: `CreateLocationRequest.DefaultShiftId`, `UpdateLocationRequest.DefaultShiftId`, `LocationDto.DefaultShiftId`.

- [ ] **Step 1: Write the failing validation test** — setting `DefaultShiftId` to another tenant's shift (or a soft-deleted shift) is rejected.

```csharp
[Fact]
public async Task Update_location_with_cross_tenant_DefaultShiftId_is_rejected()
{
    var otherTenantShift = await SeedShiftAsync(_ctxB, TenantB, "B-shift", new[]{1,2,3});
    var resp = await UpdateLocationAsync(TenantA, locId, new { DefaultShiftId = otherTenantShift.Id });
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode); // or 404 — cross-tenant never resolves
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Add `Guid? DefaultShiftId` to the request/response DTOs and wire it through Create/Update handlers + `LocationService`.**
- [ ] **Step 4: Add the validator** — the shift must exist, belong to the current tenant (query respects the global tenant filter, so a cross-tenant id simply won't be found), and be active:

```csharp
RuleFor(x => x.DefaultShiftId).MustAsync(async (id, ct) =>
        id == null || await _db.Shifts.AnyAsync(s => s.Id == id && s.IsActive, ct))
    .WithMessage("DefaultShiftId must reference an active shift in this tenant.");
```

- [ ] **Step 5: Run — expect PASS. Add a positive test (same-tenant active shift accepted). Commit.**

```bash
git commit -am "feat(core-hr): Location CRUD exposes DefaultShiftId with same-tenant/active validation"
```

**PHASE 1 GATE:** `dotnet build HRM.sln` green · new Postgres tests green · `dotnet test` full suite green. Only then proceed.

---

## Phase 2 — Consumer correctness + location policy

### Task 2.1: Route leave day-counting through the resolver (fixes BUG-284)

**Files:**
- Modify: `src/backend/HRM.Infrastructure/Services/LeaveRequestService.cs:132,142,353`
- Modify: `src/backend/HRM.Application/Features/LeaveRequests/WorkingDaysCalculator.cs` (accept a resolved work-week; keep `DefaultWorkWeek` only as the code-default constant)
- Test: `src/backend/HRM.Tests/Integration/LeaveWorkingDaysLocationTests.cs`

**Interfaces:**
- Consumes: `ShiftScheduleResolver.ResolveWorkingDaySetsAsync` (Task 1.2), `IHolidayProvider` (existing).
- Produces: leave day-count and half-day gate now honour the employee's resolved working-day set.

- [ ] **Step 1: Write the failing test** — a Sun–Thu employee taking Sun–Thu leave is deducted 5 days (not 5 counting Fri and skipping Sun), and a half-day request on Sunday is accepted while Friday is rejected.

```csharp
[Fact]
public async Task Gulf_employee_leave_counts_shift_working_days_not_mon_fri()
{
    // Dubai Sun-Thu employee, 1 week leave Sun..Thu (5 workdays), no holidays
    var days = await RequestLeaveAndGetDeductedDaysAsync(gulfEmp, from: sunday, to: thursday);
    Assert.Equal(5m, days);                       // all 5 are workdays
    // Friday half-day (their weekend) → rejected
    Assert.Equal(HttpStatusCode.BadRequest, (await HalfDayAsync(gulfEmp, friday)).StatusCode);
    // Sunday half-day (a workday) → accepted
    Assert.Equal(HttpStatusCode.OK, (await HalfDayAsync(gulfEmp, sunday)).StatusCode);
}
```

- [ ] **Step 2: Run — expect FAIL** (today counts Mon–Fri).
- [ ] **Step 3: Inject `ShiftScheduleResolver` (or an `IWorkingCalendar` facade) into `LeaveRequestService`; at each of the 3 sites, resolve the employee's working-day set for the date range and pass it as the `workWeek` argument** to `CountWorkingDays` / the half-day gate, instead of relying on `DefaultWorkWeek`.
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Add a single-branch-tenant regression** (no location/shift config → still Mon–Fri, existing behaviour preserved). **Commit.**

```bash
git commit -am "fix(leave): count leave days by resolved shift working-days, not hardcoded Mon-Fri (BUG-284)"
```

### Task 2.2: Fix OT weekend basis (fixes BUG-285)

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/OvertimeMultiplierResolver.cs:36`
- Modify: `src/backend/HRM.Infrastructure/Services/OvertimeService.cs:87-91,204-208` (pass the resolved working-day set)
- Test: `src/backend/HRM.Tests/Unit/OvertimeWeekendBasisTests.cs` + integration arm

**Interfaces:**
- Consumes: resolved working-day set (from `ShiftScheduleResolver`).
- Produces: `OvertimeMultiplierResolver.Resolve(...)` decides weekend by "date's ISO day ∉ workingDays", not `Saturday or Sunday`.

- [ ] **Step 1: Write the failing unit test** — with a Sun–Thu working set, Friday resolves to the **weekend** multiplier and Sunday to the **weekday** multiplier.

```csharp
[Fact]
public void Weekend_is_derived_from_working_day_set_not_saturday_sunday()
{
    var workingDays = new[]{7,1,2,3,4};                 // Sun-Thu
    var friday = new DateOnly(2026,7,3);                // ISO 5
    var sunday = new DateOnly(2026,7,5);                // ISO 7
    Assert.Equal(WeekendMultiplier, Resolve(friday, workingDays, settings).Multiplier);
    Assert.Equal(WeekdayMultiplier, Resolve(sunday, workingDays, settings).Multiplier);
}
```

- [ ] **Step 2: Run — expect FAIL** (hardcoded Sat/Sun).
- [ ] **Step 3: Change line 36** from `date.DayOfWeek is Saturday or Sunday` to `!workingDays.Contains(IsoDay(date.DayOfWeek))`; thread `workingDays` in from `OvertimeService` (which already resolves the employee's shift for the OT window).
- [ ] **Step 4: Run — expect PASS. Add an integration arm asserting stored OT earnings use the right multiplier for a Gulf tenant. Commit.**

```bash
git commit -am "fix(overtime): weekend multiplier derived from resolved shift, not Sat/Sun (BUG-285)"
```

### Task 2.3: Unify holiday lookups on `IHolidayProvider(locationId)` (fixes BUG-286)

**Files:**
- Modify: `src/backend/HRM.Infrastructure/Services/OvertimeService.cs:798-801` (replace inline unfiltered query)
- Modify: payroll working-days path to consult `IHolidayProvider` (Task 2.5 uses this)
- Test: `src/backend/HRM.Tests/Integration/OvertimeHolidayLocationScopeTests.cs`

- [ ] **Step 1: Write the failing test** — a New-York-only holiday does NOT give a London employee the holiday OT multiplier.
- [ ] **Step 2: Run — expect FAIL** (unfiltered holiday query today).
- [ ] **Step 3: Replace `OvertimeService.IsPublicHolidayAsync` body with a call to `_holidayProvider.GetHolidaysAsync(tenantId, from, to, employee.LocationId)` and check membership.**
- [ ] **Step 4: Run — expect PASS. Commit.**

```bash
git commit -am "fix(overtime): scope holiday OT multiplier by employee LocationId via IHolidayProvider (BUG-286)"
```

### Task 2.4: Location-scoped `AttendanceSettings` override layer

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/AttendanceSettings.cs` (add nullable `LocationId`; a null row = tenant default, a set row = that location's override)
- Modify: `src/backend/HRM.Infrastructure/Persistence/Configurations/AttendanceSettingsConfiguration.cs` (unique index `(TenantId, LocationId)` where not deleted; nullable LocationId)
- Create: `src/backend/HRM.Infrastructure/Services/AttendancePolicyResolver.cs` — resolves the effective settings for an employee: **employee's Location override → tenant (LocationId null) → lazily-created default**.
- Create (CLI): migration `AttendanceSettings_LocationOverride`
- Test: `src/backend/HRM.Tests/Integration/AttendancePolicyResolverTests.cs`

**Interfaces:**
- Produces: `AttendancePolicyResolver.ResolveAsync(Guid employeeId, DateOnly asOf) : AttendanceSettings` — the single accessor OT/late/geofence code uses instead of reading the tenant row directly.

- [ ] **Step 1: Write the failing test** — a Dubai-location override with `WeekendOvertimeMultiplier = 3.0` resolves for a Dubai employee, while a Colombo employee still gets the tenant default `2.0`.
- [ ] **Step 2: Run — expect FAIL** (single tenant row today).
- [ ] **Step 3: Add nullable `LocationId`; migration; unique `(TenantId, LocationId)`.**
- [ ] **Step 4: Implement `AttendancePolicyResolver`** (employee → LocationId → override row; else tenant row; else lazy default). Point OT/late/geofence read sites at it.
- [ ] **Step 5: Run — expect PASS. Validation: override `LocationId` must be same-tenant; one override per (tenant, location). Commit.**

```bash
git commit -am "feat(attendance): location-scoped AttendanceSettings override + AttendancePolicyResolver"
```

### Task 2.5: `ExcludeHolidaysFromWorkingDays` flag → payroll working-days denominator (D7)

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/AttendanceSettings.cs` (add `bool ExcludeHolidaysFromWorkingDays = true`)
- Modify: `src/backend/HRM.Infrastructure/Services/ShiftScheduleResolver.cs` `CountWorkingDays` (optionally subtract holidays) OR the payroll caller in `PayrollOvertimeCalculator` / `PayrollRunProcessor`
- Test: `src/backend/HRM.Tests/Integration/PayrollWorkingDaysDenominatorTests.cs`

- [ ] **Step 1: Write the failing test** — with the flag on (default), a month with 2 public holidays yields a working-days denominator reduced by 2 (so the OT hourly base rises accordingly); with the flag off, holidays count as working days.
- [ ] **Step 2: Run — expect FAIL** (payroll denominator ignores holidays today).
- [ ] **Step 3: Add the flag (default true), resolve it via `AttendancePolicyResolver`, and subtract location-scoped holidays from the payroll working-days count when on.**
- [ ] **Step 4: Run — expect PASS. Commit.**

```bash
git commit -am "feat(payroll): configurable ExcludeHolidaysFromWorkingDays (default on) for working-days denominator"
```

**PHASE 2 GATE:** build green · all new Postgres tests green · full `dotnet test` green · **manually re-verify BUG-284/285/286 fixtures fail pre-fix / pass post-fix** (they were authored as the Step-1 tests).

---

## Phase 3 — Employee attributes

### Task 3.1: `Employee.Fte` + wire proration (fixes the US-LV-002 AC-K1 seam)

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/Employee.cs` (add `decimal Fte = 1.0m`)
- Modify: `CreateEmployeeCommand` / `UpdateEmployeeProfileRequest` / DTOs / validator (range `0 < Fte <= 1.0`, 2dp)
- Modify: `src/backend/HRM.Infrastructure/Services/LeaveEntitlementService.cs:~414,~521,~602` (replace `fte: 1.0m` with `employee.Fte`)
- Create (CLI): migration `Employee_Fte`
- Modify FE: employee create/edit form adds an FTE input (Core HR)
- Test: `src/backend/HRM.Tests/Integration/FteProrationTests.cs`

- [ ] **Step 1: Write the failing test** — a 0.5-FTE employee joining at year start gets exactly half the full-year entitlement.
- [ ] **Step 2: Run — expect FAIL** (callers hardcode 1.0).
- [ ] **Step 3: Add `Fte` (default 1.0) + migration; validator rejecting `0`, negatives, `>1.0`; wire `employee.Fte` into the 3 `CalculateProRata` call sites.**
- [ ] **Step 4: Run — expect PASS. Add validation negative tests. Commit.**

```bash
git commit -am "feat(core-hr): Employee.Fte (default 1.0) + wire part-time leave proration"
```

### Task 3.2: `FteScaledOvertimeBase` flag → OT hourly base (D6)

**Files:**
- Modify: `AttendanceSettings.cs` (add `bool FteScaledOvertimeBase = false`)
- Modify: `src/backend/HRM.Domain/Payroll/PayrollOvertimeCalculator.cs` (when on, OT hourly base uses `standardHours * employee.Fte`)
- Test: `src/backend/HRM.Tests/Unit/OvertimeFteBaseTests.cs`

- [ ] **Step 1: Write the failing test** — with the flag on, a 0.5-FTE employee's OT hourly rate is 2× a full-timer's on the same monthly basic; with the flag off (default), they're equal.
- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Add flag (default false), resolve via `AttendancePolicyResolver`, scale the base when on.**
- [ ] **Step 4: Run — expect PASS. Commit.**

```bash
git commit -am "feat(payroll): configurable FteScaledOvertimeBase (default off) for part-time OT base"
```

### Task 3.3: `Employee.WorkArrangement` + Remote geofence exemption

**Files:**
- Create: `src/backend/HRM.Domain/Enums/WorkArrangement.cs` (`OnSite=0, Hybrid=1, Remote=2`)
- Modify: `Employee.cs` (add `WorkArrangement` default `OnSite`), DTOs, validator (defined enum only)
- Modify: clock-in path (`AttendanceService`) — `Remote` ⇒ skip geofence enforcement
- Create (CLI): migration `Employee_WorkArrangement`
- Modify FE: employee form adds a work-arrangement select
- Test: `src/backend/HRM.Tests/Integration/RemoteClockInTests.cs`

- [ ] **Step 1: Write the failing test** — a `Remote` employee outside the geofence can clock in; an `OnSite` employee outside it cannot.
- [ ] **Step 2: Run — expect FAIL** (geofence is unconditional today).
- [ ] **Step 3: Add enum + field + migration; in the geofence check, bypass when `WorkArrangement == Remote`.**
- [ ] **Step 4: Run — expect PASS. Commit.**

```bash
git commit -am "feat(attendance): Employee.WorkArrangement + geofence exemption for Remote"
```

**PHASE 3 GATE:** build + full test suite green; FE `ng build` + `ng test` green.

---

## Phase 4 — Fiscal / probation location-scoping

### Task 4.1: Configurable probation period (fixes ISSUE-304)

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/Tenant.cs` (add `int ProbationPeriodDays = 90`)
- Modify: `src/backend/HRM.Domain/Entities/Location.cs` (add `int? ProbationPeriodDays`)
- Modify: `src/backend/HRM.Infrastructure/Services/EmployeeStatusService.cs:342,352-353,362` (resolve Location override → Tenant default instead of `AddDays(90)`)
- Modify: `TenantSettingsService` + org-profile DTO/validator (expose `ProbationPeriodDays`, positive bound)
- Create (CLI): migration `ProbationPeriodDays`
- Test: `src/backend/HRM.Tests/Integration/ProbationPeriodConfigTests.cs`

- [ ] **Step 1: Write the failing test** — a tenant with `ProbationPeriodDays = 180` gets a probation-end reminder based on DOJ+180; a Dubai-location override of `90` wins for a Dubai employee.
- [ ] **Step 2: Run — expect FAIL** (hardcoded 90).
- [ ] **Step 3: Add fields + migration; resolve `Location.ProbationPeriodDays ?? Tenant.ProbationPeriodDays` in `EmployeeStatusService`; validator (positive, sane upper bound).**
- [ ] **Step 4: Run — expect PASS. Commit.**

```bash
git commit -am "feat(core-hr): configurable probation period (Tenant + Location override) (ISSUE-304)"
```

### Task 4.2: Wire `FiscalYearStartMonth` into the leave year (fixes ISSUE-305, supersedes ISSUE-176)

**Files:**
- Create: `src/backend/HRM.Domain/Leave/LeaveYear.cs` — a pure helper `LeaveYear.BoundsFor(DateOnly asOf, int fiscalYearStartMonth) : (DateOnly start, DateOnly end)`.
- Modify: `LeaveAccrualJob`, `ProcessLeaveYearEndJob`, `LeaveCarryForwardCalculator.ComputeExpiryDate`, `LeaveEntitlementEngine.CalculateProRata` (replace `new DateTime(leaveYear,1,1)…12,31` with `LeaveYear.BoundsFor(..., tenant.FiscalYearStartMonth)`)
- Test: `src/backend/HRM.Tests/Unit/LeaveYearTests.cs` + integration arm for an Apr–Mar tenant
- Modify: statutory YTD report path to use the same helper (closes ISSUE-176) — optional, same change

- [ ] **Step 1: Write the failing unit test** — for `fiscalYearStartMonth = 4`, `BoundsFor(2026-07-15)` returns `2026-04-01 .. 2027-03-31`; for `= 1` it returns the calendar year.
- [ ] **Step 2: Run — expect FAIL** (helper doesn't exist).
- [ ] **Step 3: Implement `LeaveYear.BoundsFor`; replace the four hardcoded Jan-1/Dec-31 sites; delete the `TODO(tenant-settings)` in `ProcessLeaveYearEndJob`.**
- [ ] **Step 4: Run — expect PASS. Integration arm: an Apr–Mar tenant's accrual/carry-forward anchor to April. Commit.**

```bash
git commit -am "feat(leave): leave-year honours Tenant.FiscalYearStartMonth (calendar or fiscal) (ISSUE-305)"
```

### Task 4.3: `LeaveEntitlementRule.LocationId` tier

**Files:**
- Modify: `src/backend/HRM.Domain/Entities/LeaveEntitlementRule.cs` (add nullable `LocationId`)
- Modify: `src/backend/HRM.Infrastructure/Services/LeaveEntitlementEngine.cs` `SelectMostSpecificRule` (add Location to the specificity score, above employment-type)
- Modify: rule CRUD DTO/validator (LocationId same-tenant)
- Create (CLI): migration `LeaveEntitlementRule_LocationId`
- Test: `src/backend/HRM.Tests/Integration/LeaveEntitlementLocationRuleTests.cs`

- [ ] **Step 1: Write the failing test** — a Dubai-location rule (25 days) wins over a tenant-wide rule (20 days) for a Dubai employee; a Colombo employee still gets 20.
- [ ] **Step 2: Run — expect FAIL** (no Location dimension today).
- [ ] **Step 3: Add `LocationId` + migration; extend the specificity scoring (Location match = highest weight below employee override); validator same-tenant.**
- [ ] **Step 4: Run — expect PASS. Commit.**

```bash
git commit -am "feat(leave): LeaveEntitlementRule location-scoping tier (Dubai vs Colombo entitlements)"
```

**PHASE 4 GATE:** full backend + FE suite green; re-run ISSUE-304/305 fixtures.

---

## Self-Review (against the spec)

- **Spec §4 Phase 1** → Tasks 1.1–1.3. ✅ (BUG-113 already done, correctly omitted.)
- **Spec §4 Phase 2** → Tasks 2.1 (BUG-284), 2.2 (BUG-285), 2.3 (BUG-286), 2.4 (location policy), 2.5 (D7 flag). ✅
- **Spec §4 Phase 3** → Tasks 3.1 (Fte), 3.2 (D6 flag), 3.3 (WorkArrangement). ✅
- **Spec §4 Phase 4** → Tasks 4.1 (probation ISSUE-304), 4.2 (fiscal ISSUE-305), 4.3 (entitlement Location tier). ✅
- **Spec §7.1 validation coverage** → validator + negative test steps in Tasks 1.3, 3.1, 3.3, 4.1, 4.3; cross-tenant FK isolation tests in 1.3, 2.4, 4.3. ✅
- **Spec §6 stay-fixed list** → Global Constraints "do not touch". ✅
- **Type consistency:** `ResolveWorkingDaySetsAsync` signature reused verbatim across Tasks 1.2/2.1/2.2; `AttendancePolicyResolver.ResolveAsync` reused across 2.4/2.5/3.2; `LeaveYear.BoundsFor` reused across 4.2. ✅

## Execution note

Phases are independently shippable and should be **separate PRs** (Phase 1 first — it gates the rest). Sub-agents (`@backend-dev`, `@frontend-dev`) implement on disjoint paths and do **not** commit; the orchestrator runs the verify gate (build + Postgres + Karma + integration-enforcer + test-authenticator) per phase before merging, per the repo's established method.
