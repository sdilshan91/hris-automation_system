---
id: TC-LV-173
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-173: Manager week view renders a Gantt-like grid -- employee names on Y-axis, days on X-axis (AC-3, FR-5)

## 1. Test Objective
Verify the Manager can toggle from month view to week view, which displays a detailed Gantt-style grid with direct-report employee names as row labels (Y-axis) and the days of the week as columns (X-axis), with leave rendered as horizontal bars spanning the leave dates.

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5
- UI/UX Notes: Section 8 (segmented Month | Week | List control; Gantt-style bars)

## 3. Preconditions
- Tenant "acme"; Manager "Maya" with direct reports having leaves within the selected week.
- "Sam": Annual Approved 2026-06-08..10 (Mon-Wed of the viewed week).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| View toggle | Month \| Week \| List segmented control | Section 8 |
| Week | 2026-06-08 (Mon) .. 2026-06-14 (Sun) | X-axis days |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya toggles the view control to Week | The calendar switches to a week grid; employee names appear as Y-axis row labels and the 7 days appear as X-axis columns. |
| 2 | Inspect Sam's row | Sam's Annual leave renders as a horizontal Gantt bar spanning Mon-Wed (2026-06-08..10) and stopping at the leave end. |
| 3 | Navigate to the next/previous week | The X-axis day columns advance/retreat by a week; bars re-clip to the visible week range. |
| 4 | Toggle back to Month | The view returns to the month grid retaining the same underlying data (no extra fetch needed for the same range, or a correct refetch). |

## 6. Postconditions
- Week view shows a Gantt-like grid (employees Y-axis, days X-axis); view toggle works both directions.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
