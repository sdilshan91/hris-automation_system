---
id: TC-ATT-088
user_story: US-ATT-007
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-088: Filters -- department, location, shift, and employee status each scope the summary to matching employees only (AC-5/FR-5)

## 1. Test Objective
Verify AC-5/FR-5: selecting a department filter shows the summary only for employees in that department; and that the additional FR-5 filters -- location, shift, and employee status -- each likewise scope the result set, and combine (AND) correctly, while remaining tenant-scoped.

## 2. Related Requirements
- User Story: US-ATT-007
- Acceptance Criteria: AC-5 (department filter)
- Functional Requirements: FR-5 (filter by department, location, shift, employee status)
- UI/UX Notes: §8 (Notion-style filter chips)

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with `Attendance.Read.All`.
- Month 2026-05 summary generated. acme has >= 2 departments (Engineering, Sales), >= 2 locations, >= 2 shifts, and a mix of employee statuses (Active, Probation, plus a Terminated employee with May data).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | selected period |
| departmentId | Engineering | AC-5 |
| location | HQ | FR-5 |
| shift | Day Shift | FR-5 |
| status | Active | FR-5 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /summary/monthly?month=2026-05&departmentId={engineering}` | 200; only Engineering employees appear; Sales employees are absent (AC-5). |
| 2 | Filter by location = HQ | Only employees assigned to the HQ location appear (FR-5). |
| 3 | Filter by shift = Day Shift | Only employees on the Day Shift appear (FR-5). |
| 4 | Filter by employee status = Active | Only Active employees appear; the Terminated employee is excluded (FR-5). |
| 5 | Combine department=Engineering AND status=Active | Only Active Engineering employees appear (filters AND together); the banner totals/average recompute for the filtered set. |
| 6 | Clear all filters | The full tenant employee set returns (matches TC-ATT-084). |
| 7 | Filter by a department with no matching employees | Returns an empty/zeroed result with an informative empty state, not an error. |

## 6. Postconditions
- The summary correctly scopes to each filter and to combined filters, always within the acme tenant; clearing filters restores the full set.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Department/location/shift/employee-status reference data comes from Core HR and US-ATT-005; this TC filters against seeded values.
- A filter value belonging to another tenant must not leak rows; cross-tenant filter isolation is covered by TC-ATT-ISO-010.
