---
id: TC-LV-214
user_story: US-LV-011
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-214: Absenteeism job is idempotent — re-running does not create duplicate LOP entries for the same absent day (CONDITIONAL on Attendance)

## 1. Test Objective
Verify that re-running `ProcessAbsenteeismJob` (daily or on-demand) for a period that already has a System-Generated LOP entry for an employee/day does NOT create a duplicate entry (FR-2 idempotency, consistent with the module's other Hangfire jobs). The attendance-driven trigger DEPENDS on the Attendance module — the no-op seam path is verified live and the de-duplication is verified conditionally.

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Dependency: US-ATTENDANCE-* — CONDITIONAL

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Absent day | one past working day | single unaccounted absence |
| Job runs | 2 | re-run same period |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Seam, live) Run the job twice with the no-op attendance provider | Both runs complete; zero LOP entries created either time (no source). |
| 2 | (CONDITIONAL) Seed one absence; run the job; then run it again for the same period | After the first run, exactly one System-Generated LOP entry exists; after the second run, still exactly one (no duplicate). Mark CONDITIONAL on Attendance. |
| 3 | Verify the de-dup key | The job skips an employee/day that already has an LOP entry (no over-counting that would inflate the payroll deduction). |

## 6. Postconditions
- The auto-LOP job is idempotent; no duplicate System-Generated entries accumulate across runs.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
