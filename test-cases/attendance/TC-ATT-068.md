---
id: TC-ATT-068
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-068: Overtime threshold boundary -- 8h20m on an 8h shift with a 30-min threshold creates NO overtime record (negative/boundary)

## 1. Test Objective
Verify BR-2/FR-1: work that exceeds the shift standard by LESS than the tenant overtime threshold is not counted as overtime. Worked example: 8h20m (500 min) net on an 8h (480 min) standard shift with a 30-minute threshold is only 20 minutes over standard (< 30), so the clock-out creates no overtime_record. Includes the just-past-threshold positive control to pin the exact boundary.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-1 (the not-triggered side)
- Functional Requirements: FR-1 (detection condition: total > standard + threshold)
- Business Rules: BR-2 (threshold tenant-configurable, default 30 min; below-threshold excess is not overtime)

## 3. Preconditions
- Tenant "acme", overtime threshold = 30 min, standard_hours = 480 min (8h), weekday.
- Employee "Asha" authenticated with `Attendance.Clock.Self`, an OPEN record that will compute to the net minutes per the variant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| standard_hours | 480 min | 8h |
| overtime_threshold | 30 min | |
| Variant A net | 500 min (8h20m) | 20 min over standard, < 30-min threshold -> NO overtime |
| Variant B net | 509 min (8h29m) | 29 min over, still < 30 -> NO overtime (last below-threshold minute) |
| Variant C net | 511 min (8h31m) | 31 min over, > 30-min threshold -> overtime recognised (positive control) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clock out Variant A (500 min net) | 200; `GET /overtime/my` shows NO overtime_record for today; attendance_log overtime_minutes = 0 / status not OVERTIME. |
| 2 | Repeat with Variant B (509 min net, the highest below-threshold value) | Still NO overtime_record -- the threshold gate is not crossed at 29 min over. |
| 3 | Repeat with Variant C (511 min net, just past threshold) | An overtime_record IS created (status PENDING, AUTO_DETECTED), confirming the boundary is exactly at standard + threshold. |
| 4 | Confirm the threshold is read from tenant overtime rules, not hard-coded | Changing the tenant threshold to 15 min and re-running Variant A (20 over) now DOES create an overtime_record -- threshold is tenant-configurable (BR-2). |

## 6. Postconditions
- No overtime_record exists for the below-threshold variants; the configuration-driven boundary is confirmed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- This boundary holds regardless of which overtime-minutes definition the backend uses (see TC-ATT-067 Note): at/below the threshold, no overtime is recognised under either definition, so the not-triggered assertion is unambiguous.
