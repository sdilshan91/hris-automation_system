---
id: TC-LV-262
user_story: US-LV-003
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-07-15
defect:
  - BUG-284
---

# TC-LV-262: Gulf (Sun–Thu) leave day-count uses the resolved work-week — Sun–Thu leave deducts 5 workdays; half-day on Sun accepted, on Fri rejected (BUG-284 regression)

## 1. Test Objective
Verify the BUG-284 fix on US-LV-003: `LeaveRequestService` counts leave days, gates half-days, and previews the balance against the (location-aware) `ShiftScheduleResolver` work-week instead of the hardcoded `WorkingDaysCalculator.DefaultWorkWeek` (Mon–Fri). For a Gulf employee on a Sun–Thu work-week (`{7,1,2,3,4}`), a leave spanning **Sunday through Thursday** deducts **5 working days** — **Friday** (their weekend) is not counted and **Sunday** (a workday) is not skipped; a half-day request **on Sunday** is accepted while a half-day **on Friday** is rejected (not a working day).

## 2. Related Requirements
- User Story: US-LV-003 (also US-LV-006 balance preview)
- Acceptance Criteria: leave day-count / half-day gate against the work-week
- Defect: BUG-284
- Cross-reference: US-ATT-011 FR-2/FR-7 (resolver is the single source of the work-week)

## 3. Preconditions
- Gulf employee resolving a Sun–Thu work-week (Location/tenant shift `{7,1,2,3,4}`).
- A leave type with sufficient balance; half-day allowed.
- Postgres-backed context; assert the deducted ledger amount and the half-day accept/reject.
- Pre-fix: production callers omit the optional `workWeek` arg → Mon–Fri assumed → this test FAILS (Fri counted, Sun skipped).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Work-week | `{7,1,2,3,4}` (Sun–Thu) | Fri/Sat = weekend |
| Full-week leave | Sun → Thu | expected 5 workdays |
| Half-day (Sunday) | workday | accept |
| Half-day (Friday) | weekend | reject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request leave Sunday through Thursday. | Deducts **5** working days; Friday within any spanning range is not counted, Sunday IS counted. |
| 2 | Inspect the balance preview for the same range. | Preview shows 5 days (consistent with the deduction). |
| 3 | Request a **half-day on Sunday**. | Accepted (Sunday is a working day). |
| 4 | Request a **half-day on Friday**. | Rejected (Friday is not a working day for this employee). |

## 6. Postconditions
- Leave balances/half-day gating are correct for the Gulf population; no Mon–Fri hardcode remains.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
