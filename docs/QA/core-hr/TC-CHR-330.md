---
id: TC-CHR-330
user_story: US-CHR-009
module: Core HR
priority: medium
type: integration
status: automated
created: 2026-07-15
defect:
  - ISSUE-304
---

# TC-CHR-330: Probation period is tenant-configurable with a Location override — DOJ+180 tenant default, Dubai override wins at DOJ+90 (ISSUE-304 regression)

## 1. Test Objective
Verify the ISSUE-304 fix on US-CHR-009 BR-6: `EmployeeStatusService.CheckProbationEndDatesAsync` reads the probation period from **`Tenant.ProbationPeriodDays`** (with an optional **`Location.ProbationPeriodDays?` override**) instead of the hardcoded 90 days. A tenant with `ProbationPeriodDays = 180` fires the probation-end reminder at **DOJ + 180**; a Dubai employee whose Location override is **90** fires at **DOJ + 90** (Location wins over Tenant per D5).

## 2. Related Requirements
- User Story: US-CHR-009
- Business Rule: BR-6 (per-tenant probation period)
- Defect: ISSUE-304
- Cross-reference: spec Phase 4 (Tenant default + optional Location override); spec §7.1 (positive-integer within sane bounds; location null → tenant fallback)

## 3. Preconditions
- Tenant with `ProbationPeriodDays = 180`.
- A Dubai Location with `ProbationPeriodDays = 90`; a Colombo Location with `ProbationPeriodDays = null` (falls back to tenant).
- Three employees: tenant-only (no location), Dubai, Colombo — each with a known `DateOfJoining`.
- Postgres-backed context; probation-reminder evaluation runnable for a fixed "as-of" date.
- Pre-fix: the literal `DateOfJoining.AddDays(90)` makes the 180-day and override arms FAIL.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant ProbationPeriodDays | 180 | default |
| Dubai override | 90 | Location wins |
| Colombo override | null | falls back to 180 |
| Invalid override | 0 / negative | reject (§7.1) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Evaluate probation-end for the tenant-only employee. | Reminder anchored at **DOJ + 180** (not 90). |
| 2 | Evaluate for the **Dubai** employee. | Anchored at **DOJ + 90** — the Location override wins. |
| 3 | Evaluate for the **Colombo** employee (null override). | Anchored at **DOJ + 180** — falls back to the tenant default. |
| 4 | Attempt to set a Location/Tenant probation of `0` or a negative value. | Rejected (positive integer within sane bounds). |

## 6. Postconditions
- Probation windows reflect tenant config and location overrides; no hardcoded 90 remains.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `ProbationPeriodConfigTests.TenantConfigured180_AnchorsTheReminderAtDoj180_NotDoj90` (step 1)
  - `ProbationPeriodConfigTests.LocationOverrideWins_AndANullOverrideFallsBackToTheTenantDefault` (steps 2–3 — both employees asserted in ONE sweep, so resolving everyone at a single period cannot pass)
  - `ProbationPeriodConfigTests.TenantDefault90_IsUnchanged_AndNotifiesAtDoj90` (**the control** — 90 is the column default and the value the service hardcoded, so every existing tenant is unchanged)
  - `ProbationPeriodConfigTests.EmployeeOutsideTheReminderWindow_IsNotNotified` (the mirror — stops "notify every probation employee" passing)
  - `ProbationPeriodConfigTests.CrossTenantSweep_ResolvesEachTenantsOwnPeriod` (the sweep is cross-tenant via `IgnoreQueryFilters`; each tenant must resolve its OWN period)
  - `ProbationPeriodConfigTests.ColumnDefaults_TenantIs90_AndLocationStaysNull` (migration safety on live rows)
- Step 4 (reject 0 / negative) → `UpdateOrgProfileValidator` bounds it to 1..1825.
- **Mutation-verified:** reverting the resolution to the hardcoded 90 reddens 4 arms; the two 90-day controls correctly stay green.
- **Note:** the period is no longer a SQL predicate (it is per-employee now), so the sweep resolves in memory off 3 batched queries — flat in employee count, which matters because this is a cross-tenant background job.
- Backing suite trait: `[Trait("TC", "TC-CHR-330")]`.
