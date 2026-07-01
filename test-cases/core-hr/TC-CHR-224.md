---
id: TC-CHR-224
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: blocked
created: 2026-06-12
---

# TC-CHR-224: Probation reminder -- daily job sends HR notification when probation end date is within 7 days, no auto-transition (AC-4)

## 1. Test Objective
Verify that the daily background job detects employees in "probation" status whose probation end date is within 7 days (specifically testing at 5 days out as per test hints) and sends a notification to the HR Officer reminding them to confirm (transition to active) or extend probation. Critically, the system does NOT auto-transition the employee. This validates AC-4 and FR-6.

## 2. Related Requirements
- User Story: US-CHR-009
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer user (`hr-officer-uuid`) exists in the "acme" tenant.
- Employee "New Hire" (`emp-probation-uuid`) exists with status `probation` and `date_of_joining` = 85 days ago (meaning probation ends in 5 days, given default 90-day probation period per BR-6).
- The daily probation check background job is configured and can be triggered manually for testing.
- Notification system is available (or stub/log is in place).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | New Hire (emp-probation-uuid) | Status: probation |
| Date of Joining | 2026-03-19 | 85 days before 2026-06-12 |
| Probation Period | 90 days (default) | Per BR-6, tenant-configurable |
| Probation End Date | 2026-06-17 | 5 days from today |
| HR Officer | hr-officer-uuid | Notification recipient |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the employee's current status is `probation`. | Status confirmed as `probation` in the database. |
| 2 | Trigger the daily probation check background job (manually or via Hangfire dashboard). | Job executes successfully. |
| 3 | Verify an HR notification was generated. | A notification record exists targeting the HR Officer(s) of the "acme" tenant. The notification content references "New Hire", mentions the probation end date (2026-06-17), and reminds the HR Officer to confirm (transition to active) or extend probation. DEFERRED -- If the Notification module is not yet built, verify the notification event was dispatched to the event bus or logged. |
| 4 | Verify the employee's status has NOT changed. | Employee status remains `probation`. No automatic transition to `active` or any other status occurred. |
| 5 | Verify no employment history entry was created by the job. | No new `status_change` record exists for this employee from the job run. |
| 6 | Run the job again (simulate next day). | The job should either: (a) send another reminder (daily until resolved), or (b) not duplicate if already notified for this period. Verify behavior matches implementation design. |
| 7 | Test with an employee whose probation end date is 8+ days away. | No notification is generated for this employee (outside the 7-day window). |

## 6. Postconditions
- Employee status remains `probation` (no auto-transition).
- An HR notification was generated (or event dispatched).
- No employment history entries were created by the background job.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30:** STILL BLOCKED — employee status-change is a profile/detail action reached via the Employee Directory (crashed, **BUG-099**), and this assertion (probation reminder job / future-dated background apply / suspended-excluded-from-headcount) is backend/job behavior needing a write + job execution, not a browser-render check.

> **Execution 2026-07-01 (API, acme, tenantadmin):** BLOCKED — background-job + notification, precondition unreachable. Job VERIFIED present by read: `EmployeeStatusService.CheckProbationEndDatesAsync` (line 318, invoked by `ProbationReminderJob`) selects `Status == Probation`, computes `ProbationEndDate = DateOfJoining.AddDays(90)`, and for those within the window logs a WRN reminder (`has probation ending on … (N days remaining)`) with **no auto-transition** (AC-4 ✓ by inspection — the job only reads/logs, never writes status). Not executed live because: (a) the only acme Probation employee is EMP-0033 (DoJ 2026-01-01 → probation end 2026-04-01, already ~90 days past, outside the ≤7-day window) so the job would not fire for it; (b) creating a fresh probation employee whose end date is within 7 days is blocked by BUG-093 (employee create 500s); (c) triggering the recurring Hangfire job + verifying the emitted notification is not report-only-API-reachable. Blocked: precondition-employee-uncreatable (BUG-093) + job-trigger-not-API-reachable.
