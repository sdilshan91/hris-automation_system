---
id: TC-ATT-019
user_story: US-ATT-002
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-019: Anomaly detection — clock-out span exceeding 16 hours is flagged ANOMALY for review (FR-7 / BR-6)

## 1. Test Objective
Verify FR-7 / BR-6 at the 16-hour boundary: a clock-in/out session whose `clock_out - clock_in` exceeds 16 hours (960 min) is flagged as a potential anomaly (`status = ANOMALY`) and surfaced for review, while a span at or below 16h is NOT flagged as anomalous. Confirms the maximum-single-session rule.

## 2. Related Requirements
- User Story: US-ATT-002
- Functional Requirements: FR-2, FR-4, FR-7
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Anomaly threshold = 16h (960 min) span between clock_in and clock_out.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- Two independent runs (reset between runs), each from a single open record.

## 4. Test Data
| Sub-case | clock_in | clock_out | Raw span | Expected status |
|----------|----------|-----------|----------|-----------------|
| A (at boundary) | 06:00 day1 | 22:00 day1 | 16h 0m (960 min) | NOT ANOMALY (COMPLETE/OVERTIME per shift rules) |
| B (over boundary) | 06:00 day1 | 22:01 day1 | 16h 1m (961 min) | ANOMALY |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sub-case A: clock out at the 16h-exactly point | 200 OK; the span is NOT flagged anomalous; status follows normal shift evaluation (e.g., OVERTIME). |
| 2 | Reset; Sub-case B: clock out at 16h 1m | 200 OK; `status = ANOMALY`; the record is flagged for review (e.g., surfaced in an HR anomaly/regularization queue). |
| 3 | Verify the DB row for B | `status = ANOMALY`; the span is recorded; the record is marked for review rather than silently accepted as normal hours. |
| 4 | Observe the UI for B | The summary card shows a distinct anomaly indicator/badge and a note that the unusually long session needs review. |
| 5 | Confirm the anomaly does not silently inflate OT | Overtime is not auto-credited for an anomalous span; the review step gates it (consistent with the safety-net intent). |

## 6. Postconditions
- Spans > 16h are flagged ANOMALY and routed for review; spans <= 16h follow normal evaluation.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
