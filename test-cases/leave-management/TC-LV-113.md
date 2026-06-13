---
id: TC-LV-113
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-113: Upcoming Leaves section lists approved and pending future requests (happy path)

## 1. Test Objective
Verify that the dashboard "Upcoming Leaves" section calls `GET /api/v1/leaves/my-upcoming` and lists all approved and pending future leave requests with dates, type, status, and day count (AC-3, FR-4).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated; today is 2026-06-14.
- Nina has: 1 Approved future request (Annual, 2026-07-01..2026-07-03), 1 Pending future request (Casual, 2026-08-10, 1 day), 1 Approved PAST request (2026-03-01..2026-03-02), 1 Rejected future request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Approved future | Annual 2026-07-01..07-03 | 3 days |
| Pending future | Casual 2026-08-10 | 1 day |
| Approved past | 2026-03-01..03-02 | Should NOT appear |
| Rejected future | -- | Should NOT appear |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard and view the Upcoming Leaves section | `GET /api/v1/leaves/my-upcoming` returns the Approved-future and Pending-future requests only. |
| 2 | Inspect each listed item | Each shows start/end dates (date chips), leave type, status badge (Approved/Pending), and day count. |
| 3 | Confirm exclusions | The past approved request and the rejected request are NOT listed in Upcoming Leaves. |
| 4 | Verify ordering | Future leaves are ordered chronologically (soonest first) in a timeline-style list. |

## 6. Postconditions
- Upcoming Leaves shows exactly the future approved + pending requests; read-only.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
