---
id: TC-LV-170
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-170: Month grid renders one color-coded block per employee per leave type with a leave-type legend (AC-1, FR-4, Section 8)

## 1. Test Objective
Verify the month view places each leave on the correct employee/date cells with the leave-type color, multiple overlapping employees stack without visual collision, and a color legend for leave types is shown at the top (Section 8).

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-4, FR-5
- UI/UX Notes: Section 8 (color legend, color-coded blocks, today highlight)

## 3. Preconditions
- Tenant "acme"; Manager "Maya" with several direct reports having leaves of different types in the viewed month.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Annual color | #4CAF50 | from leave type config |
| Sick color | #E91E63 | from leave type config |
| Overlap day | 3 employees off on 2026-06-12 | stacking probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the month view | Each leave block uses its leave type's configured `color`; a leave-type legend appears at the top mapping color -> type name (Section 8). |
| 2 | Inspect a day with three employees on leave | All three blocks render (stacked/listed) on that date without overlapping into illegibility. |
| 3 | Locate today's date | Today's cell carries a subtle accent indicator (Section 8). |
| 4 | Span a multi-day leave across a month boundary edge | The block clips correctly to the visible range and continues if the next/prev month is navigated. |

## 6. Postconditions
- Month grid is color-coded per leave type with a legend; overlapping employees stack cleanly.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
