---
id: TC-ATT-086
user_story: US-ATT-007
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-086: On-demand generation for the current (incomplete) month -- partial summary up to today + progress indicator (happy path)

## 1. Test Objective
Verify AC-3/FR-4: when the monthly summary has not yet been generated for the current (incomplete) month and the HR Officer requests it, the system triggers an on-demand calculation via Hangfire (`POST /api/v1/attendance/summary/monthly/generate?month=YYYY-MM`), shows a progress indicator while it runs, and produces a PARTIAL summary computed only up to the current date (no future dates/projections).

## 2. Related Requirements
- User Story: US-ATT-007
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4 (on-demand generation for the current incomplete month)
- Assumptions/Constraints: S10 (on-demand computes up to the current date; no future dates)

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with `Attendance.Read.All`.
- Current date is mid-month (e.g. 2026-06-14); the 2026-06 monthly summary has NOT been generated yet (the 1st-of-month job is for the previous month).
- Attendance data exists for 2026-06-01 through 2026-06-14 for acme employees.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-06 | current incomplete month |
| current date | 2026-06-14 | compute boundary |
| pre-state | no summary rows for 2026-06 | triggers generation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, request the 2026-06 summary with none generated yet | The system triggers on-demand generation via Hangfire (`POST /summary/monthly/generate?month=2026-06`) and returns a job-accepted/in-progress response; a progress indicator is shown in the UI (AC-3). |
| 2 | Poll/await job completion | The Hangfire summary job completes and the summary becomes available; the progress indicator resolves to the rendered table. |
| 3 | Verify the partial summary boundary | Counts (present/absent/leave/etc.) cover only 2026-06-01..2026-06-14; days 2026-06-15 onward are NOT counted as absent or present (no future projection, S10). |
| 4 | Verify generated_at | The attendance_monthly_summary rows have generated_at set to the on-demand run time. |
| 5 | Re-request the same month after generation | The already-generated summary is served (re-generation only on explicit request / per the refresh rule), not recomputed silently. |
| 6 | Verify tenant scope | Generation runs tenant-scoped (acme only); the Hangfire job uses the resolved tenant context (S10). |

## 6. Postconditions
- A partial, tenant-scoped summary for the current month up to today exists with generated_at stamped; future dates are excluded; the progress indicator resolved to the table.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The "compute up to current date" boundary uses UTC day semantics (tenant-timezone infra DEFERRED module-wide). If tenant-local "today" is required, that is the same deferred concern. **Reported to caller.**
- Whether re-requesting an already-generated current month forces a refresh or serves the cached/materialized rows follows the documented refresh rule; assert against it and flag if unspecified.
