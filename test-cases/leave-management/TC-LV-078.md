---
id: TC-LV-078
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-078: Team conflict count is shown on a request that overlaps approved leave of same-team members

## 1. Test Objective
Verify that, for each pending request, the queue/detail panel indicates how many team members are already approved off during the overlapping period. (Test Hint: two employees from the same team have overlapping approved leave; verify the conflict count is displayed on a new overlapping request.)

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Direct reports Alan Park and Priya Nair have APPROVED leave overlapping 2026-07-07..07-09.
- Direct report Jane Smith submits a new PENDING request 2026-07-06..07-08, which overlaps both.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Approved leave 1 | Alan Park, 2026-07-07..07-09 | Approved |
| Approved leave 2 | Priya Nair, 2026-07-08..07-10 | Approved |
| New pending request | Jane Smith, 2026-07-06..07-08 | Overlaps both |
| Non-overlapping pending | Other report, 2026-09-01..09-02 | Conflict count 0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the pending queue | Jane's overlapping request and the non-overlapping request both appear. |
| 2 | Inspect Jane's request conflict indicator | It shows a conflict count of 2 (Alan and Priya are approved off during the overlap). |
| 3 | Open Jane's detail panel | The conflict count (2) and -- if the team-calendar snippet is available -- the names/periods of the conflicting teammates are shown; if US-LV-009 team calendar is deferred, the numeric count from FR-5 still renders. |
| 4 | Inspect the non-overlapping request | Its conflict count is 0. |
| 5 | Verify only same-team approved leave is counted | Approved leave of employees outside Robert's team is NOT included in the conflict count (BR-1 scope). |
| 6 | Verify only Approved status counts | Pending requests of teammates are not counted as conflicts -- only approved overlapping leave. |

## 6. Postconditions
- No data mutated.
- Conflict count accurately reflects overlapping approved leave within the manager's team.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
