---
id: TC-LV-242
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-242: Reports support the full FR-2 filter set — date range, job level, employment type, leave type, employee search (FR-2)

## 1. Test Objective
Verify FR-2: all reports accept the documented filters — date range, department, job level, employment type, leave type, and employee search — individually and in combination, applied server-side.

## 2. Related Requirements
- User Story: US-LV-012
- Functional Requirements: FR-2, FR-6
- Acceptance Criteria: AC-1, AC-2, AC-3

## 3. Preconditions
- Tenant "acme"; data spanning multiple departments, job levels, employment types, leave types, and a known employee name to search.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filters | from/to, departmentId, jobLevelId, employmentType, leaveTypeId, q | FR-2 query params |
| Search | "Otieno" | employee search |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply a date-range filter | Only leave data overlapping the range is aggregated/listed. |
| 2 | Apply job-level and employment-type filters | The result narrows to matching employees (note: job-level filtering depends on a JobLevel dimension — if absent in the codebase, record CONDITIONAL per the entitlement-engine vault note; employment-type verified live). |
| 3 | Apply a leave-type filter | Only the selected leave type contributes to totals/columns. |
| 4 | Type an employee-search term "Otieno" | Only matching employees appear; combine all filters and confirm the result is the intersection, applied server-side (not client-only). |

## 6. Postconditions
- All FR-2 filters work individually and combined; job-level filtering recorded CONDITIONAL if no JobLevel entity exists.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
