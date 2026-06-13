---
id: TC-LV-075
user_story: US-LV-004
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-075: Sort the pending queue by requested date or start date

## 1. Test Objective
Verify that the pending queue supports server-side sorting by `requestedAt` (default, ascending/oldest-first) and by `startDate`, and that toggling sort direction reorders results consistently.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-1, AC-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's team has pending requests where `requestedAt` order differs from `startDate` order.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Req X | requested 2026-06-01, start 2026-08-20 | Oldest request, latest start |
| Req Y | requested 2026-06-05, start 2026-07-01 | Middle request, earliest start |
| Req Z | requested 2026-06-09, start 2026-07-15 | Newest request, middle start |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the queue with no explicit sort | Default order is `requestedAt` ascending: X, Y, Z (AC-1 oldest-first). |
| 2 | Call `GET /api/v1/leaves/pending?sortBy=startDate&sortDir=asc` | Order becomes by start date ascending: Y, Z, X. |
| 3 | Call `GET /api/v1/leaves/pending?sortBy=requestedAt&sortDir=desc` | Order becomes newest-first: Z, Y, X. |
| 4 | Verify sorting is server-side | The order reflects the API response sequence, not client re-sorting of a single page. |
| 5 | Verify an invalid `sortBy` value | Falls back to the default `requestedAt` ascending or returns 400 -- never a 500. |

## 6. Postconditions
- No data mutated.
- Sorting by requested date and start date works in both directions.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
