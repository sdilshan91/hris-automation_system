---
id: TC-LV-054
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-054: Leave request for a future date beyond the future window is rejected

## 1. Test Objective
Verify that an Employee cannot submit a leave request for a future date beyond the tenant-configurable future window, and that the system rejects it with a clear message.

## 2. Related Requirements
- User Story: US-LV-003
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- The tenant's future window is configured to 90 days ahead.
- Today's date is 2026-07-06.
- Jane has sufficient balance.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Today | 2026-07-06 | Reference date |
| Future window | 90 days | Tenant-configurable (BR-2) |
| Beyond window | 2026-12-01 | ~148 days ahead -> rejected |
| Within window | 2026-08-15 | ~40 days ahead -> allowed |
| Boundary date | 2026-10-04 | Exactly 90 days ahead -> allowed (inclusive) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page; select Annual Leave with Start/End = 2026-12-01 | Date picker may disable dates beyond the future window; if reachable, an inline error appears. |
| 2 | Force submission of the 2026-12-01 request | Server returns 400/422: "Leave cannot be applied more than 90 days in advance." No request created. |
| 3 | Submit a request for 2026-08-15 (within window) | Request accepted (201 Created). |
| 4 | Submit a request for exactly 2026-10-04 (90 days ahead, boundary) | Request accepted -- boundary is inclusive. |
| 5 | Submit a request for 2026-10-05 (91 days ahead, just beyond) | Request rejected with the future-window error. |

## 6. Postconditions
- No request is created for dates beyond the future window.
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
