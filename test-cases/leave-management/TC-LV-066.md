---
id: TC-LV-066
user_story: US-LV-004
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-066: Pending leave queue loads with team requests sorted oldest-first and inline balance (happy path)

## 1. Test Objective
Verify that an authenticated Manager with the `Leave.Approve.Team` permission, navigating to the Leave Approvals page, sees a queue of all pending leave requests from their direct reports, sorted by requested date ascending (oldest first), with each row showing employee name, leave type, date range, total days, reason, and the inline current balance for that leave type.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-1, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- Manager "Robert Lee" has an active employee record in "acme" and is authenticated.
- Robert has the `Leave.Approve.Team` permission.
- Robert has three direct reports (`manager_employee_id = Robert`): Jane Smith, Alan Park, Priya Nair.
- Each direct report has at least one pending leave request with distinct `requested_at` timestamps.
- Leave types "Annual Leave" and "Sick Leave" exist and are active (US-LV-001), each with a color.
- Each requesting employee has a computed leave balance for the requested type (US-LV-002).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Manager | Robert Lee | Has Leave.Approve.Team |
| Request A | Jane Smith, Annual Leave, 2026-07-06..07-08, 3 days, requested 2026-06-01 | Oldest |
| Request B | Alan Park, Sick Leave, 2026-07-10..07-10, 1 day, requested 2026-06-05 | Middle |
| Request C | Priya Nair, Annual Leave, 2026-07-13..07-17, 5 days, requested 2026-06-09 | Newest |
| Jane balance (Annual) | 11.00 days | Inline pill |
| Alan balance (Sick) | 6.00 days | Inline pill |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Leave Approvals page at `https://acme.yourhrm.com/leave/approvals` | Page loads; an API call `GET /api/v1/leaves/pending` is issued with `X-Tenant-Subdomain: acme` and returns 200. |
| 2 | Observe the queue ordering | Rows appear sorted by `requested_at` ascending: Request A (Jane) first, then Request B (Alan), then Request C (Priya). |
| 3 | Inspect each row's columns | Each row shows employee name, leave type (with color badge), start/end dates, total days, reason, and an inline current-balance pill for that leave type. |
| 4 | Verify the API response item shape | Each item contains `requestId`, `employeeName`, `employeePhoto`, `leaveTypeName`, `leaveTypeColor`, `startDate`, `endDate`, `totalDays`, `reason`, `hasAttachments`, `currentBalance`, `requestedAt` (FR-2). |
| 5 | Verify only Robert's direct reports appear | Requests from employees not reporting to Robert are absent (BR-1). |
| 6 | Verify the inline balance is the current real-time balance | The balance pill equals the employee's current balance from the LeaveLedger running total, not the balance captured at request time (BR-4). |
| 7 | Verify total count is displayed | The page shows the total number of pending requests (3). |

## 6. Postconditions
- No data is mutated; the queue is read-only.
- Robert sees exactly his three direct reports' pending requests, oldest first.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
