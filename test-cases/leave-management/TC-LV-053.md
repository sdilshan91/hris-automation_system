---
id: TC-LV-053
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-053: Leave request for a past date beyond the lookback window is rejected

## 1. Test Objective
Verify that an Employee cannot submit a leave request for a past date that falls beyond the tenant-configurable lookback window, and that the system rejects the request with a clear message. (Test Hint: apply for a date 30 days in the past with a 7-day lookback; verify rejection.)

## 2. Related Requirements
- User Story: US-LV-003
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- The tenant's past-date lookback window is configured to 7 days.
- Today's date is 2026-07-06.
- Jane has sufficient balance.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Today | 2026-07-06 | Reference date |
| Lookback window | 7 days | Tenant-configurable (BR-1) |
| Past date beyond window | 2026-06-06 | 30 days in the past -> rejected |
| Past date within window | 2026-07-01 | 5 days in the past -> allowed |
| Boundary date | 2026-06-29 | Exactly 7 days back -> allowed (inclusive) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page; select Annual Leave with Start/End = 2026-06-06 | Date picker may disable dates older than the lookback window; if reachable, an inline error appears. |
| 2 | Force submission of the 2026-06-06 request via `POST /api/v1/leaves` | Server returns 400/422: "Leave cannot be applied for dates more than 7 days in the past." No request created. |
| 3 | Submit a request for 2026-07-01 (within the 7-day lookback) | Request accepted (201 Created). |
| 4 | Submit a request for exactly 2026-06-29 (7 days back, boundary) | Request accepted -- boundary is inclusive of the configured window. |
| 5 | Submit a request for 2026-06-28 (8 days back, just beyond window) | Request rejected with the past-date error. |

## 6. Postconditions
- No request is created for dates beyond the lookback window.
- Requests within (and at) the window boundary are created with status "Pending".

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
