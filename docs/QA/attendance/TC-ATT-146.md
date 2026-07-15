---
id: TC-ATT-146
user_story: US-ATT-011
module: Attendance
priority: high
type: functional
status: draft
created: 2026-07-15
---

# TC-ATT-146: Location.DefaultShiftId rejects a soft-deleted / inactive shift (AC-1 negative)

## 1. Test Objective
Verify US-ATT-011 AC-1 / FR-1 validation: setting `Location.DefaultShiftId` to a **soft-deleted or inactive** shift is rejected server-side (a non-active shift may not become a Location's working calendar), per spec §7.1.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-1
- Functional Requirement: FR-1
- Spec §7.1: `Location.DefaultShiftId` must be a same-tenant, **active** Shift

## 3. Preconditions
- A tenant with an **inactive** Shift (`IsActive = false`) and a **soft-deleted** Shift (`IsDeleted = true`), plus a Location.
- Actor holds `Attendance.*.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Inactive shift | `IsActive = false` | same tenant |
| Soft-deleted shift | `IsDeleted = true` | same tenant, filtered out |
| Location | Colombo Branch | target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set Colombo Branch `DefaultShiftId` to the **inactive** shift; save. | 400 validation error (shift must be active); Location `default_shift_id` unchanged (null). |
| 2 | Set Colombo Branch `DefaultShiftId` to the **soft-deleted** shift id; save. | 400/rejected — soft-deleted shift is not resolvable; not persisted. |
| 3 | Re-read the Location. | `DefaultShiftId` remains null; chain still falls through to the tenant default shift. |

## 6. Postconditions
- No inactive/deleted shift can be wired as a Location calendar; the Location tier stays empty (falls through).

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
