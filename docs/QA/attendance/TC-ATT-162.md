---
id: TC-ATT-162
user_story: US-ATT-011
module: Attendance
priority: high
type: functional
status: automated
created: 2026-07-21
automated: 2026-07-21
defect:
  - DF-22
  - ISSUE-309
---

# TC-ATT-162: Tenant-wide attendance sweeps (auto-clock-out + monthly-summary) resolve the attendance policy PER LOCATION, not the tenant default (DF-22 / ISSUE-309)

## 1. Test Objective
Verify the DF-22 / ISSUE-309 fix: the two write-path tenant-wide sweeps — the **auto-clock-out**
safety-net job and the **monthly-summary** generation — resolve each employee's effective
`AttendanceSettings` from **their Location override → the tenant default → code defaults**, instead of
applying one tenant-default row to every employee across all branches. Resolution is batched once per
sweep via `AttendancePolicyResolver.LoadAllAsync` (the tenant-default row + every location override in
one read) + `For(map, employee.LocationId)` (in-memory precedence). A location with no override — or a
tenant with no settings rows at all — falls back to the same code defaults as before (behaviour
unchanged for single-location / no-override tenants). The **absenteeism report deliberately stays
tenant-wide** and is explicitly out of scope. Before the fix, both a location-override employee and a
tenant-default employee in the same sweep read the identical tenant-default policy.

## 2. Related Requirements
- User Story: US-ATT-011 (auto-clock-out safety net) + the monthly-summary generation surface
- Acceptance Criteria: AC-3 (the sweep applies the configured attendance policy)
- Business Rule: attendance policy is configurable per tenant **and per location** (`AttendanceSettings`, one row per `(TenantId, LocationId)`)
- Finding: DF-22 / ISSUE-309 (tenant-wide sweeps ignored per-location overrides); reuses the CAL-4 `AttendancePolicyResolver.LoadAllAsync`/`For` batch resolver

## 3. Preconditions
- A tenant with a Dubai `Location` carrying an `AttendanceSettings` override **and** a tenant-default
  `AttendanceSettings` row (`LocationId == null`).
- One employee assigned to Dubai (`Employee.LocationId = dubai`) and one at the tenant default (`LocationId == null`), otherwise identical (same shift, dept, and seeded punch).
- Sweeps run inside `ITenantJobRunner.RunForTenantAsync` (tenant filter set for the resolver read).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant-default `HalfDayEnabled` | false | monthly-summary arm |
| Dubai override `HalfDayEnabled` | true | monthly-summary arm |
| Seeded day (both employees) | 240 min on a 480-min shift | exactly 50% → HALF_DAY only where enabled |
| Tenant-default `AutoBreakMinutes` | 60 | auto-clock-out arm |
| Dubai override `AutoBreakMinutes` | 120 | auto-clock-out arm |
| Open overnight log (both) | identical instant → 599-min gross | isolates the AutoBreak delta |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Run `GenerateMonthlySummaryCommand` for a tenant with a Dubai `HalfDayEnabled=true` override + a `false` tenant default; both employees have a 240-min day on the shared 480-min shift. | The Dubai employee's row reads `PresentDays == 0.5` (HALF_DAY) while the default-location employee reads `== 1.0` — each from ITS location's policy, in one sweep. Pre-fix: both `1.0`. | `MonthlySummaryIntegrationTests.Generate_LocationOverride_EachEmployeeGetsItsOwnLocationPolicy_DF22` |
| 2 | Run the auto-clock-out `RunAsync` sweep with a Dubai `AutoBreakMinutes=120` override + a `60` tenant default; both employees have an identical open overnight log (599-min gross). | Both closed as `ANOMALY` in one sweep; `TotalWorkMinutes` is `479` for the Dubai employee (599 − 120) vs `539` for the default employee (599 − 60) — the 60-min delta traces only to the per-location AutoBreak. Pre-fix: both equal. | `AttendanceClockOutIntegrationTests.AutoClockOutJob_ResolvesPolicyPerLocation_AcrossTwoLocationsInOneSweep_DF22` |
| 3 | (Regression) Existing single-location tenant sweeps with one settings row. | Unchanged — the null/no-override path keeps the code-default fallbacks; all prior tenant-default arms stay green. | existing `MonthlySummaryIntegrationTests` / `AttendanceClockOutIntegrationTests` arms (unmodified) |

## 6. Postconditions
- Multi-location tenants have each branch's employees swept against that branch's attendance policy;
  single-location / no-override tenants are behaviour-identical to before. The absenteeism report
  remains tenant-wide (unchanged, by design).

## 7. Test Category Tags
- [x] Happy path (override honored)
- [ ] Negative test
- [x] Boundary test (null-override → code-default fallback preserved)
- [ ] Security test
- [x] Multi-tenant isolation (per-location resolution inside the tenant-scoped sweep)
- [ ] Performance test (also removes the monthly-summary per-employee settings N+1)
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite), both carrying `[Trait("TC", "TC-ATT-162")]`:**
  - `HRM.Tests/Integration/MonthlySummaryIntegrationTests.Generate_LocationOverride_EachEmployeeGetsItsOwnLocationPolicy_DF22`
  - `HRM.Tests/Integration/AttendanceClockOutIntegrationTests.AutoClockOutJob_ResolvesPolicyPerLocation_AcrossTwoLocationsInOneSweep_DF22`
- The CAL-4 `AttendancePolicyResolver` index/precedence contract is separately guarded on real Postgres by `AttendancePolicyResolverTests` + `AttendanceSettingsCrudPostgresTests`.
