# Pass A9 — performance requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 4 Must-Have stories at AC level (20 ACs) + 7 story-level rows + 1 FR-level lead = **28 rows**
> **Status:** ✅ VALIDATED — both named leads settled from source.
> **Headline:** **13 of 14 PARTIALs fail leg 2.** BUG-243/BUG-244 reconciled the *routes* and *request* bodies; **the response DTO field names were never reconciled**, and the Karma specs mock the invented shape.

## Orchestrator validation — both leads settled, one against my brief

**Lead (a) — `LogOnlyRecommendationIntegrationService`: the auditor pushed back, correctly.** My factual premise held — `DependencyInjection.cs:695` registers the log-only impl unconditionally; no override, no sibling real implementation exists. **But my conditional did not fire.** It read all five ACs of US-PRF-010: workspace listing, rule-based auto-generate, submit+approval routing, aggregate summary, manager team view. **None requires a downstream write.** The requirement that does is **BR-6**, a business rule that §10 explicitly frames as post-approval.

> **Verdict: unmet BR-6, deliberately deferred and documented in-code — a decision, not a defect. Not CONTRADICTED.** *"Per the calibration rule I am declining to inflate it."* `TEST-STATUS.md:189` already records it honestly as ENH-015.

**Lead (b) — goal cascading: Pass B's "COVERED @ 70%" does degrade. Confirmed.**
Tech doc §11.9 requires *"org → department → individual"*. `Goal.ParentGoalId` (`Goal.cs:48`) is a self-FK to another `Goal`, and **`Goal.EmployeeId` (`:18`) is non-nullable** — every goal must belong to an employee, so **no department- or org-level objective is representable.** Seven naming variants searched (`OrganizationalObjective`, `GoalLevel`, `GoalOwnerType`, …); the only `DepartmentId` hits are cycle-participant scoping. **The FE never sets it either** — `goal-setting.component.ts:628` creates a `parentGoalId` control with no input bound to it. **PARTIAL on leg 1 and leg 2. Pass B's rating should be downgraded.**

---

## VERDICT TABLE

| Req ID | Requirement | MoSCoW | Verdict | Evidence · note |
|---|---|---|---|---|
| PRF-001 AC-1 | Goal form: 7 fields | Must | IMPLEMENTED | `GoalDtos.cs:69-79`; `GoalService.cs:95-99`; FE `IGoal` matches `GoalDto` **field-for-field** |
| PRF-001 AC-2 | Persist tenant-scoped, notify | Must | IMPLEMENTED | `GoalService.cs:95-115`; filter `AppDbContext.cs:565`; DI binds **real** `RealPerformanceNotificationService` |
| PRF-001 AC-3 | Weights ≠ 100% → error | Must | IMPLEMENTED | `GoalService.cs:462-468` (`weight_not_100` at finalize), `:81-84` cap; **BUG-056 closed** |
| **PRF-001 AC-4** | Team dashboard incl. **finalized** | Must | **PARTIAL (leg2)** | BE emits `"Finalized"` (`GoalService.cs:687`); **FE type omits it** (`goal.models.ts:47-51`) → `statusBadge('Finalized')` → `undefined` ⇒ **blank unstyled chip for exactly the state the AC names.** Also FE reads `m.jobTitle`, BE emits `EmployeeNo` |
| PRF-001 AC-5 | Window closed → read-only | Must | IMPLEMENTED | `GoalService.cs:668-674`; FE banner + `goalSettingOpen` aligned |
| **PRF-001 FR-4** | Cascading to a **departmental/org** objective | Must (FR) | **PARTIAL (leg1+2)** | See lead (b) |
| **PRF-002 AC-1** | My Review shows goals + self-rating | Must | **PARTIAL (leg2)** | `a.goals` is `undefined` (BE emits `items`) ⇒ **`forEach` throws on load** (`my-review.component.ts:543`). No mapper in the service |
| PRF-002 AC-2 | Submit → notify manager, lock | Must | PARTIAL (leg2) | BE correct; **FE never reads `isLocked`** — zero hits across `features/performance/**` |
| PRF-002 AC-3 | Save as draft | Must | PARTIAL (leg2) | Write path OK; the re-render of the returned DTO hits the same drift |
| **PRF-002 AC-4** | Window closed → read-only + message | Must | **PARTIAL (leg2)** | BE `isSelfAssessmentOpen`; FE gates on `a.windowOpen` ⇒ `undefined` ⇒ falsy ⇒ **screen renders permanently read-only even when open.** Spec mocks `windowOpen: true` |
| PRF-002 AC-5 | Hangfire reminder | Must | IMPLEMENTED | Job + service + real dispatch |
| PRF-003 AC-1 | Side-by-side self vs manager rating | Must | PARTIAL (leg2) | `r.goals` vs BE `items`; **all six goal descriptor fields renamed** |
| **PRF-003 AC-2** | Submit → weighted score, notify | Must | **PARTIAL (leg1+2)** | BE correct. **Enum break:** BE `ReviewFlag` = `None/Recognition/Promotion/Pip`; FE sends `'PromotionConsideration'` ⇒ **`JsonStringEnumConverter` parse failure ⇒ 400 on any promotion-flagged submit** |
| PRF-003 AC-3 | Reject unrated goals, list them | Must | IMPLEMENTED | 422 `incomplete_ratings` listing unrated ids |
| PRF-003 AC-4 | Team review status, colour-coded | Must | PARTIAL (leg2) | **One of four status values unmappable** (`ManagerReviewPending` vs FE `ManagerReviewSubmitted`) + 3 phantom fields |
| **PRF-003 AC-5** | Submitted review read-only unless reopened | Must | **PARTIAL (leg2)** | BE `ManagerReviewStatus` = `Draft\|Submitted`; FE locks on `'ManagerReviewSubmitted'\|'Completed'`. **Zero overlap between the two enums ⇒ a submitted review never locks; edits stay enabled** |
| PRF-004 AC-1–5 | Cycle create/schedule/dashboard/reminders/extend | Must | IMPLEMENTED ×5 | **The one surface where FE↔BE shapes match** — explicitly reconciled under BUG-257/260/261. `CyclePhaseTransitionJob.cs:63-83` genuinely computes non-completers, not a blanket broadcast; scheduler idempotent by job id |
| US-PRF-005 | 360-degree review | Should | PARTIAL (leg2) | **Config screen broken**; form/tracker/submit surfaces **do** match. AC-4 results DTO not diffed in full |
| US-PRF-006 | Meeting notes + sign-off | Should | PARTIAL (leg1+2) | FE gates on `r.status`, BE emits `signoffStatus` ⇒ sign-off/dispute controls never enable. **FE posts only `{body}`** against a 5-field request ⇒ **AC-1's four template sections are unreachable from the UI** |
| US-PRF-007 | Dashboard + analytics | Should | PARTIAL (leg2) | `filterOptions` (AC-2), `teamRanking` (AC-5), `availableExportFormats` (AC-4), `trend` (FR-3) are **all FE-invented — BE emits none** |
| US-PRF-008 | PIP | Should | PARTIAL (leg2) | Backend lifecycle solid; **FE nests checkpoints under objectives, BE keeps them flat** |
| US-PRF-009 | Goal tracking with progress | Should | PARTIAL (leg2) | **BE returns a bare array; FE types it as an object** and reads `d.goals.length`/`d.cycleName`. **Structural, not just naming** |
| US-PRF-010 | Performance-based recommendations | Could | PARTIAL (leg2) | Workspace `page.rows` vs flat `Rows`. **AC-5 reshape done correctly**; completed-cycles picker is an **exact match** |
| **US-PRF-011** | Calibration workspace | Should | 🔴 **CONTRADICTED** | §1b AC-1 cohort ✔, AC-2 `RatingCalibration` table + migration + isolation test ✔. **AC-3 MISSING** — `CyclePhase.cs:15-36` has **no state field**. **`Performance.Calibrate` permission never created** — endpoints reuse three broader permissions. **No FE surface at all.** The story is titled "calibration **workspace**"; **there is no workspace** |

---

## CONTRADICTIONS

**1. US-PRF-011 `[x]` DONE vs three missing pieces.** The *data model* shipped; the *workspace* and the *completion state machine* did not. **The story itself warned:** *"Also needed: a real `Performance.Calibrate` permission… Do **not** reuse a docstring-only string; that was the ISSUE-290 trap."* — and the code walked straight into it.

**2. Reverse drift — `TEST-STATUS.md:188-189` narrates three findings as live that are fixed:** **BUG-063** (Hangfire scheduler "NOT DI-registered" — it is, `Program.cs:411-412`, and `TEST-FINDINGS.md:58` already says RESOLVED, **so the two QA ledgers disagree**); **BUG-242** (self-assessment reopen "unimplemented" — built, `SelfAssessmentController.cs:93` + test); **BUG-057** — **half-fixed, and the ledger doesn't distinguish**: goals have the concurrency token, manager-review still doesn't.

**3. `BA/STATUS.md:361`** flags US-PRF-004 AC-B1 as "⚠ PARTIAL / needs a read" — **resolved by deletion**; the dead FE control was removed and nothing is pending.

---

## GAPS RANKED

1. **🔴 G1 — Systemic FE/BE response-shape drift across 8 of 11 stories. HIGH. Blast radius: the entire Performance UI.** There is **no adapter layer** — the envelope interceptor only unwraps `{success,data}`. Confirmed by grepping `features/performance/**` for the backend's *actual* field names (`isSelfAssessmentOpen`, `weightedSelfScore`, `goalTitle`, `isLocked`): **zero consuming hits.** *Smallest close:* a `map()` adapter per service (**safer than renaming interfaces — it isolates the change from templates**). *L overall, S–M per story. Highest value first: PRF-002 and PRF-003, since 002/009/010 **hard-crash** rather than degrade.*
2. **G2 — The Karma specs are why this survived. HIGH (test-integrity).** Every spec mocks the invented shape. *Add one contract test per surface against a captured real payload. M.*
3. **G3 — `ReviewFlag` enum breaks a write path. MED-HIGH.** `'PromotionConsideration'` cannot deserialize ⇒ 400. **Two-token FE rename. S.**
4. **G4 — US-PRF-011 AC-3 phase-completion state. MED.** *Add `CyclePhase.CompletedOn` as the story itself recommends, over a calibration-specific flag.*
5. **G5 — `Performance.Calibrate` permission absent. MED. S.**
6. **G6 — No calibration FE workspace. MED. M–L.**
7. **G7 — FR-4 cascading has no org/department tier. MED.** *Smallest **honest** fix: either build the owner tier (L) or **record a scope decision** that cascading is individual-only and amend FR-4/§11.9. Recommend the decision first; the UI is missing either way.*
8. **G8 — US-PRF-006 template sections unreachable. MED. M.**
9. **G9 — Manager-review optimistic concurrency still unwired. LOW-MED. S** — the goals surface already has the pattern to copy.
10. **G10 — BR-6 downstream integration is log-only. LOW as a defect (recorded decision), MED as product debt. Do not schedule as a bug.**

---

## COVERAGE SUMMARY

```
Rows: 28 | IMPLEMENTED: 13 | PARTIAL: 14 | MISSING: 0 | CONTRADICTED: 1
```

**13 of 14 PARTIALs fail leg 2, not leg 1.** Only three rows carry a leg-1 component.

The backend is consistently strong — handlers dispatched, controllers routed, services DI-registered with **real** implementations, 11 migrations, and **all 15 tenant-scoped performance entities carry a global query filter.** Leg 3 broadly satisfied: 28 xUnit files + ~85 IEEE-829 TCs.

**The one story whose FE contract is clean (US-PRF-004) is the one that got an explicit reconciliation pass. That is the template for closing G1.**

---

## CONFIDENCE

**Thorough (95%+):** PRF-001 (5 ACs + FR-4), PRF-002 (5), PRF-003 (5), PRF-004 (5), and both leads — *"I would defend either verdict."*

**Story-level, spot-checked (80–85%):** PRF-005 (**AC-4 results DTO not diffed — could be clean like the form or drifted like the config; a 20-line diff settles it**), 006, 007, 008 (80%), 009 (90%), 010. **The *direction* of each verdict is high-confidence; the *completeness* of each field list is not.**

**PRF-011 (90%)** — three direct negative observations. Residual: the `RatingCalibration` migration body wasn't read to confirm the dormant policy, though the isolation test existing is decent proxy evidence.

**Not reached:** no test executed (leg 3 recorded, not run) — **so I cannot say whether the specs I flagged currently pass**. No runtime confirmation: every "crash" claim is a static read of `x.y` where `y` is provably absent. **Confidence that PRF-002/009/010 hard-fail at runtime: 90%; that the softer ones degrade rather than crash: 85%.** All NFRs unverifiable statically. **BUG-003/068/069 cross-tenant leakage explicitly out of scope and not re-verified.**

---

## OUT-OF-LANE

- **type:** test-integrity · **severity:** HIGH · **where:** `my-review.component.spec.ts:26,31`, `team-goals.component.spec.ts:28,36` · **what:** specs mock shapes the backend cannot emit, so the module's FE suite is **green against a fictional contract.** · **suggested-action:** `@test-authenticator`; one payload-fixture contract test per FE service, captured from the real API.
- **type:** bug · **severity:** HIGH · **where:** `manager-review.models.ts:83` + `manager-review.component.ts:509,723` · **what:** `'PromotionConsideration'` cannot deserialize into C# `ReviewFlag.Promotion` → **400 on any promotion-flagged submit.** · **suggested-action:** file as a BUG; two-token rename plus a spec asserting the exact wire value.
- **type:** doc-drift · **severity:** MED · **where:** `TEST-STATUS.md:188-189` · **what:** a frozen 2026-06-26 snapshot still describing BUG-063, BUG-242 and half of BUG-057 as live — **and `TEST-FINDINGS.md:56-58` already lists two of them RESOLVED, so the two QA ledgers now disagree.** · **suggested-action:** `/verify-fix` for BUG-063 and BUG-242; **split BUG-057 into goals (resolved) and manager-review (open).**
- **type:** doc-drift · **severity:** MED · **where:** tech doc `:850` vs `Goal.cs:18,48` · **what:** §11.9 promises org→department→individual cascading; the model supports only individual→individual. · **suggested-action:** **decision gate** — build the owner tier or amend the doc; either way downgrade Pass B's rating and record an ADR.
- **type:** risk · **severity:** MED · **where:** `DependencyInjection.cs:695` · **what:** BR-6's seam is log-only with no outbox/replay, so **approved promotions/bonuses/training nominations are silently dropped in production with only a Serilog line as evidence** — invisible to anyone reading only `STATUS.md`'s `[x]`. · **suggested-action:** track as product debt; **if it ships log-only for a while, add a startup warning so the stub cannot reach prod unnoticed.**
