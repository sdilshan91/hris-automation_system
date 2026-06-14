---
id: TC-ATT-075
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-075: Manager adjusts overtime on approval -- approve but reduce 3h to 2h sets approved_minutes = 120 (boundary)

## 1. Test Objective
Verify FR-6 (adjust): a manager can approve an overtime record while adjusting the approved duration down from the detected amount. Worked example: a detected 3h (180 min) overtime, approved with an adjustment to 2h, results in `approved_minutes = 120` while the original `overtime_minutes` (180) is preserved for audit, and only the approved_minutes feeds payroll.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-4 (the adjust variant)
- Functional Requirements: FR-6 (approve, reject, or adjust overtime hours), FR-7 (payroll-ready uses the approved amount)

## 3. Preconditions
- Tenant "acme". Manager "Ben"; direct report "Asha" has a PENDING overtime_record with overtime_minutes = 180 (3h).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| detected overtime_minutes | 180 (3h) | preserved |
| approve body | { approvedMinutes: 120, comment: "Approved 2h, last hour was a break" } | adjustment |
| expected approved_minutes | 120 (2h) | FR-6 |
| expected payroll basis | 120 | only approved amount feeds payroll |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben, approve with `approvedMinutes = 120` | 200; status APPROVED, `approved_minutes = 120`, `overtime_minutes` UNCHANGED at 180 (original detection preserved), comment stored. |
| 2 | Confirm payroll basis | The payroll-ready feed uses approved_minutes (120), not the detected 180. |
| 3 | Boundary -- approvedMinutes = 0 | Treated per the documented rule: either a valid "approve none" (approved_minutes 0, effectively no paid OT) or routed to rejection -- assert the documented behaviour, not an accidental one. |
| 4 | Negative -- approvedMinutes greater than detected (e.g. 240 > 180) | Rejected/clamped per the documented rule (a manager should not be able to inflate beyond detected); assert it is not silently accepted as 240. |
| 5 | Audit | The audit entry records the adjustment (from 180 detected to 120 approved), actor, timestamp. |

## 6. Postconditions
- The approved overtime reflects the manager's adjustment; the original detection is preserved; payroll uses the approved amount.

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
- Steps 3-4 (approve-zero semantics and the upper bound vs detected) are not explicitly specified by the story; assert against the backend's documented behaviour and flag to the BA if undocumented. The core FR-6 down-adjustment (180->120) is unambiguous.
