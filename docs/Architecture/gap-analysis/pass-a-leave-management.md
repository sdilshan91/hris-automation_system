# Pass A0 (PILOT) — leave-management requirements audit

> **Run:** 2026-08-08 · **Auditor:** `@requirements-auditor` contract · **Tree:** `test/local-subdomains` (810 ahead of `main`)
> **Status:** ✅ VALIDATED — 4 of 4 orchestrator spot-checks confirmed against the cited files.
> **Depth:** 8 Must-Have stories at AC level (43 ACs) + 4 Should-Have stories at story level = 47 rows.
> Raw agent output, preserved verbatim below the validation note.

## Orchestrator validation (the A-gate)

Four claims were re-verified by hand before accepting this audit:

| Claim | Result |
|---|---|
| US-LV-002 AC-7 `LocationId` entitlement tier MISSING | ✅ **Confirmed.** `LeaveEntitlementRule` fields are `LeaveTypeId, DepartmentId, JobTitleId, JobLevelId, EmploymentType, TenureMin/MaxMonths, EntitlementDays, Priority, EffectiveFrom/To, IsActive`. No `LocationId`. |
| US-LV-003 AC-7 FE hardcodes Mon–Fri | ✅ **Confirmed.** `leave-request.models.ts` — `const day = cursor.getDay(); if (day !== 0 && day !== 6) { count++; }` |
| Redis balance-cache invalidation absent | ✅ **Confirmed**, and the auditor's nuance was right: the code comments say *"Redis caching is DEFERRED module-wide (vault decision); the ledger total is the single source of truth."* An unmet AC by deliberate decision, not a live defect. |
| US-LV-012 FE/BE contract mismatch | ✅ **Confirmed.** BE `LeaveReportResult` emits `Columns: string[]` + `Rows: [{ Cells: string[] }]`. FE `IReportPage` expects `items: IReportRow[]` where `IReportRow = Record<string, …>`. The shapes cannot interoperate. |

The auditor also **corrected the orchestrator's brief on two factual points** — the module's story
prefix is `US-LV-*` not `US-LEV-*`, and ISSUE-197 is a payroll CTC employer-contribution fix
(US-RPT-003), not leave/holiday rule batching. Pushing back on the brief rather than confirming it is
the behaviour the gate was designed to test. **Calibration accepted; cleared for fan-out.**

---

## SCOPE

**Module:** `leave-management` (BA), 12 stories `US-LV-001..012` — the actual ID prefix is **`US-LV`**, not `US-LEV`.
**Depth:** 8 `Must Have` stories at one row per AC (43 ACs); 4 `Should Have` stories (`008, 009, 011, 012`) at one row per story. Total 47 rows.
**Tree audited:** content-identical to `test/local-subdomains` — the de-facto trunk, 810 ahead of stale `main`.
**Method:** 5 parallel read-only explorers, each claim spot-verified. No tests executed; test *existence* recorded only.

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| LV-001 AC-1 | Create leave type (name/code/color/entitlement/accrual freq/carry-fwd/probation), tenant-scoped | Must | IMPLEMENTED | `HRM.Domain/Entities/LeaveType.cs:15,20,25,36,41,47,53,58`; filter `HRM.Infrastructure/Persistence/AppDbContext.cs:349-351`; route `HRM.Api/Controllers/LeaveTypesController.cs:69-96`; migration `20260613035822_AddLeaveTypeEntity.cs:24-29`; test `HRM.Tests/Unit/LeaveTypeServiceTests.cs:121,396` | All 3 legs |
| LV-001 AC-2 | Edit → before/after audit; next accrual cycle | Must | IMPLEMENTED | `LeaveTypeService.cs:142,176,414-430`; test `LeaveTypeServiceAuditTests.cs:140` | BUG-025 fixed. "Next cycle" = job re-reads DB, no `EffectiveFrom` field (drift) |
| LV-001 AC-3 | Duplicate name → exact message | Must | IMPLEMENTED | `LeaveTypeService.cs:47-52,128-133`; unique idx `LeaveTypeConfiguration.cs:116-118`; test `LeaveTypeServiceTests.cs:188` | Message has trailing period vs AC text |
| LV-001 AC-4 | Deactivate → cannot apply; approved unaffected | Must | IMPLEMENTED | `LeaveRequestService.cs:119-120`; `LeaveTypeService.cs:191-224` | **leg3 thin**: no automated test on the apply-time guard; only manual `TC-LV-004.md` |
| LV-001 AC-5 | Documents-required rule + day threshold, enforced on apply | Must | IMPLEMENTED | `LeaveType.cs:63,70`; enforcement `LeaveRequestService.cs:186-194`; test `LeaveRequestServiceTests.cs:280,294,307` | Fields named `DocumentsRequired`/`DocumentDayThreshold` (drift) |
| LV-002 AC-1 | Rule maps leave type → dept + **job level**, N days/yr | Must | PARTIAL | `HRM.Domain/Entities/LeaveEntitlementRule.cs:15-72`; **stub** `LeaveEntitlementService.cs:1141-1145` | **leg1**: any `JobLevelId` is hard-rejected ("Job level not found."); no `JobLevel` entity exists. Job *title* is the delivered proxy |
| LV-002 AC-2 | Most-specific rule wins, consistent order | Must | PARTIAL | `LeaveEntitlementEngine.cs:69-76,83-89`; tests `LeaveEntitlementEngineTests.cs:28-168` (9 arms) | Ranking real & deterministic, but scores dept/**job title**/employment-type — the AC's job-**level** tier does not exist |
| LV-002 AC-3 | Per-employee override beats all rules | Must | IMPLEMENTED | `LeaveEntitlementService.cs:1054-1067`, batch `:546-551`; test `LeaveEntitlementServiceTests.cs:297,605` | Override queried first, returns immediately |
| LV-002 AC-4 | Mid-year joiner pro-rated by join date + accrual frequency | Must | IMPLEMENTED | `LeaveEntitlementEngine.cs:100-146`; `LeaveEntitlementService.cs:1010-1029,1038-1046`; test `LeaveEntitlementEngineTests.cs:190-297` | — |
| LV-002 AC-5 | Rule edit → Hangfire recalc enqueued; audit-logged | Must | IMPLEMENTED | Enqueue `LeaveEntitlementService.cs:152-163`; audit `:143,1270-1285`; scheduler `HRM.Api/Jobs/HangfireLeaveEntitlementRecalcScheduler.cs:23-25`; test `LeaveEntitlementServiceTests.cs:707` | BUG-118 fixed. Audit row itself has **no** test (code-verified only) |
| LV-002 AC-6 | FTE 0.5 × 20 days = exactly 10, wired into `CalculateProRata` | Must | IMPLEMENTED | `LeaveEntitlementEngine.cs:110,139`; all 4 call sites pass `employee.Fte`: `LeaveEntitlementService.cs:452,568,863,915`; tests `LeaveEntitlementEngineTests.cs:266`, `Integration/FteProrationTests.cs:172,199,230` | `fte = 1.0m` is a *parameter default*, never the production path |
| LV-002 AC-7 | `LocationId` tier ranks below override, above other dims | Must | **MISSING** | Absent from `LeaveEntitlementRule.cs`, `LeaveEntitlementEngine.cs`, `LeaveEntitlementRuleConfiguration.cs`, all migrations, DTO, controller, FE form (direct grep, exit 1) | All 3 legs fail. Yet `docs/QA/leave-management/TC-LV-ISO-049.md` was authored for it |
| LV-003 AC-1 | Submit → Pending + manager notification + confirmation | Must | IMPLEMENTED | `LeaveRequestService.cs:276,315-316`; impl `LeaveNotificationService.cs:50-63`; DI `DependencyInjection.cs:351`; test `LeaveRequestServiceTests.cs:180,195` | Event key `leave.requested` vs doc `leave-requested` (drift) |
| LV-003 AC-2 | Inline balance; insufficient → blocked | Must | PARTIAL | Backend `LeaveRequestService.cs:207-238`; preview `:426-483` + `LeaveRequestsController.cs:108-127` | **leg2 (FE)**: Angular never calls `balance-preview` (0 refs); FE branch on `code==='insufficient_balance'` is unreachable — `LeaveRequestsController.cs:55` emits no error code |
| LV-003 AC-3 | Sick > threshold, no doc → medical-certificate error | Must | IMPLEMENTED | `LeaveRequestService.cs:186-194`; test `LeaveRequestServiceTests.cs:280,294,307` (boundary covered) | — |
| LV-003 AC-4 | Half-day AM/PM → 0.5 days | Must | IMPLEMENTED | `LeaveRequestService.cs:160-167,270-272`; validator `CreateLeaveRequestValidator.cs:32-49`; test `LeaveRequestServiceTests.cs:209` | — |
| LV-003 AC-5 | Overlap → exact message | Must | IMPLEMENTED | `LeaveRequestService.cs:196-205` (exact string, 409); test `LeaveRequestServiceTests.cs:322` | Returns 409 not 400 |
| LV-003 AC-6 | Public holidays excluded; employee informed of adjusted count | Must | PARTIAL | `LeaveRequestService.cs:170-174`; provider `HolidayProvider.cs:40-50`; DI `DependencyInjection.cs:342`; test `Integration/HolidayIntegrationTests.cs:190` | Exclusion solid. "Informed" weak — FE `countWorkingDays` has **no holiday awareness**; user sees adjusted count only post-submit |
| LV-003 AC-7 | Shift working-day set drives day-count + half-day gate (BUG-284) | Must | IMPLEMENTED | Resolver `LeaveRequestService.cs:1660-1673` → `ShiftScheduleResolver.cs:64-169`; count `:157,173`; half-day gate `:163-165`; tests `Integration/LeaveWorkingDaysLocationTests.cs:249,278,316,348,406,578` | Server-authoritative and correct. **FE still hardcodes Mon–Fri** (see gaps) |
| LV-004 AC-1 | Direct reports, oldest-first, + inline balance | Must | IMPLEMENTED | `LeaveRequestService.cs:517-521,565,598-600,611-626`; DTO `LeaveRequestDtos.cs:96,208`; test `PendingLeaveQueueServiceTests.cs:348,362` | Balance genuinely in the DTO |
| LV-004 AC-2 | Server-side pagination, default 20, total count | Must | IMPLEMENTED | `LeaveRequestService.cs:557,570-573`; default `:497`, `LeaveRequestsController.cs:206`; test `PendingLeaveQueueServiceTests.cs:224,241` | — |
| LV-004 AC-3 | Filters: leave type, employee, date range | Must | IMPLEMENTED | `LeaveRequestService.cs:545-555`; test `PendingLeaveQueueServiceTests.cs:308,320,332` | — |
| LV-004 AC-4 | Detail panel: photo, color, attachments (downloadable), balance, last-3 history, team-calendar snippet | Must | PARTIAL | Present: `LeaveRequestDtos.cs:84,88`, `LeaveRequestService.cs:616,619`. Missing: `leave-approvals.component.ts:465-466,472-479,481-488` | **leg1**: attachments not downloadable (`HasAttachments` bool only); last-3 history + team-calendar snippet are **literal TODO placeholders rendered in the production UI** |
| LV-004 AC-5 | New request while viewing → auto-refresh or banner | Must | PARTIAL | Manual button only: `leave-approvals.component.ts:121-135` with TODO at `:122-123`; backend TODO `LeaveRequestService.cs:632-636` | **leg2**: hub exists (`HRM.Api/Hubs/NotificationHub.cs`) and FE has a SignalR client; approvals screen never subscribes. No polling |
| LV-005 AC-1 | Approve → status + "used" ledger + audit + notification + **Redis invalidation** | Must | PARTIAL | 4/5 real: `LeaveRequestService.cs:926,920-923,936,960-961`, audit writer `:1532-1554` | **leg1**: Redis invalidation is a TODO only — `LeaveRequestService.cs:955-957` |
| LV-005 AC-2 | Reject w/ mandatory reason, no deduction, audit, notification+reason | Must | IMPLEMENTED | `LeaveRequestService.cs:981-982,999,1006,1025-1026`; `LedgerEntryId=null` `:1034`; test `LeaveApprovalServiceTests.cs:228,254` | — |
| LV-005 AC-3 | Balance insufficient at approval → warn+confirm, or block | Must | PARTIAL | Block branch real: `LeaveRequestService.cs:893-898`, hard floor `:904-912` (BUG-029 fixed) | **leg2**: warn/confirm path is dead — `ApproveLeaveRequestRequest` has only `Comment` (`LeaveRequestDtos.cs:122-126`); controller drops error codes (`LeaveRequestsController.cs:294`). FE modal keys on fields the API cannot emit |
| LV-005 AC-4 | Multi-level → "Pending L2 Approval" + notify next approver | Must | PARTIAL | Multi-level **is** real: `LeaveRequestService.cs:290-303,865-868,1052-1133`; runtime `WorkflowRuntimeService.cs:293-305`; next-approver notify `:645-651`; test `Integration/WorkflowEntityWiringPostgresTests.cs:519-536` | No `PendingL2Approval` in `LeaveRequestStatus.cs`; request stays `Pending`. Queue is direct-reports-only (`LeaveRequestService.cs:515-516`) so an L2 non-manager never sees it |
| LV-005 AC-5 | Concurrency → first wins, second 409, via xmin | Must | IMPLEMENTED | `HRM.Domain/Entities/LeaveRequest.cs:73-78`; `LeaveRequestConfiguration.cs:82-91` (`IsRowVersion()` → `xmin`); messages `LeaveRequestService.cs:945,1015`; tests `Integration/LeaveApprovalIntegrationTests.cs:247,273,308` | Suite runs InMemory, which ignores xmin (documented `:18-29`); token exercised only on real PG |
| LV-006 AC-1 | Card per active type: entitlement/used/pending/balance + progress bar | Must | IMPLEMENTED | `LeaveDashboardService.cs:75-217`; FE `leave-dashboard.component.ts:141-200,579-581`; test `LeaveDashboardServiceTests.cs:196,260` | Progress indicator is a circular SVG arc, not a linear bar (cosmetic drift) |
| LV-006 AC-2 | Click card → full ledger for the leave year | Must | PARTIAL | `LeaveDashboardService.cs:223-260`; FE `leave-dashboard.component.ts:322-364` | **leg2**: template tracks `e.ledgerId` (`:350`) but DTO serialises `id` (`LeaveDashboardDtos.cs:50`) → every `@for` key undefined. FE spec fixtures use the invented field |
| LV-006 AC-3 | Upcoming Leaves: approved + pending future | Must | PARTIAL | `LeaveDashboardService.cs:266-305`; FE `leave-dashboard.component.ts:243-266` | Same defect class: `track u.leaveRequestId` vs DTO `RequestId` (`LeaveDashboardDtos.cs:70`) |
| LV-006 AC-4 | Mobile 360px: cards stack, readable, bars scale | Must | PARTIAL | `leave-dashboard.component.ts:373` (`grid-cols-1 sm:grid-cols-2 lg:grid-cols-3`), `:86,258,398` | **leg3**: `TC-LV-127.md` status **blocked**, never executed. Arc is fixed 120×120 (`:159`); ledger table has no `overflow-x-auto` (`:340`) |
| LV-006 AC-5 | Empty state with exact copy + illustration | Must | IMPLEMENTED | `LeaveDashboardService.cs:107-109`; FE `leave-dashboard.component.ts:127-139` (inline SVG `:130-134`, exact string `:137`); test `LeaveDashboardServiceTests.cs:242` | — |
| LV-006 AC-6 | Fiscal leave year bounds balances, ledger **and year selector** (ISSUE-305) | Must | PARTIAL | Backend done: `HRM.Domain/Leave/LeaveYear.cs:44-53,63-69`; `TenantLeaveYearResolver.cs:33-52`; `Tenant.cs:40` + migration `20260617010853_Admin_TenantCompanySettings.cs:59`; tests `Unit/LeaveYearTests.cs:32-148`, `Integration/FiscalLeaveYearIntegrationTests.cs:202,272` | **leg2 (FE)**: selector calendar-only — `leave-dashboard.component.ts:413,417` use `new Date().getFullYear()`; `grep -i fiscal` over FE leave module returns nothing. FE always sends explicit `year`, bypassing the backend fiscal default |
| LV-007 AC-1 | Holiday w/ name, date, type, locations; tenant-scoped | Must | IMPLEMENTED | `HRM.Domain/Entities/Holiday.cs:16,21,27,34`; filter `AppDbContext.cs:373-375`; `HolidayService.cs:248-249`; migration `20260614045054_AddHolidayEntity.cs:14-61`; test `Integration/HolidayIntegrationTests.cs:135,174` | Single nullable `LocationId` matches the story's own FR-2/§7 — not a gap |
| LV-007 AC-2 | Holiday excluded from leave day count | Must | IMPLEMENTED | `HolidayProvider.cs:40-50`; `LeaveRequestService.cs:171-174`; DI `DependencyInjection.cs:342`; test `HolidayIntegrationTests.cs:190` (5 days − 1 holiday = 4) | Real provider registered, not the NoOp |
| LV-007 AC-3 | CSV import w/ validation; same-date duplicates flagged | Must | IMPLEMENTED | `HolidayService.cs:270-388`, dup flagging `:337-344`; route `HolidaysController.cs:141-160`; tests `Unit/HolidayServiceTests.cs:403,419,437` | Rows always tenant-wide (no per-row location column) |
| LV-007 AC-4 | Calendar month/year view, color-coded + list view | Must | IMPLEMENTED | `holiday-calendar.component.ts:43,106-108,158-160,285-287`; route `leave-management.routes.ts:29-34`; spec `holiday-calendar.component.spec.ts:143,165,192` | — |
| LV-010 AC-1 | Cancel pending → Cancelled, no balance impact, notify, audit | Must | PARTIAL | `LeaveRequestService.cs:1402-1408`, no ledger (guarded `:1293`), notify `:1434-1435`; test `CancelLeaveRequestServiceTests.cs:165,191` | **leg1**: writes `LeaveApprovalHistory` + a log line but **no `AuditLog` row** — `AddDecisionAudit` (`:1532-1554`) is called by approve/reject only |
| LV-010 AC-2 | Cancel approved future → positive `adjusted` reversal + **Redis invalidation** + notify + audit | Must | PARTIAL | Reversal real: `LeaveRequestService.cs:1293-1400` (per-pool, `restoreAmount = -used.Amount` `:1335`); test `CancelLeaveRequestServiceTests.cs:205` | **leg1**: Redis invalidation TODO at `:1429-1431`; audit-row gap as AC-1 |
| LV-010 AC-3 | Already started → exact block message | Must | IMPLEMENTED | `LeaveRequestService.cs:1269-1280` (exact string); tenant window `:1273-1278`; tests `CancelLeaveRequestServiceTests.cs:271,311,342` | "Tenant policy" clause = notice window only; no partial-cancellation toggle exists |
| LV-010 AC-4 | Payroll-locked period → exact block message | Must | IMPLEMENTED | `LeaveRequestService.cs:1282-1286`, `IsPayrollLockedAsync:1455-1460`; test `Unit/LeavePayrollLockServiceTests.cs:207,283,318` | Gated behind `wasApproved`, so pending requests in a locked period still cancel |
| **US-LV-008** | Carry-forward + expiry rules (5 ACs) | Should | PARTIAL | Math `LeaveCarryForwardCalculator.cs:46-59,65-77`; ledger `LeaveCarryForwardService.cs:168-174,239-245`; jobs `ProcessLeaveYearEndJob.cs:88-159`, `ProcessCarryForwardExpiryJob.cs`, registered `Program.cs:786-795`; preview `LeaveCarryForwardService.cs:357-411` + route `leave-management.routes.ts:39-43`; tests `Unit/LeaveCarryForwardCalculatorTests.cs:35,54`, `LeaveCarryForwardServiceTests.cs:224,252,417,441` | AC-1/2/4/5 solid. **AC-3 Redis invalidation missing** (`:122,344` TODO). Global job entry points never exercised end-to-end (QA TCs blocked) |
| **US-LV-009** | Team leave calendar (4 ACs) | Should | PARTIAL | Manager scope `LeaveRequestService.cs:714-718,744-747`; employee visibility split enforced server-side `:722-730,747,786-788`; week Gantt `team-leave-calendar.component.html:285-303`; route `leave-management.routes.ts:95-100`; tests `Unit/TeamLeaveCalendarServiceTests.cs:198,232,260,404` | AC-1/2/3 strong; AC-2 is the best-defended AC in the module (BUG-035 closed). **AC-4 weak**: breakpoint hardcoded `innerWidth < 768` (`team-leave-calendar.component.ts:378-379`), read once at construction, no resize listener |
| **US-LV-011** | Compulsory leave / LOP (4 ACs) | Should | PARTIAL | AC-1 `LeaveRequestService.cs:212-260,279-280`; AC-3 `LopService.cs:68-155` + route `LeaveLopController.cs:36-50`; AC-4 payroll consumes LOP `PayrollRunProcessor.cs:314-318,497-501`, line item `PayrollSlipCalculator.cs:137-150`; test `Integration/LopAuthorityPayrollPostgresTests.cs:289` | **AC-2 inert**: `NoOpAttendanceProvider` registered (`DependencyInjection.cs:347`) so `ProcessAbsenteeismJob` generates nothing — closed as DECIDED-NOT-BUILT (D2-b). AC-3 writes no `LeaveLedger` row; `LopService.cs:234-240` filters `Approved` only, so HR/system LOP never reaches payroll |
| **US-LV-012** | Leave reports & analytics (5 ACs) | Should | PARTIAL | Backend strong: `LeaveReportService.cs:307,433`, XLSX `:1215`, threshold `SyncExportRowThreshold = 5000` `:48`, cheap pre-count `:189-199,262-282`, Hangfire `LeaveReportExportJob.cs:29-124`; tests `Unit/LeaveReportServiceTests.cs:277-1190` | **FE coded against a fabricated contract** — BE returns `{columns:string[], rows:[{cells:string[]}]}` (`LeaveReportDtos.cs:80-105`), FE reads `res.items` keyed by column name (`leave-reports.models.ts:153-164`, template `leave-report-detail.component.ts:346-350`). AC-4 year-over-year absent entirely (0 hits for `AddYears(-1)`) |

## CONTRADICTIONS

`docs/BA/STATUS.md:64-75` marks **all twelve** stories `[x]` (PRs #23–#38). The code refutes that in both directions:

1. **US-LV-002 `[x]` is refuted by AC-7.** `LocationId` on `LeaveEntitlementRule` does not exist in entity, config, migration, engine, DTO, controller or FE — verified by direct grep returning nothing. Yet `docs/QA/leave-management/TC-LV-ISO-049.md` was authored (2026-07-15, status `draft`) to test it, and `docs/QA/TEST-STATUS.md:127` records it as a Configurable-Calendar-epic deliverable. **A test case exists for a feature that was never built.**
2. **US-LV-012 `[x]` is refuted at the FE/BE seam.** The reports backend is genuinely good; the Angular layer consumes a response shape the API never emits, so every report table and every chart renders its empty state in the running app. Two chart types the FE requests (`utilization-by-type`, `absenteeism-trend`) are not in the backend enum (`LeaveReportDtos.cs:24-31`) → 400. A `trend-analysis` report type and a `/leaves/reports/summary` endpoint exist only in FE code.
3. **US-LV-006 AC-6 / ISSUE-305 is only half-closed.** Backend fiscal-year work is real and well tested; the FE year selector remains calendar-only, and because the FE always sends an explicit `year`, the backend's fiscal default is bypassed. An Apr–Mar tenant in Jan–Mar requests the wrong leave year.
4. **US-LV-003 AC-7 / BUG-284 is only half-closed.** Server-side is exemplary. `leave-request.models.ts:250-252` still hardcodes `day !== 0 && day !== 6`, and it drives the displayed day count, the balance projection, the LOP prompt trigger and the document-threshold pre-block.
5. **Ledger stale in the opposite direction (over-reporting gaps).** `docs/BA/STATUS.md:121` still lists `[ ] FIX BUG-098` — the null guard is present at `leave-type.models.ts:133-135`. `STATUS.md:340` lists US-LV-002 AC-K1 FTE proration as a stale seam — it is built and pinned by four Postgres arms. `TC-LV-031.md:7` says "Employee entity has NO FTE field — feature-not-built"; `Employee.Fte` exists and is wired. `TC-LV-030.md:8` fails on BUG-118, which the code now fixes. `TC-LV-240.md` fails on a hang the ISSUE-230 pre-count closed.
6. **US-LV-011 AC-2 is `[x]` but formally not built.** `docs/QA/TEST-FINDINGS.md:7793-7805` closes ISSUE-357 as DECIDED-NOT-BUILT (D2-b). STATUS.md carries no such qualifier.

**Systemic signal — the most important finding here.** Four separate FE specs pass against mocked backend shapes the API is incapable of producing: reports (`leave-report-detail.component.spec.ts:27,31,188` mock `{items:[…]}`), the approvals negative-balance modal (`leave-approvals.component.spec.ts:366,389,415` mock an error code the controller strips), the leave-application LOP branch, and the dashboard ledger fixtures (`ledgerId`). **Green FE suites are actively concealing broken integrations.** This is the mechanism that made `docs/BA/STATUS.md` wrong, and it will keep doing so until FE fixtures are generated from — or contract-tested against — the real DTOs.

## GAPS RANKED

1. **HIGH — US-LV-012 FE/BE contract mismatch.** Whole reports feature non-functional despite green specs. `src/frontend/…/models/leave-reports.models.ts:153-164` vs `src/backend/…/LeaveReports/DTOs/LeaveReportDtos.cs:80-105`. **Size: M**
2. **HIGH — US-LV-002 AC-7 `LocationId` entitlement tier missing.** Multi-location tenants cannot express location-scoped entitlements; a TC is already written. `src/backend/HRM.Domain/Entities/LeaveEntitlementRule.cs:15-72`. **Size: M**
3. **MED-HIGH — US-LV-003 AC-7 frontend Mon–Fri hardcode.** Gulf/Sun–Thu employees see a wrong day count and a wrongly-triggered LOP prompt. `src/frontend/…/models/leave-request.models.ts:250-252`. **Size: S**
4. **MED-HIGH — US-LV-006 AC-6 frontend year selector calendar-only.** Fiscal tenants query the wrong leave year for three months a year. `leave-dashboard.component.ts:413,417`. **Size: S**
5. **MED-HIGH — US-LV-005 AC-3 warn/confirm path is dead.** `LeaveRequestsController.cs:290,294`, `LeaveRequestDtos.cs:122-126`. **Size: S**
6. **MED — Redis balance-cache invalidation absent module-wide** (LV-005 AC-1, LV-008 AC-3, LV-010 AC-2). **No correctness bug today** — balances compute live from the ledger — so this is an unmet AC by deliberate vault decision, not a defect. **Size: M**
7. **MED — US-LV-004 AC-4 three detail elements are TODO placeholders in shipped UI.** `leave-approvals.component.ts:465-488`. **Size: M**
8. **MED — US-LV-006 AC-2/AC-3 `@for` track-key mismatch** → duplicate-key errors on every ledger and upcoming-leave row. `:350,254`. **Size: S**
9. **MED — US-LV-010 cancel writes no `AuditLog` row**, inconsistent with approve/reject. `LeaveRequestService.cs:1422-1427`. **Size: S**
10. **MED — US-LV-002 job-level dimension hard-rejects every value.** `LeaveEntitlementService.cs:1141-1145`. **Size: M**
11. **MED — US-LV-011 HR/system-assigned LOP never reaches payroll**, and AC-3 writes no ledger row. `LopService.cs:234-240`. **Size: M**
12. **LOW — US-LV-004 AC-5** no realtime refresh though hub and FE client both exist. **Size: M**
13. **LOW — US-LV-009 AC-4** breakpoint 768px, evaluated once, no resize listener. **Size: S**
14. **LOW — US-LV-001 AC-4** inactive-type apply guard has no automated test. **Size: S**

## COVERAGE SUMMARY

**Must Have — 43 ACs:** 27 IMPLEMENTED (63%) · 15 PARTIAL (35%) · 1 MISSING (2%).
**Should Have — 4 stories:** 0 IMPLEMENTED · 4 PARTIAL · 0 MISSING.
**Overall 47 rows:** 27 IMPLEMENTED (57%) · 19 PARTIAL (40%) · 1 MISSING (2%).

Per-story Must-Have: LV-001 5/5 · LV-007 4/4 · LV-003 5/7 · LV-002 4/7 (+1 MISSING) · LV-004 3/5 · LV-005 2/5 · LV-006 2/6 · LV-010 2/4.

**Where the failures concentrate:** of 15 PARTIALs, **10 fail on leg 2 (wiring) or leg 1 at the frontend**, not the backend. The .NET layer is strong — 582 xUnit test methods across 60 leave/holiday test files, real-Postgres arms for the money paths, correct tenant query filters on all six leave entities (`AppDbContext.cs:349-375`), all eight leave Hangfire jobs DI-registered and recurring (`Program.cs:332-348,741-803`), 12 leave migrations present. **The Angular layer is where the module breaks, and its specs do not catch it because they mock contracts the API cannot produce.**

### Calibration check (auditor reporting against the orchestrator's brief)

- **BUG-291 (accrual frequency): genuinely present and substantive.** Period-keyed crediting `LeaveEntitlementService.cs:919-990`; elapsed-period math `AccrualPeriodProgress:1010-1029`; rounding-safe cumulative-difference `PeriodAccrualAmount:1039-1046`; `LeaveLedger.AccrualPeriod` column with migration `20260730043400_Leave_AccrualPeriod.cs`; legacy rows explicitly untouched (`:945`, DF-65). Tests `Integration/LeaveAccrualFrequencyPostgresTests.cs`, `Unit/LeaveAccrualPeriodMathTests.cs`. Not cosmetic.
- **ISSUE-284 (accrual flush once-per-employee): genuinely present.** `LeaveEntitlementService.cs:736-763`; `ProcessSingleAccrualAsync` explicitly does not save (`:990`). The comment at `:739-758` argues correctly why per-employee is the safe boundary and why batching across employees was rejected. PR #455 / commit `5a590e30`.
- **ISSUE-197 — the orchestrator's brief was wrong.** It is **not** "nationality/holiday rule resolution batching". PR #454 / commit `4df527c9` is `fix(ISSUE-197): CTC employer contributions from the real resolver, via a batch API` — a **Payroll / Reports** issue (US-RPT-003, TC-RPT-003-08, `TEST-FINDINGS.md:4550-4560`). The batching is `IStatutoryDeductionResolver.ResolveManyAsync` loading the statutory rule set once per distinct **country**. It touches nothing in the leave module.

## CONFIDENCE

- US-LV-002 AC-7 MISSING — **97%** (verified by grep across entity, engine, configuration, all migrations; only `location_id` hits are on `employees`).
- US-LV-012 FE/BE mismatch — **95%** (read both shapes side by side; 5% is that the app was not executed).
- US-LV-005 AC-3 dead confirm path — **95%**.
- US-LV-003 AC-7 backend IMPLEMENTED — **97%**.
- US-LV-006 AC-6 FE calendar-only — **90%** (did not read `buildYearOptions` in full).
- US-LV-004 AC-4 placeholders — **98%** (read the rendered TODO markup).
- US-LV-006 AC-2/AC-3 track-key mismatch — **80%** (did not personally diff every field; JSON casing could alias).
- Redis invalidation absent — **95%**.
- Single-explorer verdicts without auditor spot-check (LV-008 sub-ACs, LV-009 AC-4, LV-011 AC-3 ledger absence, LV-002 AC-5 audit-test absence) — **~80%** each.
- Coverage percentages — **~85%**. A reviewer applying a stricter literal reading of AC text would land 3–4 rows differently (notably LV-003 AC-7 and LV-005 AC-4).

## OUT-OF-LANE

- **type:** test-integrity / systemic · **severity:** HIGH · **where:** `src/frontend/src/app/features/leave-management/**/*.spec.ts` · **what:** at least four FE spec suites assert against mocked backend payloads the API cannot emit (reports `{items:[…]}`, approvals error-code modal, dashboard `ledgerId`, leave-application `lopOption`). Green suites mask broken integrations; this is the mechanism by which `docs/BA/STATUS.md` became wrong. · **suggested-action:** dispatch `@test-authenticator` over the leave FE specs; consider generating FE fixtures from the real DTOs or adding contract tests as a module-wide remediation.
- **type:** ledger-drift · **severity:** MED · **where:** `docs/BA/STATUS.md:64-75,121,340`; `docs/QA/leave-management/TC-LV-029/030/031/240.md`; `docs/QA/TEST-STATUS.md:127` · **what:** stale in **both** directions — four TCs fail/block against defects the code has since fixed, while three stories are `[x]` despite unbuilt or half-built ACs. · **suggested-action:** route through `/auto-heal`; re-execute the stale TCs rather than re-reading them.
- **type:** brief-factual-error · **severity:** LOW · **where:** orchestrator prompt · **what:** story prefix is `US-LV-*` not `US-LEV-*`; ISSUE-197 misattributed to leave. · **suggested-action:** corrected in the fan-out briefs.
