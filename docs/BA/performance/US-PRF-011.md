---
id: US-PRF-011
module: Performance Management
priority: Should Have
persona: HR Officer
status: rescoped
created: 2026-07-06
updated: 2026-07-30
sprint: backlog
acceptance_criteria_count: 4
---

# US-PRF-011: Performance Calibration Workspace

> **⚠ RESCOPED 2026-07-30 — the original stub's justification was factually false.**
> This story was authored 2026-07-06 and never re-checked against the code. Its stated reason for existing —
> a calibration "DEAD-END TRAP" — does not exist, and one AC it counted as missing had already shipped.
> Verified and filed as [[ISSUE-348]] and [[ISSUE-352]]; rescope approved by the user 2026-07-30.
>
> | Original claim | Reality (verified against the code) |
> |---|---|
> | "enabling the calibration toggle permanently blocks recommendation generation (`calibration_incomplete`, and **nothing can mark it complete**)" | **FALSE.** `RecommendationService.cs:406-416` gates on `ManagerReviews.Any(r => r.CycleId == … && r.EmployeeId == … && r.SubmittedAt != null)` — satisfied by an ordinary US-PRF-003 manager-review submit. There is no `CalibrationStatus`, no `CalibrationCompletedAt`, and nothing that can get stuck. The passing test `RecommendationServiceTests.Submit_with_calibration_enabled_requires_a_submitted_review` (`:259-269`) asserts success with `calibration: true` — and predates this story. The stub's author read the error **string** ("after calibration is complete"), not the predicate beneath it. |
> | "permanently blocks recommendation **generation**" | **FALSE.** `AutoGenerateAsync` (`:186-266`) contains zero calibration references; the only `IsCalibrationEnabled` read in the service is at `:407`, inside `SubmitAsync`. |
> | US-PRF-010 AC-B1 completed-cycles picker (counted as part of this story's value) | **ALREADY SHIPPED** in commit `bcd7c333` (2026-07-08) — endpoint, query handler, FE service, rendered picker and tests all present. Two days *after* the reconciliation recorded it missing. |
>
> **Net effect: roughly half this story's stated value was already delivered or was never real.** What remains
> is a genuine but smaller capability — a calibration *execution surface* — justified on its own merits
> (running a calibration committee), NOT as an unblocker.

## 1. Description
**As an** HR Officer (calibration facilitator),
**I want** a calibration workspace where a committee reviews and adjusts employee ratings across a cohort and
records the calibrated rating alongside the original,
**So that** moderation decisions are auditable and a calibration phase can be run and closed deliberately.

*(The original "so that the toggle is no longer a dead-end and US-PRF-010 can proceed" is removed — US-PRF-010
already proceeds.)*

## 1b. Rescoped acceptance criteria
| # | Scope | Status | Note |
|---|-------|--------|------|
| AC-1 | Cohort / rating-distribution read surface | **IN SCOPE** | Substantial reuse available: `PerformanceDashboardController` `dashboard/overview` + `dashboard/department/{id}` already compute rating distributions, and `reviews/cycles/{id}/team` already returns a cohort. **Project from these rather than building fresh.** |
| AC-2 | Calibrated-vs-original rating model + audit | **IN SCOPE — the only net-new capability** | No calibrated-rating field exists on `ManagerReview`/`ManagerReviewItem`. New table(s) ⇒ the NEW-TENANT-TABLE rule applies (dormant `tenant_isolation` policy in-migration, or `RlsIsolationPostgresTests` fails). |
| AC-3 | Mark the calibration phase complete | **IN SCOPE, but note the modelling cost** | `CyclePhase` has **no state at all** today (only type/sequence/dates), so this needs the first phase-level state in the model. Prefer a general `CyclePhase.CompletedOn` over a calibration-specific flag on `AppraisalCycle` — the general form also lets `CyclePhaseTransitionJob` and the cycle dashboard stop special-casing Calibration ([[ISSUE-350]]). |
| ~~AC-4~~ | ~~"Unblock US-PRF-010 recommendation generation"~~ | **REMOVED** | The lockout does not exist ([[ISSUE-348]]). |

**Prerequisite (cheap, do first):** [[ISSUE-349]] — nothing validates that `IsCalibrationEnabled == true` implies a
Calibration `CyclePhase` exists, or the converse. A workspace keyed off the flag would have no window to run in;
one keyed off the phase would ignore the flag.

**Also needed:** a real `Performance.Calibrate` permission — added to `PermissionCatalog` **and** the role bundles,
and covered by `PermissionCatalogTests`. Do **not** reuse a docstring-only string; that was the ISSUE-290 trap.

**Explicitly NOT in scope:** the completed-cycles picker (shipped), any "calibration completion unblocker", and
[[ISSUE-351]]'s narrow completed-cycle one-way door — that is a US-PRF-003 reopen-window problem and must not be
used to justify a committee workspace.

## 2. Preconditions
- An appraisal cycle exists (US-PRF-004) with the calibration phase enabled.
- Employee ratings (US-PRF-003) exist for the cohort under calibration.

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A cycle with calibration enabled and rated employees | The facilitator opens the calibration workspace | A cohort view (e.g. rating distribution / grid) is shown for review. |
| AC-2 | The committee adjusts an employee's calibrated rating | They save the adjustment | The calibrated rating is persisted with an audit trail (who/why), distinct from the original manager rating. |
| AC-3 | Calibration review is finished | The facilitator marks the phase **complete** | The cycle's calibration state transitions to complete — **unblocking US-PRF-010 recommendation generation** (removes the `calibration_incomplete` trap). |
| AC-4 | Two tenants run calibration | Any calibration action runs | All calibration data is tenant-isolated. |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI to be written. Key items to cover: calibration-session model, committee membership, distribution guardrails, immutable original vs. calibrated rating, completion state machine, audit, tenant isolation.

## 9. Dependencies
- US-PRF-003 (manager ratings), US-PRF-004 (appraisal cycles), **US-PRF-010** (the story this unblocks).

## 11. Test Hints
- Verify the completion transition removes the recommendation-generation block (regression for the dead-end trap).
