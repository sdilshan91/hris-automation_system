# Pass A2 — payroll requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 11 Must-Have stories at AC level (60 ACs) + 2 Should-Have at story level = **62 rows**
> **Status:** ✅ VALIDATED — 4 of 4 orchestrator spot-checks confirmed.
> **Headline:** 🔴 **a money bug — LOP is under-deducted for mid-month joiners and leavers, and no test can catch it.** Plus four write paths dead at the FE/BE contract.

## Orchestrator validation

| Claim | Result |
|---|---|
| **LOP double-proration (money bug)** | ✅ **Confirmed, and worse than reported — the code contradicts its own comment.** `PayrollSlipCalculator.cs:138` states *"BR-2: LOP deduction line. daily_rate = monthly_basic / working_days"*. `:142` computes `var dailyRate = proRatedBasic / workingDays;` where `proRatedBasic` was set at `:126` as `Round(c.MonthlyAmount * proRataFactor)`. The pro-ration is applied twice. |
| No test combines proration with LOP > 0 | ✅ **Confirmed.** Every case in `PayrollSlipCalculatorTests.cs` has either `LopDays: 0` (`:41,68,109,125,149,175,192`) or `ProRataPaidDays: null` (`:85`, the only `LopDays: 3` case). **The two conditions are never combined.** |
| US-PAY-007 contract break (`type` vs `AdjustmentType`) | ✅ **Confirmed.** `grep adjustmentType` over `src/frontend/.../payroll` → **zero hits**; `PayrollAdjustmentsController.cs:171` binds `string AdjustmentType`. |
| RLS is enabled (auditor **rejected** its own sub-explorer here) | ✅ **Confirmed.** `appsettings.json:20-22` — `"Rls": { "Enabled": true }`. The sub-explorer had read a stale `Rls/README.md`; the auditor caught it and overruled. Exactly the behaviour the contract asks for. |

**The auditor also corrected the orchestrator's brief:** the brief said to weight "whether the arithmetic is pinned by real-Postgres tests." For this module that framing is slightly wrong — the money engine (`PayrollSlipCalculator`, `StatutoryCalculator`) is **pure**, so golden-dataset xUnit tests are the *correct* instrument, not a weakness. 15 of 33 payroll integration suites are real Testcontainers Postgres, covering the money-critical paths.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| PAY-001 AC-1 | Create component, tenant-scoped | Must | IMPLEMENTED | `SalaryComponentService.cs:71-101`; `SalaryComponentsController.cs:63-79`; `DependencyInjection.cs:429` | FE contract aligned |
| PAY-001 AC-2 | Edit; historical payslips unchanged | Must | PARTIAL | `SalaryComponentService.cs:104-150` | **leg3**: no test asserts the payslip-immutability half |
| PAY-001 AC-3 | Structure + per-component rules (%/fixed/formula) | Must | IMPLEMENTED | `SalaryStructureService.cs:54-103`; `CtcBreakdownCalculator.cs:105-128`; `SalaryFormula.cs` | Real safe evaluator, not a stub |
| PAY-001 AC-4 | Reorder drives calculation order | Must | PARTIAL | BE ok `CtcBreakdownCalculator.cs:74`; FE dead `payroll.service.ts:86-95` | **leg2**: FE POSTs `/salary-components/reorder`; controller has only 5 routes → 404 |
| PAY-001 AC-5 | Delete blocked, count of affected **employees** | Must | 🔴 **CONTRADICTED** | `SalaryComponentService.cs:229-233` counts `SalaryStructureComponents`; `payroll.service.ts:151-154` | Wrong metric **and** count never on the wire → UI always shows 0 |
| PAY-001 AC-6 | Tenant isolation, RLS at DB level | Must | IMPLEMENTED | `AppDbContext.cs:489-498`; migration `…RlsPolicies_Dormant.cs:135`; `DbInitializer.cs:135-155`; `appsettings.json:20-22` | RLS **is** enabled (see reverse drift) |
| PAY-002 AC-1 | Assignment creates rows computed from CTC | Must | PARTIAL | BE ok `SalaryAssignmentService.cs:231-334`; FE broken `SalaryAssignmentDtos.cs:19-28` vs `employee-salary.models.ts:53-60` | **leg2**: `totalAnnualEarnings/totalMonthlyEarnings/components` vs `totalAnnual/totalMonthly/lines` |
| PAY-002 AC-2 | Future-dated assign; revision history | Must | PARTIAL | `SalaryAssignmentService.cs:252-320`; `SalaryAssignmentDtos.cs:39` vs `employee-salary.models.ts:110` | **leg2**: `Components` vs `lines` — Compensation tab always empty |
| PAY-002 AC-3 | Per-component override persisted | Must | IMPLEMENTED | `CtcBreakdownCalculator.cs:83-89`; `SalaryAssignmentService.cs:298`; `CtcResidualBalancer.cs:81-92` | Request shapes match; display degraded by AC-1 |
| PAY-002 AC-4 | Bulk assignment + progress | Must | PARTIAL | `bulk-salary-assignment.component.ts:364-368` sends `rows`; `SalaryAssignmentDtos.cs:97-103` binds `Employees` | **leg2**: always 400 `no_employees` (`SalaryAssignmentService.cs:89-90`) |
| PAY-002 AC-5 | Cross-tenant denial | Must | IMPLEMENTED | `AppDbContext.cs:500-506`; `SalaryAssignmentIntegrationTests.cs:145` | |
| PAY-003 AC-1 | Queued + Hangfire + 202 + runId | Must | IMPLEMENTED | `PayrollRunService.cs:131`; `PayrollRunsController.cs:32-49` | Idempotency-Key honoured |
| PAY-003 AC-2 | **Locks** attendance/leave, then computes | Must | PARTIAL | `PayrollRunProcessor.cs:295`, `:1082-1094` | **leg1**: run only **reads** a lock via `GetPeriodLockAsync`; never calls `LockPeriodAsync`. FR-4's "lock on Processing" absent |
| PAY-003 AC-3 | Slips persisted → ReviewPending + notify HR | Must | IMPLEMENTED | `PayrollRunProcessor.cs:648-671`; `RealPayrollNotificationService.cs:265-266` | SignalR replaced by 2s polling |
| PAY-003 AC-4 | 409 when period already Finalized | Must | IMPLEMENTED | `PayrollRunService.cs:97-110` | |
| PAY-003 AC-5 | 5,000 employees within 10 min | Must | UNVERIFIABLE | `PayrollRunProcessor.cs:218` (histogram exists) | Needs a seeded 5k load run |
| PAY-003 AC-6 | Skip employee w/o structure, warn, continue | Must | IMPLEMENTED | `PayrollRunProcessor.cs:470-477` | |
| PAY-003 AC-7 | Tenant isolation through the pipeline | Must | IMPLEMENTED | `PayrollRunProcessor.cs:234-239`; `PayslipJobRlsPostgresTests.cs:233` (real PG, RLS on) | |
| PAY-004 AC-1 | Hangfire PDF job, `{tenantId}/payroll/{runId}/{employeeId}.pdf` | Must | IMPLEMENTED | `PayslipStoragePath.cs:17-22`; `GeneratePayslipsJob.cs:27-76` | Path matches AC literally |
| PAY-004 AC-2 | PDF incl. branding, working/paid/LOP days | Must | IMPLEMENTED | `PayslipPdfRenderer.cs:46-131`; `PayslipBatchRenderer.cs:422-459` | All AC fields present |
| PAY-004 AC-3 | Download-All ZIP + per-employee download | Must | PARTIAL | ZIP ok `PayslipGenerationService.cs:273-313`; FE `payslip.models.ts:61` `id` vs `PayslipDtos.cs:39` `SlipId` | **leg2**: per-row download uses `p.id` = `undefined` |
| PAY-004 AC-4 | Cross-tenant blob access denied | Must | IMPLEMENTED | `AppDbContext.cs:521-522`; `LocalFileStorage.cs:93-115` | |
| PAY-004 AC-5 | Regenerate overwrites | Must | IMPLEMENTED | `PayslipGenerationService.cs:57-80` | Broader than spec — also allowed on Finalized runs (deliberate) |
| PAY-005 AC-1 | List Finalized-run payslips only | Must | IMPLEMENTED | `MyPayslipService.cs:61-67` | FE model aligned field-for-field |
| PAY-005 AC-2 | Inline breakdown + Download PDF | Must | IMPLEMENTED | `MyPayslipService.cs:106-186` | Point-in-time dept/designation snapshot |
| PAY-005 AC-3 | Download the US-PAY-004 PDF | Must | IMPLEMENTED | `MyPayslipService.cs:188-219` | |
| PAY-005 AC-4 | 403 (not 404) on another employee's payslip | Must | IMPLEMENTED | `MyPayslipService.cs:230-249`; `MyPayslipIntegrationTests.cs:182-231` | 403/404 split correctly tested |
| PAY-005 AC-5 | Responsive at 360px | Must | UNVERIFIABLE | `my-payslips.component.ts:382-385` | Needs a browser |
| PAY-006 AC-1 | Tax slabs + effective date drive runs | Must | PARTIAL | BE ok `StatutoryCalculator.cs:101-122`; FE `statutory-configuration.component.ts:782` sends `slabs`; `StatutoryRuleDtos.cs:127` binds `TaxSlabs` | **leg2**: save always fails validation |
| PAY-006 AC-2 | EPF employee+employer, ceiling, min(basic,ceiling)×rate | Must | PARTIAL | Math correct `StatutoryCalculator.cs:162-172`; `StatutoryDeductionResolver.cs:213-222` | **leg2**: saved EPF never redisplays → overwrite-with-zeros risk |
| PAY-006 AC-3 | Versioned rules per fiscal year | Must | IMPLEMENTED | `FiscalYearResolver.cs:28-45`; `StatutoryRuleMultiCountryPostgresTests.cs` | |
| PAY-006 AC-4 | Tenant isolation of statutory config | Must | IMPLEMENTED | `AppDbContext.cs:528-542` | |
| PAY-006 AC-5 | Mandatory statutory component cannot be removed | Must | IMPLEMENTED | `SalaryStructureService.cs:320-324,537-559` | 409 `statutory_mandatory` |
| PAY-007 AC-1 | Create adjustment (type/amount/period) | Must | PARTIAL | BE ok `PayrollAdjustmentService.cs:75-151`; FE `adjustment.models.ts:131` `type` vs controller `:171` `AdjustmentType` | **leg2**: create always 400; `adjustmentType` → **0 FE hits** |
| PAY-007 AC-2 | Deduction subtracted from net | Must | IMPLEMENTED | `PayrollRunProcessor.cs:916-917`; `PayrollSlipCalculator.cs:162-174` | Pinned only by InMemory |
| PAY-007 AC-3 | Document at `{tenantId}/payroll/adjustments/{id}/` | Must | IMPLEMENTED | `PayrollAdjustmentService.cs:348-354` | |
| PAY-007 AC-4 | Correction for finalized period → arrears next run | Must | IMPLEMENTED | `PayrollAdjustmentService.cs:421-444`; `PayrollRunProcessor.cs:919-921` | |
| PAY-007 AC-5 | Tenant isolation | Must | IMPLEMENTED | `AppDbContext.cs:544-546` | |
| PAY-008 AC-1 | Submit → AwaitingApproval + notify approvers | Must | IMPLEMENTED | `PayrollApprovalService.cs:77-136` | |
| PAY-008 AC-2 | Approve → Approved | Must | IMPLEMENTED | `PayrollApprovalService.cs:142-242` | |
| PAY-008 AC-3 | Reject w/ reason → Rejected | Must | IMPLEMENTED | `PayrollApprovalService.cs:248-289` | ≥10-char reason enforced |
| PAY-008 AC-4 | Multi-step sequential routing | Must | IMPLEMENTED | `PayrollApprovalService.cs:194-230`; `PayrollApprovalStepConfigPostgresTests.cs` (real PG) | Genuinely per-step |
| PAY-008 AC-5 | Finalize → records immutable | Must | PARTIAL | `PayrollApprovalService.cs:338-386` | **leg1**: no lock flag/interceptor; immutability enforced ad hoc per write path |
| **PAY-010 AC-1** | LOP days + LOP = (monthly_basic/working_days)×days | Must | 🔴 **PARTIAL — money bug** | `PayrollSlipCalculator.cs:142` | **leg1**: daily rate uses the **already pro-rated** basic ÷ full working days → **LOP under-deducted for mid-month joiners/leavers.** **leg3**: no test combines proration with LOP>0 |
| PAY-010 AC-2 | Overtime at tenant-configured multiplier | Must | IMPLEMENTED | `PayrollOvertimeCalculator.cs:61-95`; `AttendanceSettings.cs:152-158` | Weekday/weekend/holiday multipliers are tenant columns |
| PAY-010 AC-3 | Leave encashment → next run earning adjustment | Must | IMPLEMENTED | `LeaveEncashmentService.cs:150-190`; `PayrollRunProcessor.cs:1019-1046` | |
| PAY-010 AC-4 | Block run when attendance not finalized | Must | IMPLEMENTED | `PayrollRunService.cs:112-121,494-509` | Hard 409, fail-closed |
| PAY-010 AC-5 | Tenant-scoped attendance/leave fetch | Must | IMPLEMENTED | `PayrollRunProcessor.cs:1101-1190` | |
| PAY-012 AC-1 | History list incl. Initiated By / Approved By | Must | PARTIAL | `PayrollAuditService.cs:58-120`; `PayrollAuditDtos.cs:20,22` | **leg2**: BE emits raw GUIDs; UI shows a GUID, not a person |
| PAY-012 AC-2 | Run detail + audit timeline | Must | PARTIAL | `PayrollApprovalDtos.cs:168` vs `approval.models.ts:138` | **leg2**: FE reads `actorName`; BE emits only `actorUserId` → renders "—" always |
| PAY-012 AC-3 | Audit entry w/ before/after JSON, IP, UA | Must | IMPLEMENTED | `PayrollAuditLogger.cs:74-103` | Real snapshots, not nulls |
| PAY-012 AC-4 | 30-day payroll audit query + export | Must | IMPLEMENTED | `PayrollAuditService.cs:124-265` | |
| PAY-012 AC-5 | Tenant isolation on audit_log | Must | IMPLEMENTED | `PayrollAuditService.cs:156,193-204,227` (explicit predicate — `audit_log` is not a `BaseEntity`) + RLS policy | |
| PAY-013 AC-1 | Effective-dated `TenantFnFPolicy` + policy API | Must | PARTIAL | BE ok `FnFPolicyService.cs:38-60` | **leg2**: **zero** FE surface. Documented Phase-2 deferral |
| PAY-013 AC-2 | Policy version pinned; safe default | Must | IMPLEMENTED | `RealPayrollFnFIntegration.cs:269`; `FnFPolicyService.cs:83-107` | |
| PAY-013 AC-3 | Settlement computed on offboarding completion | Must | IMPLEMENTED | `DependencyInjection.cs:384` registers **`RealPayrollFnFIntegration`**, not the LogOnly stub | |
| PAY-013 AC-4 | Idempotent per offboarding instance | Must | IMPLEMENTED | migration `…FnFSettlement.cs:96-101` (partial unique index); `RealPayrollFnFIntegration.cs:284-287` (23505 recovery); `FinalSettlementPostgresTests.cs` | |
| PAY-013 AC-5 | Country chain, statutory skip+flag, net floored at 0 | Must | IMPLEMENTED | `RealPayrollFnFIntegration.cs:165-222,268-271,331-363` | |
| PAY-013 AC-6 | Run excludes settlement-owned final period | Must | IMPLEMENTED | `PayrollRunProcessor.cs:241-261` | Money-critical double-pay guard, real |
| PAY-013 AC-7 | Tenant isolation + RLS policy | Must | IMPLEMENTED | migration `…FnFSettlement.cs:135` | |
| **US-PAY-009** | Payroll reports & analytics | Should | PARTIAL | All 8 FR-1 types `PayrollReportService.cs:112-123,279-1102`; ClosedXML `PayrollReportRenderer.cs:76-113`. **FE contract aligned** | **leg1**: FR-6 per-tenant bank-advice format absent — hardcoded `:960-963` |
| **US-PAY-011** | Bulk payslip email distribution | Should | PARTIAL | BE complete `PayslipDistributionRunner.cs:176-183,293-296`; `RealPayslipEmailSender` registered | **leg2**: "Send Payslips" button **permanently disabled** (`payslip-distribution.component.ts:428-430,497`). **leg1**: FR-6 rate limit hardcoded `MaxEmailsPerMinute = 0` |

---

## CONTRADICTIONS

### 🔴 The money bug — US-PAY-010 AC-1

`PayrollSlipCalculator.cs:126` sets `proRatedBasic = Round(c.MonthlyAmount * proRataFactor)`.
`:138` comments *"BR-2: LOP deduction line. daily_rate = monthly_basic / working_days"*.
`:142` computes `var dailyRate = proRatedBasic / workingDays;`

**The pro-ration is applied twice, and the code contradicts its own comment.**

Worked example — 22 working days, BASIC 22,000, employee joins mid-month (`ProRataPaidDays = 11`), 2 LOP days:
- Code deducts `(11,000 / 22) × 2 = 1,000`
- Correct per AC: `(22,000 / 22) × 2 = 2,000`
- **Under-deducted by exactly `(1 − proRataFactor)`.**

**No test can catch it.** Every case in `PayrollSlipCalculatorTests.cs` has either `LopDays: 0` (`:41,68,109,125,149,175,192`) or `ProRataPaidDays: null` (`:85`). The Postgres joiner test `PayrollWorkingDaysDenominatorTests.cs:206` is also LOP-free. **The one test that would settle it:** `WorkingDays: 22, ProRataPaidDays: 11, LopDays: 2` → assert `2,000`, not `1,000`.

### Four write paths dead at the FE/BE contract, all marked `[x]` in STATUS.md

| Story | STATUS.md claim | Reality |
|---|---|---|
| US-PAY-002 (PR #64) | done | `bulk-salary-assignment.component.ts:364-368` sends `rows`; DTO binds `Employees` → **400 `no_employees`, always** |
| US-PAY-006 (PR #68) | done | `statutory-configuration.component.ts:782` sends `slabs`; DTO binds `TaxSlabs`; validator `NotEmpty()` rejects → **income-tax slabs cannot be saved from the UI** |
| US-PAY-007 (PR #69) | done | FE `type` vs BE `AdjustmentType`; `adjustmentType` → **0 FE hits** → **every adjustment created via the UI 400s** |
| US-PAY-011 (PR #73) | done | FE reads `g.generatedCount`; BE emits `generated` → `undefined > 0` false → **Send button can never enable** |

### US-PAY-001 AC-5
AC requires "count of affected **employees**". Code counts `SalaryStructureComponents` and the count never reaches the wire, so the modal always reads *"in use by 0 active employee(s)"*. `TEST-FINDINGS.md:2681` records this correctly as OPEN — **only `STATUS.md` is wrong.**

### Reverse drift (docs claim broken/deferred; code shows shipped)
- **RLS is not dormant.** `Rls/README.md` and `SalaryComponentConfiguration.cs:9-10` say RLS is deferred and disabled. Actual: `appsettings.json:20-22` `"Enabled": true`, and `DbInitializer.cs:135-155` runs `ENABLE ROW LEVEL SECURITY` + `FORCE` over every `tenant_id`-bearing table at startup. **The docs are ~1 month stale.** *(Residual caveat correctly warned in code at `DbInitializer.cs:127-133`: a superuser/`BYPASSRLS` connection makes enforcement inert — an environment question.)*
- **Attendance hard-block shipped.** `PayrollRunProcessor.cs:288-291` says hard enforcement "is deferred". It is not — `PayrollRunService.cs:112-121` returns 409 `attendance_not_finalized`.
- **ISSUE-108 fixed but still OPEN.** `SalaryStructureService.cs` now audits all seven write paths.
- **Year-end tax statement is not a stub** despite the enum doc-comment saying so.

---

## GAPS RANKED

1. **FE/BE contract drift kills four write paths — S each, ~M total.** Fix: rename FE model fields to the wire names, then replace hand-mocked Karma fixtures with the real DTO shape. **Every one is masked by a green spec mocking the FE's own invented shape** — `payroll.service.spec.ts:179,201` even mocks two endpoints (`/salary-components/reorder`, `/validate-formula`) that **do not exist on the controller at all**.
2. **🔴 US-PAY-010 AC-1 — LOP under-deduction. Money bug. S.** Fix: divide by `paidDaysBeforeLop`, or use the un-pro-rated basic. Add the combined test.
3. **US-PAY-003 AC-2 — the run never acquires the period lock. S.** `AttendancePayrollService.cs:241 LockPeriodAsync` is never invoked from any run path. HR must lock manually, so attendance can still be edited mid-run.
4. **US-PAY-001 AC-5 — wrong metric + no wire field. S.**
5. **US-PAY-004 — YTD on the payslip PDF is a hardcoded stub. S.** `PayslipBatchRenderer.cs:504` `TenantYtdEnabled() => false`, while the sibling employee-portal path correctly reads the same column at `MyPayslipService.cs:306-310`. **A tenant that enables YTD never gets it on the PDF.**
6. **US-PAY-012 AC-1/AC-2 — actor names never resolved. S.** The pattern to copy already exists: `PayrollApprovalService.cs:582-608 ResolveSubmitterNamesAsync`.
7. **US-PAY-011 FR-6 rate limiting absent. S.** `MaxEmailsPerMinute = 0`, not tenant-configurable.
8. **US-PAY-009 FR-6 bank-advice format not configurable. M.**
9. **US-PAY-013 AC-1 has no UI. M — decision, not defect.** `STATUS.md:209` explicitly defers the FE policy UI to Phase 2.
10. **US-PAY-008 AC-5 — immutability distributed, not enforced. M.** No `IsLocked` flag or interceptor; each new write path must remember its own Finalized check. Latent risk, not a present defect.
11. **US-PAY-007 AC-2 pinned only by InMemory. S.** Worth a Postgres arm given `numeric(18,2)` semantics.

---

## COVERAGE SUMMARY

```
Requirements audited: 62 | IMPLEMENTED: 42 | PARTIAL: 17 | MISSING: 0 | UNVERIFIABLE: 2 | CONTRADICTED: 1
```

**Where the failures concentrate.** Of 18 non-clean verdicts, **12 fail at leg 2 — the frontend/backend contract** — versus 5 at leg 1 and 1 at leg 3. The pilot lesson reproduces almost exactly.

**Zero MISSING.** The payroll backend is genuinely built, deeply so — **no NoOp/stub is registered in DI anywhere in this module** (`LogOnlyPayslipEmailSender` exists but nothing registers it; `RealPayrollFnFIntegration`, `RealPayslipEmailSender`, `RealPayrollNotificationService` are the live registrations). All 22 Angular payroll components are routed or embedded in a routed parent — no orphans.

**The drift is concentrated in the earliest-built stories** (US-PAY-001/002/006/007), whose FE models carry a header comment describing an *"assumed contract"*. The later ones (my-payslips, distribution, reconciliation, reports) are aligned field-for-field. **The team learned; the early code was never revisited.**

---

## CONFIDENCE

- **Every FE/BE mismatch: 97%** — both sides re-read directly, exact fields quoted, none taken on a sub-explorer's word. The one claim **rejected** on verification was "RLS is dormant" — the sub-explorer read a stale README instead of `appsettings.json` + `DbInitializer`.
- **LOP double-proration: 85%** — arithmetic and test gap certain; the 15% is whether an upstream caller pre-scales `LopDays`. *Settled by one test.* **(Orchestrator verified the code and the test gap directly.)**
- **PAY-003 AC-5 (5k/10min) and PAY-005 AC-5 (360px): UNVERIFIABLE by construction.**
- **PAY-008 AC-5, PAY-013 AC-1: 80%** — judgement calls on AC intent, read strictly.
- **Overall: 90%.** Limits: static reading only, so "the button is permanently disabled" is traced not observed; one of five evidence sweeps had not returned, but both its stories were audited directly with own-cited `file:line`. Note 29 payroll TCs sit at `status: fail` and 12 at `blocked` — corroborating, but driving no verdict here.

---

## Addendum — late sub-explorer (US-PAY-010 / US-PAY-013 deep evidence)

The payroll auditor noted one of five evidence sweeps had not returned when it finished. It returned
afterwards. Most of it corroborates; **one claim is wrong and is overruled here.**

### ⚠ Overruled: the sub-explorer disputes the money bug, and is mistaken

It reports of `PayrollSlipCalculator.cs:137-148`:
> *"`var dailyRate = proRatedBasic / workingDays;` — **exact formula match**"* for AC-1's
> `daily_rate = monthly_basic / working_days`.

**It is not a match.** `proRatedBasic` is assigned at `:126` as `Round(c.MonthlyAmount * proRataFactor)`
— already pro-rated. Dividing *that* by full `workingDays` applies the pro-ration a second time. The
sub-explorer read the LOP line in isolation and did not trace `proRatedBasic` to its assignment.

**The orchestrator verified this directly** (read `:118-150` in full) and confirms the auditor's
`PARTIAL` verdict. The money bug stands. Recorded because it is a clean illustration of why the
evidence bar requires tracing a value to its source rather than pattern-matching a formula.

### Corroborated by the sub-explorer

- **`LogOnlyPayrollFnFIntegration` does not exist in the codebase** — `grep` returns nothing. The real
  `RealPayrollFnFIntegration` is registered at `DependencyInjection.cs:384`, and
  `OffboardingIntegrationTests.cs:225` is an explicit regression guard: *"a revert to LogOnly leaves no row → this fails."*
- **FR-6 lock-on-Processing is not implemented as specified.** `LockPeriodAsync` is never called from
  any run path. The design *inverts* FR-6: HR must lock **before** Initiate is allowed (AC-4's gate),
  rather than the run locking on entering Processing. Functionally similar protection, not the
  documented mechanism.
- **US-PAY-013 RLS policies are created DORMANT** for the three F&F tables (migration
  `…FnFSettlement.cs:118-141`, `CREATE POLICY` with no `ENABLE`), which the story's AC-7 explicitly
  says. `RlsIsolationPostgresTests.cs:126-145` dynamically discovers every `tenant_id` table and
  `ENABLE + FORCE`s it, so the dormant policies are proven functionally correct once the runtime
  reconciler activates them. **Consistent with the module-wide RLS finding, not in conflict with it.**
- **Reconciliation and encashment DTOs are aligned field-for-field** with their FE models — no
  structural mismatch. This is a genuine clean bill for the later-built payroll surfaces.

### New gaps it adds

- **US-PAY-010 AC-4 — the 409 negative path has no test.** Every integration fixture calls a
  `LockAttendance()` helper *specifically to avoid* triggering `attendance_not_finalized`
  (`PayrollRunIntegrationTests.cs:170`, `PayrollAdjustmentIntegrationTests.cs:176`). The gate exists
  and is correct; **nothing asserts it rejects.** `TC-PAY-010-04` is a manual TC marked `pass` with no
  linked automated test. **Size: S**
- **US-PAY-010 AC-4 — a narrow race.** `PayrollRunService.InitiateAsync` hard-blocks, but
  `PayrollRunProcessor.cs:287-300` (the async Hangfire stage) re-checks softly: if the lock was released
  between Initiate and Process it **skips LOP with a log note rather than failing the run.** Silent
  under-deduction. **Size: S**
- **US-PAY-013 AC-4 — the concurrent-retry recovery branch is untested.** The Postgres test proves the
  DB rejects a duplicate insert with `23505`; no test races two concurrent `TriggerFinalSettlementAsync`
  calls to exercise the `catch (DbUpdateException)` recovery at `RealPayrollFnFIntegration.cs:280-297`. **Size: S**
- **US-PAY-013 AC-6 — the double-pay exclusion guard is tested only on EF InMemory**, never on real
  Postgres, despite being the money-critical guard that stops an offboarded employee being paid twice. **Size: S**

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** HIGH · **where:** `docs/QA/TEST-FINDINGS.md:2542,2553,2564` vs `:2631,2670,2681` · **what:** finding IDs **BUG-060, ISSUE-108 and ISSUE-109 are each used TWICE** — once for Recruitment, once for Payroll — and the resolution roll-up at `:56-58` credits "#169 ISSUE-109" without saying which. · **suggested-action:** renumber the payroll trio, and **re-audit every "RESOLVED" line citing a colliding ID — a fix to one module currently reads as closing the other's finding.**
- **type:** test-integrity · **severity:** HIGH · **where:** `payroll.service.spec.ts:179,201` · **what:** Karma specs assert against `POST /payroll/salary-components/reorder` and `/validate-formula` — **neither route exists** on the controller. Green specs standing over two endpoints that 404. Same pattern in `statutory-configuration.component.spec.ts`, `adjustment-form.component.spec.ts`, `payslip-list.component.spec.ts`, `payslip-distribution.component.spec.ts`, `employee-salary.service.spec.ts`. · **suggested-action:** hand to `@test-authenticator`; consider generating FE models from the OpenAPI schema so this class of drift cannot compile.
- **type:** bug · **severity:** MED · **where:** `payroll-adjustments.component.ts:529` · **what:** `a.type.localeCompare(b.type)` — with the real API payload `a.type` is undefined, so sorting by Type throws a TypeError rather than degrading. · **suggested-action:** fix with the field rename; add a null-safe comparator regardless.
- **type:** risk · **severity:** MED · **where:** `DbInitializer.cs:127-133` · **what:** RLS is ENABLE+FORCE'd at startup, but if `DefaultConnection` points at a superuser/`BYPASSRLS` role the enforcement is **silently inert** — the code logs a warning and continues. Several payroll ACs claim "RLS enforces isolation at the database level". · **suggested-action:** verify the deployed role is `hrm_app` (NOBYPASSRLS) per `Rls/roles.sql`; **consider failing startup rather than warning** when `Rls:Enabled=true` and the role bypasses.
