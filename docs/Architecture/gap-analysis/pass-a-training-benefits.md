# Pass A12 — training-benefits requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains` @ `75a20208`
> **Depth:** all 4 items are `Should Have` (zero Must Have) → **one row per story**, with all 27 ACs read individually.
> **Status:** ✅ VALIDATED — 2 of 2 orchestrator spot-checks confirmed, **including one that refutes my brief.**
> **Headline:** this module is a **counter-example to the codebase-wide contract-drift pattern** — zero FE/BE mismatches across 12 DTO pairs. Its real gaps are orphaned FE methods and one unenforced status check.

## Orchestrator validation

| Claim | Result |
|---|---|
| **My brief's "leg 3 will fail for most rows" was WRONG** | ✅ **Confirmed — I was wrong.** `BenefitEnrollmentPostgresTests.cs`, `BenefitPlanPostgresTests.cs`, `TrainingEnrollmentPostgresTests.cs` (all **real Testcontainers-Postgres**) + `BenefitEligibilityEvaluatorTests.cs`, `BenefitPlanValidatorTests.cs`, `TrainingValidatorTests.cs`, plus **6 Karma specs**. IEEE-829 TCs: **0**. The accurate statement is *"no TC suite"* — **not** *"zero test coverage."* |
| Employee `Status` never checked on the enrollment path | ✅ **Confirmed.** `grep -c EmployeeStatus` over `BenefitEnrollmentService.cs` returns **0**, while `Employee.cs:114` defines `public EmployeeStatus Status`. |

### ⚠ My brief propagated the QA tracker's error

I told this auditor *"leg 3 will fail for most rows."* I took that from `TEST-STATUS.md:233-235`, which lists US-TRN-001/002/003 under *"⚠ Actual zero test coverage."* **The auditor refused it and checked**, correctly noting that its contract defines leg 3 as *"an xUnit / Karma / Playwright test **or** an IEEE-829 TC."* Leg 3 **passes at story level for all three stories.**

It said plainly: *"I would have produced a materially misleading report if I had accepted it."* That is the eighth time an auditor has corrected one of my briefs, and the second time the error originated in a ledger I had been warning everyone else not to trust.

### The auditor also self-reported an error it made and corrected

Its first reachability grep used an exclusion pattern that filtered out the very line proving `EligibilityRulesComponent` **was** wired; a second used the wrong method name (`addEligibilityRule` vs the real `createEligibilityRule`). **Both would have been manufactured gaps.** It re-verified every orphan claim with a name-agnostic grep afterwards and disclosed the near-miss unprompted.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| **US-TRN-EPIC** | Cross-cutting: tenant isolation, notifications, audit on all training/benefit entities | Should | IMPLEMENTED | `AppDbContext.cs:759-775` (**all 5 entities filtered**); migrations `20260710184241_Training_Catalog.cs:97-128` + `20260711013157_Benefits_Enrollment.cs:100-116` (per-table `tenant_isolation` RLS policies added **by the same migrations that created the tables**); `NotificationEventCatalog.cs:1780-1884` (all 7 event keys); audit at `TrainingService.cs:617-631`, `BenefitPlanService.cs:276-290`, `BenefitEnrollmentService.cs:427-441` | The epic's "Future" list (multi-session attendance, dependents, payroll-deduction) is an **explicit documented deferral** |
| **US-TRN-001** | Training catalog + enrollment lifecycle (10 ACs) | Should | 🔴 **CONTRADICTED** | BE complete across all 10 ACs: `TrainingService.cs:96-138,208-214,60-92,288-302,266-271,364-385` (FIFO waitlist), `:396-432,448-460`; filter `AppDbContext.cs:760-764`. **FE missing**: no component calls `completeEnrollment` (`training.service.ts:108-117`) or `getTrainingHistory` (`:129-134`) — exhaustive grep of `features/` | `STATUS.md:264` claims *"SHIPPED — BE + FE"* unqualified. **AC-8 (mark completion + certificate/score) and AC-9's HR half have no UI.** Leg 2 fails for 2 of 10 ACs |
| **US-TRN-002** | Benefit-plan administration (8 ACs) | Should | IMPLEMENTED | `BenefitPlanService.cs:67-116,199-205` (status transition matrix), `:139-163` (before/after audit), `:40-50`; `BenefitsController.cs:57-58,70-71,85-86` (`Benefits.Manage` on all writes); filter `AppDbContext.cs:767-768`; FE routed `app.routes.ts:430-448`; tests `BenefitPlanPostgresTests.cs:58,152,170,206,247` | **Highest-confidence row (92%).** AC-6 ("hard delete blocked") is satisfied **structurally** — no DELETE route exists at all, documented deliberately at `BenefitsController.cs:13-18`. Defensible, but the AC's literal path is untestable |
| **US-TRN-003** | Benefit eligibility rules + employee enrollment (9 ACs) | Should | 🔴 **CONTRADICTED** | Mostly real: `BenefitEnrollmentService.cs:61-98,250-289,239-241` (422 + failing-rule reason), `:244-248` + partial unique index (409), `:232-233,291-327`; `BenefitEligibilityEvaluator.cs:33-53` (AND-composition); filter `AppDbContext.cs:771-775`. **Two gaps** — see below | `STATUS.md:266` discloses **only** the eligible-plans deferral (ISSUE-271). The other two gaps are **undisclosed** |

---

## CONTRADICTIONS

### 1. US-TRN-001 — ledger says "BE + FE"; two ACs have no FE
`STATUS.md:264`: `[x] US-TRN-001 — Training catalog & course enrollment *(SHIPPED 2026-07-10, PR #241 — BE + FE)*`

`completeEnrollment` and `getTrainingHistory` are defined in the service and called by **nothing**. The only training components are `course-list`, `course-form` and `my-enrollments`; `training.routes.ts:10-25` exposes exactly two routes.

**Compounding:** the backend has **no "list enrollments for course" endpoint** — only `me/enrollments` and `employees/{employeeId}/training-history`. So even via raw API, HR must walk employee-by-employee to obtain an enrollment id before completing it. The story's §8 API surface doesn't list a roster endpoint either, so this is design thinness rather than an AC violation — **but it is why AC-8 is awkward to reach at all.**

**Severity calibration (the auditor's own, and I agree):** narrow and specific. 8 of 10 ACs are fully FE-backed. **The ledger line is not fiction — it is unqualified where it should be qualified.**

### 2. US-TRN-003 — ledger discloses one deferral, hides two gaps

**A — leg 1, "active employees" never enforced (undisclosed).** AC-1 says *"Absence of rules means the plan is open to all **active** employees"*; FR-4 says *"no rules → eligible-if-active-employee."* Neither is enforced: `EnrollAsync` resolves the target employee at `:205-221` and never inspects `Status`; `EligiblePlansForAsync` builds `EmployeeAttributes` from employment type, tenure, department and job title — **`Status` is not in the tuple** (`BenefitEligibilityEvaluator.cs:12-16`).

**Net: a Terminated or Inactive employee is eligible for, and can be enrolled into, any rules-free plan.** Exactly the "stored but never read on the enforcement path" class.

**B — leg 2, a second orphan (undisclosed).** ISSUE-271 correctly documents that `GET .../employees/{id}/eligible` has no FE consumer. It does **not** document that `getEmployeeEnrollments()` (`benefit.service.ts:167-174`) — the *other* half of AC-8 — is likewise called by no component. **The finding's own arithmetic ("8 FE methods vs 9 BE endpoints") counts the FE method as present and therefore misses that it is dead.**

### 3. 🔵 REVERSE DRIFT (high value) — the QA tracker says "zero test coverage"; the module is among the better-tested in the repo

`TEST-STATUS.md:233-235` calls US-TRN-001/002/003 *"Actual zero test coverage."* What exists:

| File | Lines | Coverage |
|---|---|---|
| `TrainingEnrollmentPostgresTests.cs` | 375 | AC-2..10 banner-commented; **a real concurrency race test** (`:150 ConcurrentEnrolls_Capacity1_ExactlyOneEnrolled_OneWaitlisted`, NFR-3) |
| `BenefitPlanPostgresTests.cs` | 321 | AC-1..6, AC-8 |
| `BenefitEnrollmentPostgresTests.cs` | 410 | AC-2..7, AC-9; asserts notification dispatch at `:109-110` |
| 3 unit suites | ~400 | full operator × attribute matrix incl. fail-closed malformed values |
| 6 Karma specs | — | **~90 `it()` blocks**, asserting exact URLs |

All three integration suites run against **real Postgres via Testcontainers**, not InMemory. **Anyone triaging from that ledger line would rank this module as untested when it is better covered than several `[!]`-marked modules.**

### 4. Reverse drift (minor) — `INDEX.md:8` still calls these "STUBS"
Authored to full IEEE-830 on 2026-07-09 (`status: ready`, `acceptance_criteria_count: 10`) and built 2026-07-10. The index froze at the 2026-07-06 snapshot.

---

## GAPS RANKED

1. **US-TRN-003 AC-1/FR-4 — employee `Status` never checked. MED, S.** A terminated employee remains eligible for and enrollable into any rules-free plan. **Highest-value gap here because it is a *silent* correctness hole** — no error, no log, wrong data — on a benefits-cost path. *Fix:* add `Status == Active` to the target-employee guard (~`:220`) and to `EligiblePlansForAsync`; one xUnit arm. **Open question worth deciding rather than assuming:** should a terminated employee's *existing* enrollment auto-terminate? The story says nothing; AC-7 makes termination explicit and manual. *Recommendation: keep it manual, block only new enrollments.*
2. **US-TRN-001 AC-8 — completion + certificate/score has no UI. MED, M.** The certification half of the story (`CourseEnrollment.cs:39,42`) is unreachable through the app. A roster view needs a new BE endpoint (`GET courses/{id}/enrollments`) — **without it, HR cannot discover enrollment ids from the catalog.**
3. **Three orphaned FE service methods. LOW-MED, M.** `getTrainingHistory`, `getEmployeeEnrollments` exist and are dead; `getEmployeeEligiblePlans` doesn't exist (ISSUE-271). **All three are facets of one missing surface: an HR/manager view of a single employee's training + benefits.** *One employee-detail tab closes all four AC halves and ISSUE-271 together.*
4. **Doc-drift cluster. LOW severity, HIGH blast radius — this is what stops the above from being fixed.** `TEST-STATUS.md:233-235` "zero coverage", `INDEX.md:8` "STUBS", `STATUS.md:264/266` unqualified "BE + FE". Individually trivial; together **no reader of the ledgers can see either the real coverage or the real gaps.** *Fix: three line edits + `[!]` rows.* **S.**
5. **Dead contract member. LOW.** `benefit.models.ts:99` declares `'plan_has_enrollments'`, which no backend path can emit (no delete endpoint by design).
6. **Unused enum members. LOW, informational.** `EnrollmentStatus.NoShow`, `BenefitEnrollmentStatus.Pending`/`Declined` are declared, migrated and mirrored in TS but never set. **No AC requires them** — FR-vs-AC over-specification, not a defect. Flagged only so it isn't re-discovered as a "gap" later.

### On my brief's two §11.10/§11.11 leads — both confirmed as **Pass B's lane, not this one**
- **Session scheduling / per-session attendance:** confirmed absent (no `TrainingSession`; flat dates on `TrainingCourse`). **But no AC requires it**, and `US-TRN-EPIC.md:96-97` lists it under **Future (explicitly deferred)**. A tech-doc→BA coverage gap and a *documented decision* — not counted against any verdict.
- **Reimbursement claims:** confirmed absent, and **no AC, FR, BR or data requirement in any of these stories mentions reimbursement at all.** Same conclusion.
- **"Evaluation + certification":** confirmed **COVERED** (`CourseEnrollment.cs:39,42`, written `TrainingService.cs:414-415`, surfaced `:495-496`). Pass B's "COVERED-lite with naming drift" is accurate — **however, per gap #2 the *write* path has no UI, so certification data can only be entered via raw API. That materially weakens the COVERED reading and Pass B should know it.**

---

## COVERAGE SUMMARY

```
Story rows: 4 | IMPLEMENTED: 2 | PARTIAL: 0 | MISSING: 0 | CONTRADICTED: 2
(27 ACs read individually and rolled up per the Should-Have depth rule)
```

**🔵 This module is a counter-example to the "7 of 9 modules" contract-drift pattern — worth recording as what "done right" looks like.**

All 12 DTOs were diffed against their TypeScript interfaces field by field (`TrainingDtos.cs` ↔ `training.models.ts`; `BenefitDtos.cs` + `BenefitEnrollmentDtos.cs` ↔ `benefit.models.ts`). **Every field name, nullability and type aligns**, including `DateOnly → 'yyyy-MM-dd'` and enum→string-union conventions. **All 16 FE service methods match a real `[Http*]` route with the right verb**, checked against 21 controller actions. The envelope problem that bites elsewhere is handled globally by `api-envelope.interceptor.ts:37-52`.

**Leg breakdown:** leg 1 fails once (the active-employee check). Leg 2 fails four times (all orphaned FE methods). **Leg 3 fails zero times.**

**Backend quality, stated plainly rather than hunting for something to criticise:** correct three-layer isolation on all five entities; RLS policies added by the same migrations that created the tables; partial unique indexes backing both duplicate-block rules; a genuine pessimistic `SELECT … FOR UPDATE` seat-claim inside the Npgsql retry-safe execution strategy (`TrainingService.cs:510-548`); notification intents flushed **only after commit** with retry-safe clearing; and a DB-free eligibility evaluator that is **fail-closed on malformed rule values**. All handlers dispatched by routed controller actions; all three services DI-registered with real implementations — **no NoOp substitution.**

---

## CONFIDENCE

**Overall: 90%.**

| Verdict | Conf. | Settled by |
|---|---|---|
| US-TRN-002 IMPLEMENTED | **92%** | Residual: AC-7 (403 for non-Manage) is proven only by the `[RequirePermission]` attributes — the integration tests drive services directly, never the controller→MediatR→authz HTTP chain (already tracked repo-wide as ISSUE-273(d)). A `WebApplicationFactory` smoke test would settle it |
| US-TRN-001 CONTRADICTED (FE gap) | **93%** | Exhaustive grep, re-run after the auditor's own false positive. Small residual: completion could be triggered from a shared/admin component outside `features/` |
| US-TRN-003 leg-1 gap | **95%** | Zero `EmployeeStatus` hits; both enrollment paths read end to end *(orchestrator confirmed)* |
| US-TRN-003 leg-2 orphan | **90%** | Same grep-completeness caveat |
| US-TRN-EPIC IMPLEMENTED | **85%** | The epic has `acceptance_criteria_count: 0`; "implemented" means its four cross-cutting bullets hold. Included for completeness rather than because it moves anything |

**What story-level depth did NOT allow (stated explicitly at my request):**
- Did not open every component template to confirm each rendered control is *enabled* for the right permission — route guards and service wiring were verified, **not per-button permission hiding.** A `Training.View.Own` user seeing a disabled-but-visible "Create course" button would not surface here.
- Did not trace the notification **templates** behind the 7 registered event keys — only that the keys exist and dispatch post-commit. **A registered key with a missing template would render as a silent no-send.** (Related: ISSUE-270 records these events reuse `NotificationCategory.OnboardingOffboarding` because no Training/Benefits category exists — WONTFIX'd.)
- Did not collapse to per-AC rows, so an AC 80% implemented within an otherwise-passing story could be under-reported. Mitigated by reading all 27 ACs, but the rows carry no per-AC verdicts.

**Genuinely UNVERIFIABLE statically:** the three NFR-1 latency budgets (catalog <500ms/500 courses, plans <500ms/200 plans, eligibility <300ms). Noted: `EligiblePlansForAsync:167-173` pulls **all** Active plans into memory before filtering in LINQ-to-Objects — fine at the NFR's 200-plan ceiling, **so not a flag, just the thing a load test would probe first.**

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** HIGH · **where:** `TEST-STATUS.md:233-235` · **what:** asserts "Actual zero test coverage" for a module with 6 xUnit files (~1,100 lines, 3 Testcontainers-Postgres, AC-banner-commented) plus 6 Karma specs (~90 `it()`). · **suggested-action:** reword to *"no IEEE-829 TC suite / no TEST-STATUS rows (automated xUnit + Karma coverage exists)"*, open `[!]` rows citing this report's gaps, and **re-rank COMPLETION-PLAN P0-2 — authoring TCs here is lower value than closing the FE gaps.**
- **type:** doc-drift · **severity:** LOW · **where:** `docs/BA/INDEX.md:8` · **what:** lists these as "STUBS"; authored 2026-07-09, shipped 2026-07-10.
- **type:** bug · **severity:** MED · **where:** `BenefitEnrollmentService.cs:220` · **what:** `EmployeeStatus` never consulted — a Terminated/Inactive employee is enrollable into any rules-free plan. · **suggested-action:** file as a new BUG; **it is not currently in `TEST-FINDINGS.md` and needs a finding ID to be actionable.**
- **type:** bug · **severity:** MED · **where:** `training.service.ts:108` · **what:** `completeEnrollment` (AC-8) and `getTrainingHistory` (AC-9 HR half) have no component consumer — same orphan class as ISSUE-271 but for Training, and currently unrecorded. · **suggested-action:** extend ISSUE-271 to cover all four orphaned methods under one "HR view of an employee's training & benefits" item. **One employee-detail tab closes all four.**
- **type:** test-integrity · **severity:** LOW · **where:** `training.service.spec.ts:208,243` · **what:** green specs cover service methods no screen invokes — **coverage that conceals a missing UI rather than proving a working feature.** · **suggested-action:** leave the specs (never weaken a test); note in ISSUE-273's batch that **service-level FE coverage without a component consumer should not be read as feature coverage.**
- **type:** doc-drift · **severity:** LOW · **where:** `benefit.models.ts:99` · **what:** declares an error code no backend path can emit.
