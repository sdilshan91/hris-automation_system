---
id: TC-ATT-060
user_story: US-ATT-005
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-060: Delete prevention when a shift is assigned -- exact "shift_in_use" message; deletion succeeds after reassignment (AC-4, FR-6)

## 1. Test Objective
Verify the delete-protection rule (AC-4, FR-6, BR-2): deleting a shift that has active employee assignments is blocked with HTTP 409 and the EXACT message "This shift is assigned to {N} employees. Please reassign them before deleting." (where {N} is the count of currently-assigned employees), error code `shift_in_use`. Once all assignments are reassigned/ended, the same delete succeeds.

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6 (prevent deletion of shifts with active assignments)
- API contract: DELETE returns 409 `shift_in_use` with the exact message

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.
- Shift "Day Shift" (shift_id known) is currently assigned to 3 employees (active employee_shift rows).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift_id | Day Shift | Currently assigned |
| assigned count {N} | 3 | Must appear in the message |
| Exact message | "This shift is assigned to 3 employees. Please reassign them before deleting." | Verbatim |
| Error code | shift_in_use | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR, `DELETE /api/v1/attendance/shifts/{shift_id}` while 3 employees are assigned | Response 409 with code `shift_in_use` and the EXACT message "This shift is assigned to 3 employees. Please reassign them before deleting." |
| 2 | Verify no deletion occurred | The shift still exists (or is_active unchanged); the 3 employee_shift rows are intact. |
| 3 | Confirm {N} is dynamic | Reassign 1 of the 3 away; re-attempt delete -> message now reads "...assigned to 2 employees..." (the count reflects the current active assignments). |
| 4 | Reassign all remaining employees off the shift (or end their assignments) | The shift now has zero active assignments. |
| 5 | `DELETE .../shifts/{shift_id}` again | Response 200/204; the shift is deleted (or soft-deactivated via is_active=false per the implemented delete semantics) since it is no longer in use. |
| 6 | Verify audit | The blocked attempt and the successful delete are recorded in the audit log, tenant-scoped. |

## 6. Postconditions
- An in-use shift cannot be deleted (exact message surfaced); after reassignment the shift is deletable; history preserved per the delete semantics.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
