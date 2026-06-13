---
id: TC-LV-068
user_story: US-LV-004
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-068: Server-side pagination boundary -- 25 pending requests return 20 on page 1 and 5 on page 2

## 1. Test Objective
Verify that the pending queue uses server-side pagination with a default page size of 20, returns the correct page slices and a total count, and that ordering is stable across pages. (Test Hint: create 25 pending requests; verify page 1 returns 20 and page 2 returns 5.)

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-2
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's direct reports collectively have exactly 25 pending leave requests with distinct, ordered `requested_at` timestamps.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Total pending requests | 25 | Across Robert's team |
| Default page size | 20 | FR-4 / AC-2 |
| Sort | requested_at ascending | Stable ordering |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/pending?page=1` (no pageSize) | Response 200 with exactly 20 items, `totalCount: 20...`; `page: 1`, `pageSize: 20`, `totalCount: 25`. |
| 2 | Verify page 1 ordering | The 20 items are the 20 oldest by `requested_at`, in ascending order. |
| 3 | Call `GET /api/v1/leaves/pending?page=2` | Response 200 with exactly 5 items (requests 21--25), `page: 2`, `pageSize: 20`, `totalCount: 25`. |
| 4 | Verify no overlap or gap between pages | The 5 page-2 items continue immediately after the 20 page-1 items; no request appears on both pages; none is skipped. |
| 5 | Call `GET /api/v1/leaves/pending?page=3` | Response 200 with an empty `items` array and `totalCount: 25` (beyond last page yields no items, not an error). |
| 6 | Verify the UI pager | The pager shows total count (25) and reflects 2 pages at the default size. |

## 6. Postconditions
- No data mutated.
- Pagination slices are correct, stable, and total count is accurate.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
