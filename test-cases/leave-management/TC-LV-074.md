---
id: TC-LV-074
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-074: Filter returning no matches shows an empty state, not an error

## 1. Test Objective
Verify that when a filter combination matches no pending requests, the queue shows a clear "no results" empty state with an option to clear filters, and the API returns an empty result set with a zero total count.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-4

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's team has pending Annual and Sick requests, but no Bereavement requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter | leaveType = "Bereavement Leave" | No matches |
| Pending of that type | 0 | Empty result expected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply the leave-type filter "Bereavement Leave" | `GET /api/v1/leaves/pending?leaveTypeId={bereavement}` returns 200 with `items: []` and `totalCount: 0`. |
| 2 | Observe the UI | A clear "No matching requests" empty state is shown with a "Clear filters" action; no spinner stuck, no error toast. |
| 3 | Apply an impossible combined filter (e.g. employee + out-of-range date) | Empty result; same empty-state messaging. |
| 4 | Click "Clear filters" | All filters removed; the full unfiltered queue is restored. |

## 6. Postconditions
- No data mutated.
- Zero-match filters render a graceful empty state with a recovery action.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
