---
id: TC-LV-177
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-177: Cancelled leaves are not shown on the calendar (BR-4)

## 1. Test Objective
Verify that leave requests in a Cancelled state are excluded from all calendar views (month, week, list) for managers, employees, and HR.

## 2. Related Requirements
- User Story: US-LV-009
- Business Rules: BR-4
- Functional Requirements: FR-1, FR-2, FR-3

## 3. Preconditions
- Tenant "acme"; Manager "Maya"; direct report "Sam" had an Approved leave 2026-06-08..10 that is then Cancelled.
- Another direct report "Ravi" has a current Approved leave 2026-06-20.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Sam | Annual, Cancelled, 2026-06-08..10 | must NOT appear |
| Ravi | Annual, Approved, 2026-06-20 | must appear |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya loads the month view | Ravi's approved leave appears; Sam's cancelled leave does NOT appear on 2026-06-08..10. |
| 2 | Switch to week and list views | Cancelled leave remains absent across all view modes. |
| 3 | View as employee Nina (department-approved view) | Cancelled leave is absent (only approved-and-active leaves are shown). |
| 4 | Cancel Ravi's leave, reload | Ravi's leave disappears from the calendar after cancellation. |

## 6. Postconditions
- Cancelled leaves are excluded from every calendar view and persona.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
