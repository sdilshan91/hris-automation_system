---
id: TC-ATT-027
user_story: US-ATT-003
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-027: Regularization for a date older than the tenant lookback period is rejected with the exact lookback message (negative)

## 1. Test Objective
Verify that submitting a regularization for a date older than the tenant's configured lookback period is rejected, no `attendance_regularization` row is created, no workflow is initiated, and the system returns the exact message "Regularization requests can only be submitted for the last {N} days." with {N} substituted by the tenant's configured value.

## 2. Related Requirements
- User Story: US-ATT-003
- Acceptance Criteria: AC-3
- Functional Requirements: FR-6
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`, regularization workflow configured.
- Tenant regularization lookback period = 7 days (so the rendered message reads "...the last 7 days.").
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.
- The target date is 10 days ago (older than the 7-day lookback) and has no record.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Lookback period (N) | 7 days | Tenant config; message substitutes N |
| date | today - 10 days | Beyond lookback |
| regularization_type | MISSED_BOTH | |
| requested_clock_in / out | 09:00 / 17:30 local | |
| reason | "Forgot to clock in/out that day." | >= 10 chars |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In the UI, attempt to open regularization for a date 10 days ago | The "Request Regularization" affordance is disabled/hidden for dates outside the lookback window, OR the drawer shows an inline lookback warning before submission. |
| 2 | Force a submit via API: `POST /api/v1/attendance/regularizations` with `date` = today - 10 days | Response status is 422 (or 400). No regularization row is created. |
| 3 | Inspect the error body / UI message | The message reads exactly: "Regularization requests can only be submitted for the last 7 days." (N = the tenant's configured lookback value). |
| 4 | Verify the database | No `attendance_regularization` row exists for the target date; no workflow instance was initiated. |
| 5 | Verify the boundary semantics | The lookback is evaluated in the tenant's timezone (America/New_York), not server UTC date, so the rejection is consistent with the tenant's local calendar. (Exact edge of N vs N+1 days is covered by TC-ATT-031.) |

## 6. Postconditions
- No regularization created; no workflow started; the employee sees the lookback message.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
