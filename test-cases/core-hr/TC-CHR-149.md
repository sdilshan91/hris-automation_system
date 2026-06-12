---
id: TC-CHR-149
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-149: Multi-filter combination -- department + status + job title + employment type

## 1. Test Objective
Verify that applying multiple filters simultaneously (department, status, job title, employment type) produces a correctly intersected result set, and that all active filter chips are displayed and individually removable. This validates FR-2 with complex filter combinations.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2, FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 100 employees exist across varied departments, statuses, job titles, and employment types.
- At least 3 employees match: Department = "Engineering", Status = "active", Job Title = "Senior Developer", Employment Type = "Full-Time".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter: Department | Engineering | |
| Filter: Status | active | |
| Filter: Job Title | Senior Developer | |
| Filter: Employment Type | Full-Time | |
| Expected match count | 3 | Exact intersection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory | Full directory loads. |
| 2 | Open filters and select all four filters | Department: Engineering, Status: active, Job Title: Senior Developer, Employment Type: Full-Time. |
| 3 | Click "Apply Filters" | Directory shows 3 employees matching all four criteria. |
| 4 | Verify four filter chips are displayed | "Department: Engineering", "Status: active", "Job Title: Senior Developer", "Employment Type: Full-Time". |
| 5 | Verify URL contains all filter params | `?departments=Engineering&statuses=active&jobTitles=Senior Developer&employmentTypes=Full-Time`. |
| 6 | Remove "Job Title" chip | Results expand to include all active, full-time Engineering employees (more than 3). |
| 7 | Remove "Employment Type" chip | Results expand further. |
| 8 | Verify filters are additive (AND logic) | Each filter narrows the results; removing a filter widens them. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
