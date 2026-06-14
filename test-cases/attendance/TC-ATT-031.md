---
id: TC-ATT-031
user_story: US-ATT-003
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-031: Lookback boundary -- a date exactly N days ago is accepted; N+1 days ago is rejected (boundary)

## 1. Test Objective
Verify the exact edge of the tenant-configurable lookback window (FR-6/BR-2): with lookback = N (default 7), a regularization for the date exactly N days ago (the oldest still-allowed day) is ACCEPTED, while the date N+1 days ago (one day past the window) is REJECTED with the exact lookback message. The window edge is computed in the tenant's timezone, not the server's UTC date.

## 2. Related Requirements
- User Story: US-ATT-003
- Acceptance Criteria: AC-3
- Functional Requirements: FR-6
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`, regularization workflow configured.
- Tenant regularization lookback period N = 7 days.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.
- Neither boundary date has an existing record or a pending regularization (so only the lookback rule is exercised).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| N (lookback) | 7 days | Tenant config |
| Edge-inside date | today - 7 days | Oldest allowed (inclusive) |
| Edge-outside date | today - 8 days | One day past the window |
| regularization_type | MISSED_BOTH | |
| requested times | 09:00 / 17:30 local | |
| reason | "Forgot to clock in and out that day." | >= 10 chars |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit a regularization for the date exactly 7 days ago | Response 201 Created; a PENDING regularization is created (the Nth day is inclusive of the window). |
| 2 | Submit a regularization for the date 8 days ago | Response 422 (or 400); rejected with the exact message "Regularization requests can only be submitted for the last 7 days." No row created. |
| 3 | Confirm the edge is evaluated in tenant-local time | The "N days ago" calculation uses the tenant timezone (America/New_York) and the local calendar date, so an employee near midnight UTC is not misclassified. |
| 4 | Re-run with N changed to 14 in tenant settings | The accepted/rejected edge shifts to today-14 (accepted) / today-15 (rejected), confirming the boundary tracks the tenant-configurable value (BR-2). |

## 6. Postconditions
- The day exactly N days ago is accepted; N+1 days ago is rejected; the edge follows the tenant-configured N and tenant timezone.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
