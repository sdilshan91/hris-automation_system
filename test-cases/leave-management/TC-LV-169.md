---
id: TC-LV-169
user_story: US-LV-009
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-169: Manager month view shows direct reports' approved and pending leaves as colored blocks (AC-1, happy path)

## 1. Test Objective
Verify that when a Manager opens the Team Leave Calendar in month view, a month-grid renders every direct report's approved AND pending leave as a colored bar/block on the correct dates, scoped to the manager's direct reports within the tenant.

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-4, FR-5
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme"; Manager "Maya" authenticated with `Leave.View.Team` (direct reports defined via `ReportsToEmployeeId`).
- Direct reports "Sam" (Annual, Approved, 2026-06-08..2026-06-10) and "Ravi" (Sick, Pending, 2026-06-15) exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | GET /api/v1/leaves/team-calendar?from=2026-06-01&to=2026-06-30 | FR-1 |
| Sam | Annual Leave, Approved, 2026-06-08..10 | colored block, manager sees status+type |
| Ravi | Sick Leave, Pending, 2026-06-15 | pending also shown to manager (FR-2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya opens the Team Leave Calendar (defaults to month view) | The current month grid loads; the API returns Sam's approved and Ravi's pending entries for her direct reports only. |
| 2 | Inspect Sam's leave | A colored block (leave-type color) spans 2026-06-08 to 2026-06-10 on Sam's dates; hover/tooltip shows employee name, leave type, dates, status=Approved. |
| 3 | Inspect Ravi's leave | Ravi's Pending leave on 2026-06-15 renders as a colored block visibly distinguished as pending (e.g. hatched/lighter), with status=Pending in the tooltip (FR-2). |
| 4 | Verify each item carries the FR-4 fields | Response items include employeeId, employeeName, leaveTypeName, color, startDate, endDate, status, totalDays. |

## 6. Postconditions
- Manager month view shows direct reports' approved + pending leaves as colored blocks with full detail; no data mutated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
