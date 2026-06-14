---
id: TC-LV-241
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-241: Filter by department "Engineering" returns only Engineering employees (FR-2, Test Hint)

## 1. Test Objective
Verify the FR-2 Test Hint: applying a department filter of "Engineering" to a report returns only employees in the Engineering department; employees of other departments are excluded.

## 2. Related Requirements
- User Story: US-LV-012
- Functional Requirements: FR-2, FR-6
- Acceptance Criteria: AC-1, AC-2

## 3. Preconditions
- Tenant "acme"; departments "Engineering" and "Sales" each with employees; report data exists for both.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter | departmentId = Engineering | FR-2 |
| Engineering employees | e.g. 6 | expected result set |
| Sales employees | e.g. 5 | must be excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a report with no department filter | Both Engineering and Sales employees appear. |
| 2 | Apply the department filter "Engineering" (server-side query param) | Only the 6 Engineering employees appear; no Sales employee is in the result, count and export both reflect the filter. |
| 3 | Combine with another filter (e.g. employment-type) | The result is the intersection (Engineering AND that employment type). |
| 4 | Clear the filter | Both departments reappear. |

## 6. Postconditions
- Department filtering restricts the report to the selected department's employees only.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
