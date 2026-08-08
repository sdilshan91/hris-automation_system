# Pass A6 — attendance requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 8 Must-Have stories at AC level (43 ACs) + 3 Should-Have at story level = **46 rows**
> **Status:** ✅ VALIDATED — 2 of 2 orchestrator spot-checks confirmed.
> **Headline:** 🔴 **`FteScaledOvertimeBase` is dead wiring — enabling the flag changes no payroll output**, and a TC marked `automated` covers the path that cannot exist.

> ⚠ *Recovered after turn-budget exhaustion; `maxTurns` raised to 140 (`fd0b99ce`). The auditor states its own coverage per story in `## CONFIDENCE` — read that section before trusting any single row.*

## Orchestrator validation

| Claim | Result |
|---|---|
| **`FteScaledOvertimeBase` never reaches payroll** | ✅ **Confirmed.** `PayrollRunProcessor.cs:977-978` calls `PayrollOvertimeCalculator.Compute(basic, workingDays, buckets, minutes)` — **four arguments.** The signature (`PayrollOvertimeCalculator.cs:59-67`) declares `decimal fte = 1.0m, bool fteScaledBase = false` as trailing optionals, so both fall to defaults. **`grep -c '\.Fte\b'` over `PayrollRunProcessor.cs` returns `0`.** |
| Location tier missing from `ShiftService.ResolveForEmployeeAsync` | Accepted at the auditor's stated 93% — not independently re-read |

### The auditor corrected two of my brief's leads — both corrections accepted

**1. My `NoOpAttendanceProvider` lead was wrong on impact.** The DI comment *is* stale ("No attendance module yet (US-ATT-*)") — that much held. But the conclusion did not follow. `IAttendanceProvider` has exactly one production consumer: `LopService`, feeding the **leave** module's absenteeism job. **The attendance→payroll LOP rail runs through a completely different path** (`IAttendancePayrollService.GetPayrollDataAsync` → `PayrollRunProcessor.cs:1106`), and since D2/BUG-293 the authoritative figure comes from `_lopService.GetPayrollLopDaysAsync` reading `LeaveRequest.IsLop` rows directly. **Zero attendance ACs are damaged by the NoOp registration.** Cost: a misleading DI comment, nothing more.

**2. My "stored but not enforced" suspicion about `AttendanceSettings` was too broad.** The auditor verified enforcement-path reads for ~15 knobs — `RequireGeolocation`, `IpAllowlistEnabled`/`IpAllowlist`, `RequirePhoto`, all five geofence fields, `RegularizationLookbackDays`, three OT multipliers, OT thresholds, `RequireOvertimePreApproval`, `StandardWorkMinutes` — **all read, all location-resolved.** Exactly **two** exceptions: `GracePeriodMinutes` (read at the wrong precedence) and `FteScaledOvertimeBase` (read nowhere). One knob in fifteen, not a pattern.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| ATT-001 AC-1 | Clock-in creates log, UTC, tenant-stamped | Must | IMPLEMENTED | `AttendanceService.cs:186-198`; `AppDbContext.cs:385-386`; envelope unwrapped globally `api-envelope.interceptor.ts:36-51` | |
| ATT-001 AC-2 | Duplicate clock-in blocked, exact message | Must | IMPLEMENTED | `:110-116` + `:236-249` (23505 race → same 409) | **BUG-047 race now caught** |
| ATT-001 AC-3/4 | Geolocation required/optional | Must | IMPLEMENTED ×2 | `:139-142`, `:137,190-191` | |
| ATT-001 AC-5 | IP allowlist rejects non-allowed IP | Must | IMPLEMENTED | `:129-135` (`IpAllowlistMatcher`, CIDR-capable) | **ISSUE-066 FIXED** — reverse drift |
| ATT-001 AC-6 | Remote geofence-exempt; OnSite/Hybrid blocked | Must | PARTIAL (leg2) | `:150,153` | Backend correct. **Auditor flags: the FE claim was inherited from my brief, not re-verified this pass — 60% confidence in the PARTIAL** |
| ATT-001 AC-7 | Location geofence + grace override wins over tenant | Must | **PARTIAL (leg1)** | Geofence correct `:735-738`; grace `:768` = `shift.Grace > 0 ? shift : settings` | **Grace precedence is inverted vs BR-9** — a shift with non-zero grace shadows the Location override |
| ATT-002 AC-1–5 | Clock-out, hours, OT split, short day, geo | Must | IMPLEMENTED ×5 | `:349,370-375`, `:318-325`, `:372-374`+`:404-408`, `:370-375`, `:344-347` | OT written in the same transaction |
| ATT-003 AC-1–5 | Regularization: PENDING + workflow, linkage, lookback, duplicate, payroll lock | Must | IMPLEMENTED ×5 | `:552-604`, `:544-557`, `:512-518`, `:531-540`, `:520-529` | Local-day window, tz-correct |
| ATT-004 AC-1–3, AC-5 | Approve/reject/queue/out-of-team | Must | IMPLEMENTED ×4 | `RegularizationApprovalService.cs:141-191,71-115`; `OvertimeService.cs:772-779` | AC-5 verified by pattern — **78%** |
| ATT-004 AC-4 | Multi-level workflow advances | Must | IMPLEMENTED | `:405-478` — `StepAdvanced` → stays Pending; `InstanceApproved` → Approved | **Reverse drift**: TEST-STATUS says "no workflow engine" — it is wired |
| ATT-005 AC-1–5 | Shift CRUD, assign, no overlap, delete guard, rotation | Must | IMPLEMENTED ×5 | `ShiftService.cs:58-99,390-460,419-436,211-235,527,594-601` | `ShiftDto` ↔ `attendance.models.ts:488-509` **field-by-field match** |
| **ATT-005 AC-6** | Location default shift is the tier above tenant default | Must | **PARTIAL (leg1)** | 4-tier exists for working-**day sets** (`ShiftScheduleResolver.cs:66-120`); but `ShiftService.ResolveForEmployeeAsync:494-520` is **3-tier — no Location tier** | Late/early signals, OT standard minutes and the shift endpoint all use the 3-tier path → **a Location-configured employee is graded against the tenant shift.** Violates US-ATT-011 FR-2/BR-2 |
| **US-ATT-006** | Overtime tracking & approval | Should | PARTIAL (leg1) | AC-1..AC-8 spot-checked and sound; BUG-285 + BUG-286 both fixed (`OvertimeService.cs:96,929-936`; `:95,897`) | **AC-9 fails** — see ATT-011 AC-5. Also: OT date from **UTC** (`:91`), not tenant-local, unlike the rest of the module |
| ATT-007 AC-1,2,4,5 | Summary, drill-down, export, dept filter | Must | IMPLEMENTED ×4 | `AttendanceSummaryService.cs:72-119,148,311,908-1009,773-774` | |
| ATT-007 AC-3 | On-demand generation **"via Hangfire"** + progress | Must | PARTIAL (leg1, mechanism) | `:243-309` computes **synchronously in-request**, returns `Status="COMPLETED"` inline; contrast `:350` which does enqueue | **The auditor's least confident call (55%)** — outcome met, named mechanism not. Real NFR-2 exposure: a 5,000-employee loop inside an HTTP request |
| **US-ATT-008** | Late/early tracking | Should | PARTIAL (leg1) | AC-1..5,7 sound; **ISSUE-065 UTC-vs-tz FIXED** (`TenantClock.LocalTimeOfDay`) | **AC-6 fails** (grace precedence). **BR-8 half-day carve-out IS implemented** (`:779-791`), contradicting ISSUE-086 |
| ATT-009 AC-1–4 | Payroll pulls inputs, LOP, OT×multiplier, period lock | Must | IMPLEMENTED ×4 | `PayrollRunProcessor.cs:1101-1108,494-512,518-520`; `AttendancePayrollService.cs:241-293` | **ISSUE-088 garbage-date lock fixed** at `:247-252` |
| ATT-009 AC-5 | Unlock → correct → **recalculate payroll** → re-lock | Must | PARTIAL (leg1) | `:295-337`; `:328-330` states the FR-6 payroll-refresh trigger is **deferred** | Corrections don't propagate unless HR re-runs payroll manually |
| **US-ATT-010** | Dashboard & reports | Should | PARTIAL (leg1) | AC-1,3,4,5 sound; **BUG-050 Manager 403 FIXED** (`AttendanceController.cs:1244,1269`) | **AC-2 fails as written**: live board is **30-second polling**, not SignalR; NFR-2 (3s) unmet. Documented deferral. FE/BE contract matches field-for-field |
| **ATT-011 AC-1** | `Location.DefaultShiftId` **exposed on Location create/edit** | Must | 🔴 **PARTIAL (leg2)** | Backend complete (`Location.cs:86`; `LocationService.cs:76,139,247-260`; migration). **FE: zero occurrences of `defaultShiftId` in `src/frontend`** | FR-1 explicitly requires UI exposure. **The AC's Given is unreachable** |
| ATT-011 AC-2 | Four-tier working-day resolution | Must | IMPLEMENTED | `ShiftScheduleResolver.cs:50-120`, batched ≤5 queries | Empty-set shadowing guard at `:70-78` is correct |
| ATT-011 AC-3 | Location-scoped policy override, ≤1 per (tenant, location) | Must | PARTIAL (leg2) | Resolver + CRUD + unique index + query filter all present | **No FE surface** — backend-only feature |
| ATT-011 AC-4 | `ExcludeHolidaysFromWorkingDays`, effective-dated, single-basis | Must | IMPLEMENTED | `PayrollCalendarPolicyService.cs:49-63,109`; `PayrollRunProcessor.cs:488-509`; test `:261,313-323` | **Denominator AND numerator share `empHolidays`** — BR-10 honoured |
| **ATT-011 AC-5** | `FteScaledOvertimeBase` on → OT base scales by FTE | Must | 🔴 **CONTRADICTED** | Stored/CRUD'd and the pure math honours it (`PayrollOvertimeCalculator.cs:59-77`). **Sole production caller `PayrollRunProcessor.cs:977-978` passes 4 of 7 args; `.Fte` appears 0 times in that file** | **The "flag on" branch is unreachable in production.** Also fails US-ATT-006 AC-9 |

---

## CONTRADICTIONS

### 🔴 1. `FteScaledOvertimeBase` claimed shipped; it is dead wiring

`STATUS.md:88` marks US-ATT-011 shipped across PRs #310/#313/#314/#315; `STATUS.md:61` lists `FteScaledOvertimeBase (default off)` under US-CHR-013. **Turning the flag on changes no payroll output** — orchestrator-verified.

**Test-integrity aggravator.** `docs/QA/attendance/TC-ATT-152.md` is `status: automated` and its Step 2 reads: *"Set `FteScaledOvertimeBase = true`; recompute the part-timer's OT base … assert the **stored OT earnings** differ from arm 1."* Its bound automation is `OvertimeFteBaseTests` — **pure calls to `PayrollOvertimeCalculator.Compute` with the flag passed by hand.** No stored earnings are ever produced. The test file's own header concedes it (`OvertimeFteBaseTests.cs:11-13`): *"a green unit test here proves the MATH, not the plumbing."* **The gap was known at authoring time and the TC was marked `automated` anyway.**

### 2. ATT-011 AC-1 claimed shipped; no FE surface exists
`grep -rn "defaultShiftId" --include=*.ts src/frontend/src` → **no matches.** FR-1 requires the field be "exposed on Location create/edit". Same for AC-3: no FE consumer of `/attendance/settings/overrides*`.

### 3. Reverse drift — TEST-STATUS.md is stale on **eight** items
All claimed OPEN in the 2026-06-27 note; all fixed in code: **BUG-047** (clock-in race), **ISSUE-065** (UTC-vs-tz late detection), **ISSUE-066** (IP allowlist CIDR), **ISSUE-067/069/071/073** (no audit rows — now `AddAttendanceAudit` with before/after in-transaction), **BUG-050** (Manager 403), **ISSUE-086** (BR-8 half-day), **ENH-005** (multi-level approval), **ISSUE-068** (single-center geofence — now `GeoFenceLocations` any-match).

---

## GAPS RANKED

1. **🔴 `FteScaledOvertimeBase` dead wiring — HIGH (money path), S.** *Fix:* thread the already-loaded per-location policy map + `emp.Fte` into `ComputeOvertime` (`:518, 968-979`) and pass both trailing arguments. **The policy map is already loaded in the same method for other purposes.** Then bind an integration arm to TC-ATT-152 that asserts a **stored** `PayrollSlip.OvertimeAmount`.
2. **Location tier missing from `ShiftService.ResolveForEmployeeAsync` — HIGH, M.** A Gulf-branch employee with no personal assignment is graded against the tenant Mon–Fri shift's start/end/grace. Affects the shift endpoint, late/early, OT standard minutes, and clock-out work-minute knobs.
3. **No FE surface for `Location.DefaultShiftId` or the policy override — HIGH, S/M.** The entire location-calendar epic is admin-unreachable.
4. **Grace-period precedence inverted vs BR-9 — MEDIUM, S.** Needs a one-line signal from `AttendancePolicyResolver` on whether the resolved row was an override.
5. **ATT-009 AC-5 — no payroll recalculation trigger on unlock — MEDIUM, M.**
6. **ATT-010 AC-2 — 30s polling instead of SignalR; NFR-2 (3s) unmet — MEDIUM, M.** Deliberate documented deferral; SignalR infra already exists.
7. **ATT-007 AC-3 — synchronous summary generation — LOW-MED, M.** The export path at `:350` already shows the enqueue pattern.
8. **OT date derived from UTC, not tenant-local — LOW, S.** For a non-UTC tenant punching near midnight this selects the wrong weekday and therefore **the wrong weekend/holiday multiplier**.

---

## COVERAGE SUMMARY

```
Rows: 46 | IMPLEMENTED: 33 | PARTIAL: 11 | MISSING: 0 | CONTRADICTED: 2 (also counted in PARTIAL)
```

**Where the failures concentrate: leg 2, frontend** — 3 of 11 PARTIALs are backend features with **no UI at all**, and 1 is a backend feature whose enabling flag is never threaded through payroll.

**Notably, the attendance FE is the best-aligned in the codebase so far.** The global `apiEnvelopeInterceptor` (`app.config.ts:75-81`) eliminates the envelope class of bug, and every DTO↔interface pair compared matched. The two drifts found (`IAttendanceLog.attendanceLogId`, `IClockOutResult.attendanceLogId`) are **type-declaration-only — no component reads them** — though `attendance.service.spec.ts:91` still asserts a field the API never emits.

---

## CONFIDENCE — read this before trusting any row

**Thorough (≥90%):** ATT-001 (7 ACs), 002 (5), 003 (5), 005 (6), 009 (5), 011 (5). The two CONTRADICTED findings: **AC-5 at 97%** (orchestrator-verified), **AC-1 FE absence at 95%**. Gap #2 at **93%**.

**Adequate (75–85%):** ATT-004 — AC-4 read in full (92%), **AC-5 verified by pattern, not line-by-line (78%)**.

**Least confident call (55%): ATT-007 AC-3.** The AC says "via Hangfire"; the code delivers the outcome synchronously. **The auditor chose PARTIAL but explicitly says it would not defend it hard.** *Settled by:* a product decision on whether "via Hangfire" is contractual or illustrative.

**Explicitly NOT established — needs a second pass:**
- **ATT-001 AC-6 (`WorkArrangement` FE): 60%.** The FE claim was **inherited from my brief, not verified**. One grep settles it.
- **Leg 3 is under-sampled.** 182 TC files and ~30 xUnit classes confirmed by count, but **not per-AC.** Several IMPLEMENTED verdicts rest on legs 1–2 with leg 3 assumed from volume.
- **FE→BE contract comparison was sampled, not exhaustive** — 5 pairs diffed, ~30 unchecked in a 1,600-line model file.
- **Not verified:** US-ATT-006 AC-5/AC-8, **US-ATT-008 AC-4 (3-lates→0.5-day deduction reaching the summary as LOP days — worth a targeted pass since it feeds payroll)**, US-ATT-010 AC-5.

**Overall: 85%.**

---

## OUT-OF-LANE

- **type:** bug · **severity:** HIGH · **where:** `location.models.ts:14` · **what:** `ILocation.locationId` does not exist on the API response — `LocationDto.cs:8` returns `Id`. `location-list.component.ts:605` and `location-form.component.ts:640` both call with `undefined`. **Independently corroborates the core-hr pass**, and it would block ATT-011 AC-1 even after `defaultShiftId` is added. · **suggested-action:** rename to `id` (or map in the service), then re-run the location specs — presumably green against a mock carrying `locationId`.
- **type:** doc-drift · **severity:** MED · **where:** `DependencyInjection.cs:345-347` · **what:** the stale NoOp comment is what a reader hits first; `NoOpAttendanceProvider.cs:12-21` already corrects it. **Confirmed to damage zero attendance ACs.** · **suggested-action:** replace with a pointer to the class remarks and the ISSUE-357/BUG-293 resolution **so nobody re-derives the wrong conclusion** (as this brief did).
- **type:** test-integrity · **severity:** HIGH · **where:** `TC-ATT-152.md:38` vs `OvertimeFteBaseTests.cs:11-13` · **what:** a TC marked `automated` asserts stored OT earnings for a flag-on arm the pipeline cannot produce; the bound test is a pure-function call and **its own header concedes it proves the math, not the plumbing.** · **suggested-action:** demote to `draft` until an integration arm asserts a persisted slip under both flag states. **Audit sibling CAL-epic TCs for the same "unit test claimed as integration coverage" pattern.**
- **type:** test-integrity · **severity:** LOW · **where:** `attendance.service.spec.ts:91` · **what:** asserts `log.attendanceLogId`, a field the DTO never emits. Unread by any component, so no AC breaks — **but it is exactly the pattern that hid larger drift elsewhere.**
- **type:** risk · **severity:** MED · **where:** `AttendanceSummaryService.cs:243-309` · **what:** iterates every non-terminated employee synchronously inside an HTTP request. **Recorded so the perf exposure is not lost if AC-3 is later judged mere naming drift.**
