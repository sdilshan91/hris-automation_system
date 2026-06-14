---
id: TC-ATT-065
user_story: US-ATT-005
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-065: Bulk shift assignment for up to 500 employees completes within 5 seconds (NFR-2)

## 1. Test Objective
Verify NFR-2 and FR-3 at scale: a single bulk assignment of one shift to up to 500 employees (POST .../shifts/{id}/assign with 500 employeeIds and an effectiveFrom) completes within 5 seconds, creating exactly 500 `employee_shift` records, all tenant-scoped, with the single-active-shift invariant (BR-2) preserved (any prior current assignment for each employee is closed/non-overlapping).

## 2. Related Requirements
- User Story: US-ATT-005
- Non-Functional: NFR-2 (bulk assignment for up to 500 employees < 5s)
- Functional Requirements: FR-3 (bulk assignment), FR-4 (effective dating)
- Business Rules: BR-2 (no overlapping active assignments)

## 3. Preconditions
- Tenant "acme" with >= 500 active employees seeded.
- HR session with `Attendance.Shift.Manage`; target shift "Day Shift" exists.
- Timing measured server-side and end-to-end.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employeeIds | 500 distinct acme employees | bulk set |
| effectiveFrom | today | |
| SLA | <= 5000 ms | NFR-2 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/attendance/shifts/{shift_id}/assign` with 500 employeeIds and effectiveFrom = today | Response 200; the request completes within 5000 ms (measure end-to-end; capture server time). |
| 2 | Count created assignments | Exactly 500 `employee_shift` rows created (one per employee), each tenant_id = acme, effective_from = today, effective_to null. |
| 3 | Verify the single-active invariant at scale (BR-2) | For each of the 500 employees, any prior current assignment is closed so no employee ends with two overlapping active rows. |
| 4 | Verify atomicity/consistency | The bulk operation is all-or-nothing per the documented batch semantics (no partial half-applied set on failure); a mid-batch failure rolls back cleanly. |
| 5 | Repeat once to confirm stability | A second comparable run stays within SLA (no progressive degradation). |

## 6. Postconditions
- 500 tenant-scoped assignments created within 5s; single-active invariant preserved; consistent state.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
