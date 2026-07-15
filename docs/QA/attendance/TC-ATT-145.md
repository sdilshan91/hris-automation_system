---
id: TC-ATT-145
user_story: US-ATT-011
module: Attendance
priority: high
type: functional
status: draft
created: 2026-07-15
---

# TC-ATT-145: Location.DefaultShiftId accepts an active, same-tenant shift and persists it (AC-1 happy path)

## 1. Test Objective
Verify US-ATT-011 AC-1 / FR-1: a Tenant Admin can set a Location's `DefaultShiftId` to an **active, same-tenant** Shift, the value persists, and the Location tier of the working-calendar chain now points at that shift.

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-1
- Functional Requirement: FR-1
- Business Rule: BR-3 (Location tier of the precedence chain)

## 3. Preconditions
- A tenant with at least one **active** Shift (e.g. "Gulf Sun–Thu", `WorkingDays = {7,1,2,3,4}`) and one Location ("Dubai Branch").
- Actor holds `Attendance.*.All` (Tenant Admin).
- Postgres-backed `AppDbContext` (this asserts real FK/query-filter behaviour).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Location | Dubai Branch | same-tenant |
| Shift | Gulf Sun–Thu (active) | `IsActive = true`, same tenant |
| DefaultShiftId | {Gulf shift id} | FK target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Update Dubai Branch, setting `DefaultShiftId` to the active Gulf shift id; save. | 200/success; Location row persists `default_shift_id = {Gulf shift id}`. |
| 2 | Re-read the Location. | `DefaultShiftId` echoes the Gulf shift id. |
| 3 | Resolve the working-day set for a Dubai employee with no personal shift. | Resolver returns the Gulf shift's working days (Location tier supplied the result — NFR-3 derivable). |

## 6. Postconditions
- Dubai Branch's Location tier of the shift chain points at the Gulf shift; no employee-level assignment required.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
