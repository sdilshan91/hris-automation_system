---
id: TC-LV-069
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-069: Page size is capped at 50 to prevent excessive data transfer

## 1. Test Objective
Verify that the maximum page size for the pending queue is capped at 50: a client requesting a larger `pageSize` is served at most 50 items per page, consistent with the constraint in the user story (Section 10).

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-2
- Functional Requirements: FR-4
- Constraints: Section 10 (max page size 50)

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's team has 60 pending leave requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Total pending requests | 60 | Exceeds cap |
| Max page size | 50 | Section 10 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/pending?page=1&pageSize=50` | Response 200 with exactly 50 items; `pageSize: 50`, `totalCount: 60`. |
| 2 | Call `GET /api/v1/leaves/pending?page=1&pageSize=200` | Response 200 with at most 50 items; effective `pageSize` is clamped to 50 (not 200) -- either silently capped or returned as `pageSize: 50`. |
| 3 | Call `GET /api/v1/leaves/pending?page=2&pageSize=50` | Response 200 with the remaining 10 items (51--60). |
| 4 | Verify total transfer is bounded | No single response returns more than 50 request items, regardless of requested `pageSize`. |

## 6. Postconditions
- No data mutated.
- Page size is bounded at 50 across all requests.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
