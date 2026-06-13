---
id: TC-LV-071
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-071: Filter the pending queue by leave type returns only matching requests

## 1. Test Objective
Verify that applying a leave-type filter (e.g. "Sick") to the pending queue performs server-side filtering and returns only requests of that leave type from the manager's direct reports. (Test Hint: filter by leave type "Sick"; verify only sick leave requests are returned.)

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's team has pending requests across multiple leave types: 3 Annual Leave, 2 Sick Leave, 1 Casual Leave.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter | leaveType = "Sick Leave" | By type |
| Annual requests | 3 | Should be excluded |
| Sick requests | 2 | Should be returned |
| Casual requests | 1 | Should be excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Approvals page and apply the leave-type filter "Sick Leave" | An API call `GET /api/v1/leaves/pending?leaveTypeId={sick}` is issued (server-side filtering, FR-3). |
| 2 | Verify the response | Exactly the 2 Sick Leave requests are returned; `totalCount: 2`; no Annual or Casual requests appear. |
| 3 | Verify an active-filter chip is shown | A chip "Sick Leave" appears in the filter bar with a clear/remove affordance. |
| 4 | Remove the filter | The full unfiltered queue (6 requests) is restored. |
| 5 | Verify scope is preserved under the filter | Only Robert's direct reports' Sick requests are returned -- no other manager's team data (BR-1). |

## 6. Postconditions
- No data mutated.
- Leave-type filter narrows results server-side to matching requests only, within team scope.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
