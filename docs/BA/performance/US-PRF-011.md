---
id: US-PRF-011
module: Performance Management
priority: Should Have
persona: HR Officer
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 4
---

# US-PRF-011: Performance Calibration Workspace  [EPIC STUB — flesh out before build]

> **STUB** — goal + AC skeleton + dependencies only; full AC/FR/BR/NFR to be authored before implementation.
> **Reconciliation story (COMPLETION-PLAN Theme E — the "Calibration DEAD-END TRAP").** Today, enabling
> the calibration toggle permanently blocks recommendation generation (`calibration_incomplete`, and
> **nothing can mark it complete**) → US-PRF-010 lockout. There is no execution surface for the
> calibration phase. This story builds that surface so calibration can actually be run and completed.

## 1. Description
**As an** HR Officer (calibration facilitator),
**I want** a calibration workspace where a committee reviews and adjusts employee ratings across a cohort,
records calibration decisions, and **marks the calibration phase complete**,
**So that** the calibration toggle is no longer a dead-end and US-PRF-010 recommendation generation can proceed.

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
