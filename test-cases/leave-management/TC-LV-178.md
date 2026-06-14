---
id: TC-LV-178
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-178: Half-day leaves are visually differentiated from full-day leaves (BR-5, Test Hint)

## 1. Test Objective
Verify that half-day leaves render distinctly from full-day leaves on the calendar (a half-block or AM/PM indicator) so coverage can be read accurately, and that the half-day session is conveyed for managers.

## 2. Related Requirements
- User Story: US-LV-009
- Business Rules: BR-5
- Functional Requirements: FR-4 (totalDays)
- Test Hint: "Verify half-day leaves render differently from full-day leaves."

## 3. Preconditions
- Tenant "acme"; Manager "Maya"; direct report "Sam" has a half-day (PM) Annual leave on 2026-06-09 (totalDays=0.5).
- Direct report "Ravi" has a full-day Annual leave on 2026-06-09 (totalDays=1).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Sam | half-day PM, 2026-06-09, totalDays=0.5 | half-block / PM indicator |
| Ravi | full-day, 2026-06-09, totalDays=1 | full block |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya loads the month view for 2026-06-09 | Sam's block visibly renders as a half-block (or carries a "PM" / half indicator); Ravi's renders as a full block. |
| 2 | Hover Sam's block | Tooltip indicates half-day and the session (PM) and totalDays=0.5. |
| 3 | Switch to week (Gantt) view | The half-day bar is visually shorter / marked vs the full-day bar. |
| 4 | View the list view at 360px | The half-day entry is labelled (e.g. "Half day - PM") distinguishing it from full-day entries. |

## 6. Postconditions
- Half-day leaves are clearly distinguishable from full-day leaves across views.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
