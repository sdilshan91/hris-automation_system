---
id: TC-ATT-021
user_story: US-ATT-002
module: Attendance
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-021: Auto-clock-out Hangfire job closes records left open past end-of-day and flags them for regularization (BR-5)

## 1. Test Objective
Verify BR-5: a recurring end-of-day Hangfire job (e.g., 23:59 in the tenant timezone) finds any `attendance_log` still open (`clock_out` null) for the day, closes it with a system-generated `clock_out`, computes `total_work_minutes`, marks it for regularization (e.g., `status = ANOMALY` or a system-closed flag), and records the system actor in the audit fields — without clobbering records the employee closed manually.

## 2. Related Requirements
- User Story: US-ATT-002
- Functional Requirements: FR-1, FR-2, FR-7
- Business Rules: BR-5
- Assumptions/Constraints: S10 (auto-clock-out is a safety net; UTC server time)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`; tenant end-of-day configured (23:59 local).
- Tenant "globex" also `active` (to confirm the job is tenant-aware and does not bleed).
- Employee "Jordan Lee" (acme) has ONE open record for the day: `clock_in` 09:00 local, `clock_out` null, and did NOT clock out.
- Employee "Pat Kim" (acme) has a MANUALLY completed record for the same day (control: must not be touched).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Job schedule | 23:59 tenant-local | Recurring Hangfire job |
| Open record (Jordan) | clock_in 09:00 local, clock_out null | Left open |
| Completed record (Pat) | clock_in 09:00, clock_out 17:00, COMPLETE | Control, untouched |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger the end-of-day auto-clock-out job for acme (or advance the clock to 23:59 local) | The job runs and scans for open records in acme for the local day. |
| 2 | Verify Jordan's record | `clock_out` is set by the system (system-generated timestamp/end-of-day boundary), `total_work_minutes` computed, the record is flagged for regularization (system-closed / `status = ANOMALY`), `updated_by` = system actor. |
| 3 | Verify Pat's record (control) | Pat's manually completed record is unchanged — the job only touches OPEN records. |
| 4 | Verify tenant scoping | The job processes acme records only for this run; globex's open records (if any) are handled in globex's own tenant-scoped run and never mixed. |
| 5 | Verify idempotency | Re-running the job does not re-close already-closed records or double-write; once closed, a record is skipped. |
| 6 | Verify regularization surfacing | The system-closed record appears in an HR/employee regularization queue so the employee can correct the auto-generated clock-out later. |

## 6. Postconditions
- All open records for the tenant-day are system-closed and flagged for regularization; manually completed records are untouched; the job is idempotent and tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
