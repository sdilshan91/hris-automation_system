---
id: TC-LV-213
user_story: US-LV-011
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-213: Absenteeism reconciliation job auto-generates a System-Generated LOP entry for an unaccounted absence (CONDITIONAL on Attendance module)

## 1. Test Objective
Verify the auto-LOP generation path (AC-2, FR-2): the `ProcessAbsenteeismJob` Hangfire job detects a working day with no clock-in and no approved leave for an employee and creates a `leave_request` of type LOP with status "System-Generated", `is_lop = true`, `lop_source = system_generated`. The attendance-driven trigger DEPENDS on the Attendance module (US-ATTENDANCE-*); this TC verifies the job + the attendance-provider seam behaviour now (the no-op provider generates nothing) and the LOP-entry shape the job WOULD create when attendance data is present.

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-4
- Dependency: US-ATTENDANCE-* (absence/clock-in data) — CONDITIONAL
- Cross-ref: docs/vault/modules/leave-management.md (Hangfire job conventions)

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno" exists.
- Hangfire is available; the absenteeism job is registered.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Absent day | a past Monday (working day) | no clock-in, no approved leave |
| Expected status | System-Generated | per AC-2 |
| Expected source | system_generated | per FR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Seam, live) With the attendance provider in its current no-op state, run `ProcessAbsenteeismJob` | The job completes without error and creates ZERO LOP entries (no attendance source = nothing to reconcile). This is the verified DEFAULT behaviour today; recorded as the no-op seam, not a silent pass. |
| 2 | (CONDITIONAL on Attendance) Seed an unaccounted absence for Mark on the target Monday (no clock-in, no approved leave) and run the job | The job creates ONE `leave_request` for Mark: `leave_type = LOP`, `status = System-Generated`, `is_lop = true`, `lop_source = system_generated`, dated to that Monday, tenant-stamped acme. Mark this step CONDITIONAL on the Attendance module providing the absence signal. |
| 3 | Verify the LOP-entry shape | The generated entry matches the §7 data shape (is_lop, lop_source); no balance is deducted (LOP has no balance — BR-1). |
| 4 | Verify scope | Only Mark's absence produces an entry; employees who clocked in or had approved leave that day produce none. |

## 6. Postconditions
- The job runs safely against the no-op seam today; the System-Generated LOP-entry shape is defined and conditionally verified pending Attendance.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
