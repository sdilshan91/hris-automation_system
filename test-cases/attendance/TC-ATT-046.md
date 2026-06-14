---
id: TC-ATT-046
user_story: US-ATT-004
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-046: Bulk approval -- a manager selects multiple PENDING requests and approves them in a single action; all eligible requests are processed (happy path + partial-result)

## 1. Test Objective
Verify BR-7: a manager can select multiple PENDING regularizations from the queue and approve them with one action; every eligible request transitions to APPROVED, each gets its attendance_log created/updated with recalculated total_work_minutes, each employee is notified, and each action is audited individually. A request that is ineligible (out-of-team, already decided, or in a locked period) is reported in a per-item result without blocking the eligible ones.

## 2. Related Requirements
- User Story: US-ATT-004
- Business Rule: BR-7 (bulk approval -- select multiple, approve in one action)
- Functional Requirements: FR-2 (per-request attendance_log create/update), FR-6 (audit per action), FR-7 (each item still relationship-checked)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- Three PENDING requests for Dana's direct reports: R1 (Jordan), R2 (Morgan), R3 (Morgan).
- One ineligible request R4 belongs to Sam Park (NOT Dana's direct report).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Bulk approve set (eligible) | [R1, R2, R3] | 3 direct-report PENDING requests |
| Injected ineligible | R4 (Sam Park) | out-of-team -- must be reported, not applied |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `POST /api/v1/attendance/regularizations/bulk-approve` with `{ ids: [R1,R2,R3] }` | Response 200; a per-item result marks all three APPROVED. |
| 2 | Verify each request | R1, R2, R3 are all APPROVED; each has its attendance_log created/updated with recalculated total_work_minutes; each employee notified (seam); each action audited separately with Dana as actor. |
| 3 | Bulk-approve including R4 (out-of-team) | R4 is reported as denied/skipped in the per-item result with the authorization reason (TC-ATT-041 message); the eligible items in the same batch still process. No partial corruption. |
| 4 | Mixed batch with an already-decided id | The decided item is reported as skipped (BR-3) without a second mutation; eligible items still approve. |
| 5 | Confirm queue after bulk approve | The approved rows animate/drop out of the PENDING queue; the badge count decreases accordingly. |

## 6. Postconditions
- All eligible selected requests are approved with their per-request side effects and audits; ineligible items are reported per-item without blocking the batch.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Each item in a bulk action is independently relationship-checked (FR-7), immutability-checked (BR-3), and payroll-lock-checked (BR-5) -- bulk is a convenience wrapper, not a bypass of per-item rules. Per-item atomicity follows TC-ATT-047.
