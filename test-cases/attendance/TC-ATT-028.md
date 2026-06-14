---
id: TC-ATT-028
user_story: US-ATT-003
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-028: Duplicate pending regularization for the same date is rejected with the exact duplicate message (negative)

## 1. Test Objective
Verify that when an employee already has a PENDING regularization for a given date, a second submission for the same date is rejected with the exact message "A pending regularization request already exists for this date.", no second row is created, and the original pending request is left untouched. Also confirm that a non-pending prior request (e.g., REJECTED or CANCELLED) for the same date does NOT block a new submission (only one PENDING per date is the rule, per BR-3).

## 2. Related Requirements
- User Story: US-ATT-003
- Acceptance Criteria: AC-4
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured, lookback = 7 days.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.
- Jordan Lee already has ONE PENDING regularization for the target date (3 days ago).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| date | today - 3 days | Has an existing PENDING regularization |
| Existing request status | PENDING | The blocker |
| Second attempt | same date, MISSED_BOTH | Should be rejected |
| reason | "Re-submitting because I forgot details." | >= 10 chars |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | View attendance history for the target date | A "Pending" status pill is already shown; the "Request Regularization" action is disabled for that date or surfaces a duplicate notice. |
| 2 | Force a second submit via API for the same date | Response status is 409 Conflict (or 422). No second row is created. |
| 3 | Inspect the error body / UI message | The message reads exactly: "A pending regularization request already exists for this date." |
| 4 | Verify the database | Still exactly ONE regularization row for the target date; its `status`/`created_at` are unchanged; no duplicate workflow instance. |
| 5 | Set the existing request to REJECTED, then re-submit for the same date | The new submission SUCCEEDS (201) -- BR-3 blocks only a concurrent PENDING, not a previously rejected/cancelled one. A new PENDING row is created. |

## 6. Postconditions
- At most one PENDING regularization per employee per date; the duplicate attempt left the original intact.
- A non-pending prior request does not block a fresh submission for the same date.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
