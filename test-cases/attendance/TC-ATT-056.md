---
id: TC-ATT-056
user_story: US-ATT-005
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-056: Assign a shift to multiple employees with an effective date -- employee_shift records created for each (happy path, AC-2, FR-3)

## 1. Test Objective
Verify the bulk-assignment flow (AC-2, FR-3, FR-4): an HR Officer assigns one shift to several employees in a single action with an `effectiveFrom` date; the system creates one `employee_shift` record per employee linking employee_id -> shift_id with the effective_from date and a null effective_to (current), all stamped with the session tenant_id.

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3 (bulk assignment), FR-4 (effective_from/effective_to history)
- Data: `employee_shift` table

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer "Priya Shah" authenticated with `Attendance.Shift.Manage`.
- A shift "Day Shift" (shift_id known) exists in acme.
- Three acme employees E1, E2, E3 exist with no current explicit assignment to "Day Shift".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift_id | Day Shift | Target shift |
| employeeIds | [E1, E2, E3] | Bulk set |
| effectiveFrom | today | Assignment start |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `POST /api/v1/attendance/shifts/{shift_id}/assign` with body `{ employeeIds: [E1,E2,E3], effectiveFrom: today }` | Response 200/201; payload confirms 3 assignments created. |
| 2 | Inspect `employee_shift` for E1, E2, E3 | Three rows exist, each with shift_id = Day Shift, the correct employee_id, effective_from = today, effective_to = null (current), tenant_id = acme (stamped, not from body), created_by = Priya. |
| 3 | Resolve each employee's shift for today | `GET .../employees/{Ei}/shift?date=today` returns "Day Shift" for E1, E2, and E3. |
| 4 | Re-assign one employee already on the shift for the same effective date | The system does not create a duplicate overlapping CURRENT row for the same employee+shift+date (idempotent / no-op or rejected per the documented rule); assert deterministic behavior consistent with BR-2. |
| 5 | Verify audit | An audit_log entry records the bulk assignment with actor = Priya, the shift, and the affected employee ids, tenant-scoped. |

## 6. Postconditions
- Three current employee_shift assignments exist for the shift; each resolves correctly; all tenant-scoped and audited.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
