---
id: TC-LV-264
user_story: US-LV-006
module: Leave Management
priority: high
type: integration
status: automated
created: 2026-07-15
automated: 2026-07-16
automation:
  - HRM.Tests/Unit/LeaveYearTests.cs (34 arms — boundary arithmetic, fiscal pro-rata, calendar controls)
  - HRM.Tests/Unit/LeaveAccrualJobLeaveYearTests.cs (6 arms — the CREDIT-side label; had ZERO tests)
  - HRM.Tests/Unit/LeaveYearResolverDiRegistrationTests.cs (7 arms — the real AddInfrastructure container)
  - HRM.Tests/Unit/ProcessLeaveYearEndJobWindowTests.cs (23 arms — the per-tenant year-end window)
  - HRM.Tests/Integration/FiscalLeaveYearIntegrationTests.cs (10 arms — the column is actually READ, and
    the ledger's credit and debit sides agree)
  - HRM.Tests/Integration/FiscalLeaveYearMoneyIntegrationTests.cs (ISSUE-313 — 4 arms: the two MONEY debit
    sites, LeaveEncashmentService + RealPayrollFnFIntegration, now have mutation-verified fiscal guards)
  - HRM.Tests/Unit/LeaveYearEndJobRetryTests.cs (ISSUE-041 — clock pinned so the due-window job is drivable)
defect:
  - ISSUE-305
---

# TC-LV-264: Apr–Mar fiscal tenant — leave-year boundary, accrual window, and carry-forward expiry anchor to April, not January (ISSUE-305 regression)

## 1. Test Objective
Verify the ISSUE-305 fix on US-LV-006/008: the leave subsystem reads `Tenant.FiscalYearStartMonth` instead of hardcoding calendar Jan 1–Dec 31. For a tenant with `FiscalYearStartMonth = 4` (Apr–Mar), the **leave-year boundary**, the **accrual window** (`LeaveAccrualJob`), the **year-end** processing (`ProcessLeaveYearEndJob`), the **carry-forward expiry** (`LeaveCarryForwardCalculator.ComputeExpiryDate`), and **pro-rata** (`LeaveEntitlementEngine.CalculateProRata`) all anchor to **April**.

## 2. Related Requirements
- User Story: US-LV-006 (also US-LV-002 pro-rata, US-LV-008 carry-forward)
- Acceptance Criteria: **US-LV-006 AC-6 + BR-4** — leave-year is calendar-or-fiscal per tenant; balances,
  ledger and the year selector are bounded by the fiscal leave year
- Defect: ISSUE-305
- Cross-reference: `Tenant.FiscalYearStartMonth` (spec Phase 4). **ISSUE-176 is NOT superseded** by this —
  it is a *Payroll* finding (StatutoryDeduction YTD); the leave *report* read-layer remainder is **ISSUE-311**.

## 2b. Scope note — this fix is 11 sites, not 4
The finding named 4 hardcoded sites. The implementation sweep found 7; an `@integration-enforcer` audit then
found the ledger's **debit** side (leave requests, LOP, encashment, F&F) was still deriving the label from a
raw `.Year` — which would have made an Apr–Mar tenant **worse than before the fix** (credit and debit reading
different buckets → balance 0 → requests blocked, and F&F silently under-paying leave encashment on
termination). All sites now derive the label from one injected `ITenantLeaveYearResolver`, so a future site
that forgets to read the column is visible in a constructor rather than only at runtime, for one tenant, for
three months a year.

## 3. Preconditions
- Tenant with `FiscalYearStartMonth = 4`.
- An employee with an entitlement, accrual, and a carry-forward-eligible balance.
- Postgres-backed context; jobs runnable for a controlled "as-of" date.
- Pre-fix: `leaveYear = DateTime.UtcNow.Year` + `new DateTime(leaveYear,1,1)…12,31` make these arms FAIL.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| FiscalYearStartMonth | 4 | Apr–Mar |
| Leave-year start | 1 April | boundary |
| Leave-year end | 31 March | boundary |
| Mid-year hire | e.g. 1 Oct | pro-rata over Apr–Mar |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Resolve the current leave-year window for the tenant. | **1 Apr – 31 Mar** (not 1 Jan – 31 Dec). | `LeaveYearTests.AprilTenant_LeaveYearSpansTheCalendarBoundary` |
| 2 | Run the accrual job and inspect the leave-year it accrues into. | Accrual window anchored to the Apr–Mar year: on 2027-02-10 an Apr–Mar tenant accrues into leave year **2026**, not 2027. | `LeaveYearTests.AprilTenant_LabelIsTheYearTheLeaveYearStartsIn` (the `LabelFor` the job passes to `ProcessAccrualsAsync`) |
| 3 | Run year-end / carry-forward and inspect the carry-forward **expiry date**. | Expiry counts from the tenant's own 1 April: 2027-04-01 + 3mo = **2027-07-01**, not 2027-04-01. | `FiscalLeaveYearIntegrationTests.FiscalTenant_CarryForwardExpiresFromTheirOwn1April` |
| 4 | Compute pro-rata for the 1-Oct hire. | Pro-rated over the remaining Apr–Mar period, not Jan–Dec. | `LeaveEntitlementEngine.CalculateProRata` now takes `fiscalYearStartMonth`; bounds pinned by `LeaveYearTests.BoundsFor_AnyStartMonth_IsAFullContiguousYear` |
| 5 | Verify the year-end job actually FIRES on the tenant's boundary. | Due on 1 Apr (closing 2025); **not** due on 1 Jan — the only day the old `0 2 1 1 *` cron ran. | `ProcessLeaveYearEndJobWindowTests.AprilTenant_IsDueOn1April_ClosingThePreviousFiscalYear` + `..._IsNotDueOn1January_TheDayTheOldCronFired` |
| 6 | **Control:** a tenant with `FiscalYearStartMonth = 1`. | Unchanged calendar Jan–Dec behaviour (no regression). | `LeaveYearTests.Calendar_BoundsAreTheUnchangedCalendarYear`, `FiscalLeaveYearIntegrationTests.CalendarTenant_CarryForwardExpiryIsUnchanged` |
| 7 | **Differential:** two tenants identical except `FiscalYearStartMonth`. | Expiry dates MUST differ — if they match, the column is being ignored again. | `FiscalLeaveYearIntegrationTests.TheOnlyDifferenceIsTheColumn_SoTheExpiryDatesMustDiffer` |
| 8 | **Ledger coherence (the regression this TC nearly missed):** for a date in the Jan–Mar window, compare the leave-year label the CREDIT side writes with the one the DEBIT side reads. | Both resolve **2026** for 2027-02-10. If they diverge, an Apr–Mar employee's balance reads 0 and every request is blocked "insufficient balance" for a quarter of the year. | `FiscalLeaveYearIntegrationTests.CreditAndDebitSidesResolveTheSameLeaveYear_InTheJanuaryToMarchWindow` |
| 9 | **Ledger stamps:** inspect `OccurredAt` on the carry-forward and expiry rows. | Credit stamped **2027-04-01** (the fiscal year's opening), forfeiture **2027-03-31** (its close) — not 1 Jan / 31 Dec. | `FiscalLeaveYearIntegrationTests.TheLedgerYearLabelsAreUnchangedForAFiscalTenant` |
| 10 | **Fiscal pro-rata:** a 1-Oct joiner under a month-4 vs a month-1 tenant. | Must DIFFER (~6 months remain of an Apr–Mar year vs ~3 of a Jan–Dec one) — this is a money number the accrual job credits. | `LeaveYearTests.ProRata_ForAnOctoberJoiner_DiffersBetweenAFiscalAndACalendarTenant` |

## 6. Postconditions
- Fiscal-year tenants get correct leave-year boundaries, accrual, expiry, and pro-rata; calendar-year tenants unaffected.
- Leave-year **labels** are unchanged (still the calendar year the year starts in) — no ledger row is renumbered.

## 5b. Mutation Evidence (2026-07-16)
Green tests prove nothing until they fail on a broken build. Each site was mutated and the arms re-run.

**⚠ Round 1 of this evidence was itself unreliable** — a `@test-authenticator` audit showed 4 of the 7 sites
had **zero** mutation resistance (the campaign only covered sites I thought to mutate), and one of my
mutations was a **silent no-op** whose anchor never matched, so it "survived" while proving nothing. Every
mutation below now asserts the edit LANDED before trusting the verdict.

### Round 3 — the three zero-resistance sites closed (2026-07-16, post-audit)
A `@test-authenticator` audit found **7 of 13 sites had ZERO mutation resistance**: my campaign had only
mutated the sites I already believed were covered, so "7/7 killed" was true and meaningless. Three closed:

| Mutation | Before | Now |
|---|---|---|
| `LeaveAccrualJob` label → `UtcNow.Year` (the CREDIT side; **no test existed at all**) | SURVIVED | **KILLED** (5 arms) |
| `LeaveEntitlementService` drops the basis → engine | SURVIVED | **KILLED** (1 arm) — *and now a **compile error*** |
| `AddScoped<ITenantLeaveYearResolver,…>` deleted | untested | **KILLED** (5 arms) |

**The best fix was not a test.** `CalculateProRata`'s `fiscalYearStartMonth` had a `= CalendarStartMonth`
**default**, which is what made a missing argument invisible — a wrong money number credited to the ledger with
the suite green. The default is **removed**, so the mutant no longer compiles. (Same reasoning as removing the
`?? .Year` fallbacks: prefer "won't build" to "won't pass".)

**The two MONEY sites are now guarded (ISSUE-313, 2026-07-16)** — mutation-verified in
`FiscalLeaveYearMoneyIntegrationTests`:

| Mutation | Before | Now |
|---|---|---|
| `LeaveEncashmentService` leave year `LabelForAsync(payPeriod)` → `input.PayYear` (a fiscal Jan–Mar encashment rejected 422) | SURVIVED | **KILLED** (fiscal arm; calendar control survives) |
| `RealPayrollFnFIntegration` leave year `LabelForAsync(lwd)` → `lwd.Year` (F&F encashment line silently vanishes) | SURVIVED | **KILLED** (fiscal arm; calendar control survives) |

Each fiscal arm injects a REAL `TenantLeaveYearResolver` (NOT the ctor's null fallback, which would re-key off
the calendar year and hide the read under test), seeds a month-4 tenant, credits the balance under the fiscal
leave-year label, and drives the REAL service in the Jan–Mar window.

**⚠ Still zero-resistance, correct by inspection only** (3 remaining, all NON-money, wiring-auditor swept —
tracked under ISSUE-313): `LopService` debit · `LeaveDashboardService` Pending bounds + `ResolveLeaveYear`.

### Round 2 — the sites the audit proved were unprotected
| Mutation | Round 1 | Now |
|---|---|---|
| Pro-rata reverted to hardcoded Jan–Dec | SURVIVED | **KILLED** (2 arms) |
| Resolver returns constant calendar — *the original read-by-nothing defect* | SURVIVED | **KILLED** (4 arms) |
| Carry-forward `OccurredAt` hardcoded to 1 Jan | SURVIVED | **KILLED** (1 arm) |
| Debit side regressed to raw `.Year` — *the ledger split* | (not tested) | **KILLED** (1 arm) |

### Round 1 — the year-end window (still valid)

| Mutation | Verdict |
|---|---|
| Reintroduce the original defect: `FiscalYearStartMonth` read by nothing (hardcode calendar) | **KILLED** (2 arms) |
| Expiry anchored on January regardless of tenant | **KILLED** (2 arms) |
| Year-end window off-by-one (`<` → `<=`) | **KILLED** (4 arms) |
| Closing year drops the `-1` | **KILLED** (13 arms) |
| Year-end window never closes | **KILLED** (12 arms) |
| `GraceDays` 7 → 1 (no catch-up after a missed run) | **KILLED** (6 arms) |
| Window anchored on 1 Jan instead of the tenant's start month | **KILLED** (10 arms) |

Two mutants survived and were investigated rather than papered over: substituting `today.Year` for `LabelFor`, and dropping the `daysSinceOpen >= 0` lower bound. Both are **behaviourally equivalent** given `LabelFor`'s contract (it returns the year *containing* today, so the offset is never negative, and a tenant is only ever due inside its own start month). The dead lower bound was **deleted** rather than tested.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
