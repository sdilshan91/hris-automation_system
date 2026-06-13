---
id: TC-LV-073
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-073: Filter the pending queue by date range returns only overlapping requests

## 1. Test Objective
Verify that applying a date-range filter to the pending queue performs server-side filtering and returns only requests whose leave period falls within (or overlaps) the selected date range.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Robert's team has pending requests spanning July and August 2026.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter range | 2026-07-01 .. 2026-07-31 | July only |
| Request in July | Jane, 2026-07-06..07-08 | Should be returned |
| Request spanning Jul/Aug | Alan, 2026-07-30..08-02 | Overlaps -- should be returned |
| Request in August | Priya, 2026-08-10..08-12 | Should be excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply the date-range filter 2026-07-01 to 2026-07-31 | An API call `GET /api/v1/leaves/pending?from=2026-07-01&to=2026-07-31` is issued. |
| 2 | Verify the response | Jane's July request and Alan's Jul/Aug-overlapping request are returned; Priya's August request is excluded. |
| 3 | Verify boundary inclusion | A request starting exactly on 2026-07-31 is included; a request starting 2026-08-01 is excluded. |
| 4 | Verify invalid range handling | Submitting `from` later than `to` returns a 400 validation error or an empty result, not a 500. |
| 5 | Clear the filter | The full queue is restored. |

## 6. Postconditions
- No data mutated.
- Date-range filter narrows results to overlapping requests within team scope.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
