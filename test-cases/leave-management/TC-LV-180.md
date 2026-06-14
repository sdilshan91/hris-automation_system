---
id: TC-LV-180
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-180: Calendar filters by employee, leave type, and status (status filter manager-only) (FR-6, Section 8)

## 1. Test Objective
Verify the calendar's filter bar lets a manager narrow by employee, leave type, and status, that active filters render as chips, and that the status filter is unavailable to employees (whose view never includes pending/status anyway, per BR-1).

## 2. Related Requirements
- User Story: US-LV-009
- Functional Requirements: FR-6
- Business Rules: BR-1, BR-2
- UI/UX Notes: Section 8 (chip-based active filters)

## 3. Preconditions
- Tenant "acme"; Manager "Maya" with direct reports having mixed leave types and approved/pending statuses.
- Employee "Nina" for the employee-side check.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filters (manager) | employee, leaveType, status | FR-6 |
| Filter (employee) | employee, leaveType only | status hidden |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya filters by leave type = Sick | Only Sick leaves of her direct reports remain; an active-filter chip "Sick" appears. |
| 2 | Maya filters by employee = Sam | Only Sam's entries remain; chips stack and can be cleared individually. |
| 3 | Maya filters by status = Pending | Only Pending entries remain (manager-only capability, FR-6). |
| 4 | Nina (employee) opens the filter bar | No status filter is offered; even if a `status` param is forged it is ignored and no pending data returns (cross-ref TC-LV-172). |

## 6. Postconditions
- Filters work for manager (employee/type/status) with chips; employees have no status filter and cannot reveal pending.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
