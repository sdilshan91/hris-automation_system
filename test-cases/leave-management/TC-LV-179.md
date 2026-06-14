---
id: TC-LV-179
user_story: US-LV-009
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-179: Public holidays appear as background highlights on the calendar (FR-7, Test Hint; US-LV-007 integration)

## 1. Test Objective
Verify that tenant public holidays (from the US-LV-007 holiday calendar) appear as background highlights (light gray columns/rows) on the Team Leave Calendar, scoped to the viewer's location, and are visually distinct from leave blocks.

## 2. Related Requirements
- User Story: US-LV-009
- Functional Requirements: FR-7
- UI/UX Notes: Section 8 (holiday dates shown as light gray columns/rows)
- Dependency: US-LV-007 (holiday calendar data)
- Test Hint: "Verify public holidays appear as background highlights."

## 3. Preconditions
- Tenant "acme" has a public holiday on 2026-06-11 (active, public) configured via US-LV-007.
- Manager "Maya" viewing the month containing 2026-06-11.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Holiday | 2026-06-11, Public, active | background highlight |
| Holiday source | GET /api/v1/holidays?from&to (US-LV-007 FR-6) | location-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya loads the month view | 2026-06-11 is shaded as a holiday background (light gray column/cell), distinct from any leave block. |
| 2 | Confirm holiday is background, not a leave block | The holiday highlight does not appear as an employee leave bar and carries no employee name. |
| 3 | Switch to week (Gantt) view | The holiday day column is shaded across all employee rows on 2026-06-11. |
| 4 | View as a London-location employee where the holiday is New-York-only | The location-scoped holiday does not highlight for the London viewer (consistent with US-LV-007 location scoping, TC-LV-133). |

## 6. Postconditions
- Public holidays render as location-scoped background highlights, distinct from leave blocks, across views.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
