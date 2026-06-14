---
id: TC-ATT-070
user_story: US-ATT-006
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-070: Daily overtime cap -- 14h on an 8h shift with a 4h daily cap caps overtime at 4h and flags it (boundary)

## 1. Test Objective
Verify BR-4/FR-8 (daily cap): when detected overtime exceeds the tenant's maximum daily overtime, the recorded `overtime_minutes` is capped at the configured maximum and the record is flagged as exceeding the cap. Worked example: 14h net on an 8h standard shift = 6h of raw overtime, but with a 4h daily cap the overtime_record is capped at 240 min and flagged.

## 2. Related Requirements
- User Story: US-ATT-006
- Functional Requirements: FR-8 (cap overtime at the tenant's configured maximum daily/weekly; flag if exceeded)
- Business Rules: BR-4 (max daily overtime tenant-configurable, default 4h; beyond is capped and flagged)

## 3. Preconditions
- Tenant "acme", standard_hours = 480 min, threshold 30 min, max_daily_overtime = 240 min (4h).
- Employee "Asha" with an OPEN record that computes to 14h (840 min) net at clock-out.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| net_work_minutes | 840 min (14h) | |
| standard_hours | 480 min (8h) | raw overtime = 360 min (6h) |
| max_daily_overtime | 240 min (4h) | BR-4 default |
| expected overtime_minutes | 240 | capped at the daily max |
| expected flag | exceeded-daily-cap flag set | FR-8 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clock out the 14h record | 200; an overtime_record is created with `overtime_minutes = 240` (capped at the 4h max), NOT 360. |
| 2 | Inspect the record's cap flag | A flag/marker indicates the raw overtime exceeded the daily cap (so HR can see it was truncated). |
| 3 | Boundary -- a day with exactly 4h overtime (12h net) | overtime_minutes = 240 and NOT flagged as exceeding (at-cap is allowed, over-cap is flagged). |
| 4 | Boundary -- 4h + 1 min raw overtime | Capped to 240 and flagged (just over the cap). |
| 5 | Change tenant max_daily_overtime to 360 (6h) and re-run Step 1 | overtime_minutes = 360, not flagged -- the cap is tenant-configurable (BR-4). |

## 6. Postconditions
- Overtime beyond the daily maximum is stored capped at the configured maximum and flagged; the cap is configuration-driven.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The exact cap-flag field name/shape (e.g. a boolean `cap_exceeded`, a status, or a manager_comment seed) should be confirmed against the backend; this TC asserts the behaviour (capped value + a surfaced flag) rather than a column name.
