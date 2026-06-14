---
id: TC-LV-174
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-174: Mobile 360px adapts to a compact list view grouped by date (employee, leave type, status) (AC-4, NFR-4)

## 1. Test Objective
Verify that at a 360px viewport the calendar defaults to / adapts into a compact list view grouped by date, where each entry shows employee name, leave type, and status, optimized for touch.

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8 (mobile defaults to list view; touch-optimized)

## 3. Preconditions
- Manager "Maya" authenticated on a 360px-wide viewport (Chrome device emulation, e.g. Galaxy S-class).
- Direct reports have several leaves across multiple dates in the viewed range.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport | 360 x 800 | NFR-4 lower bound |
| Grouping | by date (chronological) | AC-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Team Leave Calendar at 360px | The view renders as (or can toggle to) a compact list grouped by date; the heavy month grid does not overflow horizontally. |
| 2 | Inspect a date group | Each entry within a date shows employee name, leave type, and status; entries are chronologically grouped under their date header. |
| 3 | Verify an employee-context list (Nina) at 360px | The employee's list shows "on leave" entries only (no pending, no leave-type) consistent with AC-2/BR-1, still grouped by date. |
| 4 | Use the view toggle on mobile | Month/Week are reachable via the toggle but List is the touch-optimized default; tap targets are >= 44px. |

## 6. Postconditions
- Calendar is usable at 360px as a date-grouped compact list with employee/type/status per entry.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
