---
id: TC-LV-187
user_story: US-LV-009
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-187: Calendar renders smoothly with 50 employees and 200 leave entries (NFR-4, Test Hint)

## 1. Test Objective
Verify the calendar (month, week, and list views) renders and remains interactive with up to 50 team members and 200 leave entries in view, without dropped frames, layout collapse, or noticeable interaction lag.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-4
- Test Hint: "Load 50 employees with 200 leave entries; verify rendering performance."

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" (Leave.ViewAll) or a manager with a 50-report team.
- 50 employees with 200 leave entries (mixed approved/pending, full/half day) within the viewed month.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employees | 50 | NFR-4 upper bound |
| Entries | 200 | NFR-4 upper bound |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the month view with 50 employees / 200 entries | The grid renders without collapse; initial render is smooth (no long-task jank > a few hundred ms). |
| 2 | Switch to the week (Gantt) view | All 50 employee rows render with their bars; scrolling the Y-axis is smooth. |
| 3 | Switch to the list view and scroll | The date-grouped list renders and scrolls smoothly (virtualized or paginated as needed). |
| 4 | Apply/clear a filter at this scale | Filtering updates the view promptly without freezing the UI. |

## 6. Postconditions
- Calendar remains responsive and correct at 50 employees / 200 entries across all views.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
