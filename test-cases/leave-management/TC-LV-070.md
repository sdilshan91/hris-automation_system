---
id: TC-LV-070
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-070: Invalid or out-of-range pagination parameters are handled safely

## 1. Test Objective
Verify that invalid or out-of-range `page` / `pageSize` parameters (negative, zero, non-numeric, beyond last page) are handled defensively -- either normalized to safe defaults or rejected with a clear 400 -- without leaking errors or returning unbounded result sets.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-2
- Functional Requirements: FR-4
- Constraints: Section 10 (max page size 50)

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's team has 5 pending leave requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Total pending requests | 5 | Small set |
| Invalid inputs | page=0, page=-1, page=abc, pageSize=0, pageSize=-5, pageSize=abc | Edge values |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/pending?page=0` | Either normalized to page 1 (returns the 5 items) or 400 with a clear validation message -- never a 500. |
| 2 | Call `GET /api/v1/leaves/pending?page=-1` | Normalized to page 1 or 400; no negative offset reaches the query. |
| 3 | Call `GET /api/v1/leaves/pending?page=abc` | 400 validation error (non-numeric); no unhandled exception. |
| 4 | Call `GET /api/v1/leaves/pending?pageSize=0` | Normalized to the default page size (20) or 400; never returns zero-bounded or unbounded results. |
| 5 | Call `GET /api/v1/leaves/pending?pageSize=-5` | Normalized to default or 400; no negative limit reaches the query. |
| 6 | Call `GET /api/v1/leaves/pending?pageSize=abc` | 400 validation error; no unhandled exception. |
| 7 | Call `GET /api/v1/leaves/pending?page=99` (far beyond last page) | 200 with an empty `items` array and correct `totalCount: 5` -- not an error. |
| 8 | Verify across all cases | No 500 response; tenant scoping and direct-reports scoping still applied. |

## 6. Postconditions
- No data mutated.
- Invalid pagination inputs never crash the endpoint or bypass scoping/limits.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
