---
id: TC-LV-072
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-072: Filter the pending queue by employee returns only that employee's requests

## 1. Test Objective
Verify that applying an employee filter to the pending queue performs server-side filtering and returns only the pending requests submitted by the selected direct report.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.
- Direct reports Jane Smith (2 pending) and Alan Park (1 pending) both have pending requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter | employee = "Jane Smith" | By employee |
| Jane pending | 2 | Should be returned |
| Alan pending | 1 | Should be excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply the employee filter "Jane Smith" | An API call `GET /api/v1/leaves/pending?employeeId={jane}` is issued (server-side filtering). |
| 2 | Verify the response | Exactly Jane's 2 pending requests are returned; `totalCount: 2`; Alan's request is absent. |
| 3 | Verify the employee filter is limited to direct reports | The employee selector only offers Robert's direct reports; selecting/forcing a non-report employee id returns no rows (BR-1). |
| 4 | Combine with a leave-type filter | `?employeeId={jane}&leaveTypeId={annual}` returns only Jane's Annual requests; filters compose with AND semantics. |
| 5 | Clear filters | The full queue is restored. |

## 6. Postconditions
- No data mutated.
- Employee filter narrows results to a single direct report; composes with other filters.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
