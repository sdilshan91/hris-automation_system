---
id: TC-ATT-018
user_story: US-ATT-002
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-018: Auto-break deduction boundary — break applies just over the threshold, not just under (FR-3)

## 1. Test Objective
Verify FR-3 / BR-2 at the auto-break threshold boundary: when the tenant policy deducts a 60-minute break for shifts strictly greater than 6 hours, a worked span of exactly 6h gets NO deduction, while a span just over 6h (e.g., 6h 1m) triggers the full 60-min deduction. Confirms `total_work_minutes` math is correct at the configured boundary.

## 2. Related Requirements
- User Story: US-ATT-002
- Functional Requirements: FR-2, FR-3
- Business Rules: BR-2
- Non-Functional Requirements: NFR-2 (minute accuracy)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Tenant break policy: deduct 60 min when raw span > 360 min (6h); no deduction at or below 360 min.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- Two independent runs, each starting from a single open record (reset between runs).

## 4. Test Data
| Sub-case | clock_in | clock_out | Raw span | Break applied | Expected total_work_minutes |
|----------|----------|-----------|----------|---------------|-----------------------------|
| A (at threshold) | 09:00 | 15:00 | 360 min (6h) | No (not > 6h) | 360 |
| B (just over) | 09:00 | 15:01 | 361 min | Yes (60 min) | 301 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sub-case A: open record clock_in 09:00; clock out at 15:00 (raw 360 min) | 200 OK; `total_work_minutes = 360`; NO break deducted (span not strictly > 6h). |
| 2 | Reset; Sub-case B: open record clock_in 09:00; clock out at 15:01 (raw 361 min) | 200 OK; `total_work_minutes = 301` (361 - 60); the full break is deducted once the span crosses the threshold. |
| 3 | Verify both DB rows | A: total 360, no deduction recorded; B: total 301, 60-min break applied. |
| 4 | Verify policy source | The break duration and threshold come from tenant/shift config (FR-3, S10 assumption), not from employee input. |
| 5 | Confirm minute accuracy | Both totals are exact to the minute (NFR-2); no rounding drift. |

## 6. Postconditions
- Break deduction is applied iff the worked span exceeds the configured threshold; totals are exact.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
