---
id: TC-ATT-074
user_story: US-ATT-006
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-074: Manager approves overtime -- status APPROVED and the record is flagged payroll-ready (happy path)

## 1. Test Objective
Verify AC-4/FR-6/FR-7: when a manager approves a PENDING overtime record (`POST /api/v1/attendance/overtime/{id}/approve`), the status becomes APPROVED and the record is flagged for payroll integration (payroll-ready). With no adjustment, `approved_minutes` equals the detected `overtime_minutes`. The approval is auditable (manager id, timestamp, optional comment).

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6 (approve/reject/adjust), FR-7 (approved overtime flagged payroll-ready)

## 3. Preconditions
- Tenant "acme". Manager "Ben" authenticated with the overtime-approve permission; direct report "Asha" has a PENDING overtime_record (overtime_minutes = 60).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| overtime_record.overtime_minutes | 60 | pre-approval |
| approve body | { comment?: "Approved for project deadline" } | optional comment |
| expected status | APPROVED | AC-4 |
| expected approved_minutes | 60 | equals detected (no adjustment) |
| expected payroll-ready | true | FR-7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben, `POST /api/v1/attendance/overtime/{ashaRecordId}/approve` with an optional comment | 200; status -> APPROVED, `approved_minutes = 60`, manager_comment stored, tenant-scoped. |
| 2 | Inspect the payroll-ready flag | The record is flagged payroll-ready (FR-7) so the payroll module/US-ATT-009 can consume it. |
| 3 | Inspect the audit log | An audit entry records action=approve, actor=Ben, timestamp, target overtime_id, and the comment. |
| 4 | `GET /overtime/pending` as Ben | The approved record no longer appears in the PENDING queue. |
| 5 | `GET /overtime/my` as Asha | Asha's record shows status APPROVED with approved_minutes = 60 (green tag per §8). |

## 6. Postconditions
- The overtime record is APPROVED, payroll-ready, with approved_minutes set; the action is audited and removed from the pending queue.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Payroll-ready -> payroll consumption (FR-7):** the attendance side sets the payroll-ready flag now; the actual consumption by the payroll engine is US-ATT-009 / the Payroll module, CONDITIONAL on it. **Reported to caller.**
- **Single-level approval:** the multi-level Approval Workflow Engine (US-ADM-007) is DEFERRED; this verifies the default single-level final approval, consistent with US-ATT-004 TC-ATT-037. **Reported to caller.**
