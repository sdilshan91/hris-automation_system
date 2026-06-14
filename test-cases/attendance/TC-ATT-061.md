---
id: TC-ATT-061
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-061: Clone an existing shift to create a variant -- a new independent shift with copied parameters and no inherited assignments (FR-8)

## 1. Test Objective
Verify the clone action (FR-8, UI/UX S8 "Clone Shift"): cloning an existing shift creates a NEW `shift` row that copies the source's parameters (type, times, break, grace, minimum_hours, working_days) but gets its own shift_id and a distinct (or suffixed) name, is NOT a default unless explicitly set, and inherits NO employee assignments. The clone is independently editable without affecting the source.

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirements: FR-8 (clone an existing shift to create a variant)
- API contract: POST /api/v1/attendance/shifts/{id}/clone

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.
- Source shift "Day Shift" (shift_id known) exists, is assigned to some employees, and has known parameters.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| source shift_id | Day Shift | To clone |
| clone name | "Day Shift (Copy)" | Distinct, unique per tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR, `POST /api/v1/attendance/shifts/{source_id}/clone` (optionally with a new name) | Response 201; a new shift_id is returned, distinct from the source. |
| 2 | Compare clone vs source parameters | The clone copies type, start/end, break, grace, minimum_hours, working_days from the source. |
| 3 | Verify the clone's name uniqueness | The clone has a distinct name (default suffix like "(Copy)" or the supplied name); a name colliding with an existing shift is rejected per the per-tenant uniqueness rule (cf. TC-ATT-052). |
| 4 | Verify is_default and assignments | The clone has is_default = false (unless explicitly set) and ZERO employee_shift assignments (it does not inherit the source's assignees). |
| 5 | Edit the clone (e.g. change grace to 15) | The clone updates independently; the source shift's parameters and assignments are unchanged. |
| 6 | Verify tenant scope + audit | The clone is stamped tenant_id = acme; an audit_log entry records the clone action. |

## 6. Postconditions
- An independent variant shift exists with copied parameters, no inherited assignments, and no effect on the source.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
