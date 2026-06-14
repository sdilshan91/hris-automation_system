---
id: TC-ATT-066
user_story: US-ATT-005
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-066: Shift management UI -- inline-edit table, employee multi-select picker, rotation weekly view, and 360px card layout are accessible and responsive (WCAG 2.1 AA)

## 1. Test Objective
Verify the UI/UX Section 8 + accessibility expectations: the shift list renders as a Notion-style database table with click-to-edit inline cells; shift assignment uses a searchable multi-select employee picker (avatar + employee number); rotating shifts show a calendar-like weekly rotation view; a "Clone Shift" action is available; and on mobile (360px) the list collapses to a card layout with a full-screen assignment modal. The whole surface is keyboard-operable and screen-reader friendly and meets WCAG 2.1 AA.

## 2. Related Requirements
- User Story: US-ATT-005
- UI/UX Notes: Section 8 (database-style table, inline edit, multi-select employee picker, weekly rotation view, drag-and-drop rotation reorder, clone action, profile shift card, 360px card layout + full-screen assignment modal)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme", HR Officer authenticated with `Attendance.Shift.Manage`; several shifts incl. a ROTATING one and a populated employee directory.
- Tested on Chrome + a screen reader (NVDA/VoiceOver) and an automated axe scan; viewports 360/768/1280/1920px.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive range |
| Components | shift table, inline-edit cell, employee multi-select, rotation weekly view, clone button | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run an axe (WCAG 2.1 AA) scan on the shift list, assignment modal, and rotation view | No critical/serious violations; text/control contrast >= 4.5:1. |
| 2 | Operate the inline-edit table by keyboard only | Cells are reachable in a logical order; click-to-edit is also Enter/Space-activatable; the editable field is labeled, announces its state, and commits/cancels with keyboard; visible focus throughout. |
| 3 | Use the employee multi-select picker with keyboard + screen reader | The searchable combobox is labeled, exposes options with name + employee number, announces selection count and added/removed selections; type-ahead works; selections are removable by keyboard. |
| 4 | Navigate the rotation weekly view | The weekly pattern is conveyed as structured, labeled content (not color-only); each day's shift is announced; if drag-and-drop reorder exists, a keyboard-accessible alternative is provided. |
| 5 | Operate the Clone Shift action and the assignment flow | Clone is a labeled, keyboard-reachable control; the assignment modal traps focus and returns it on close. |
| 6 | Resize to 360px | The table collapses to a card layout; assignment uses a full-screen modal with employee search; all controls remain visible and operable without horizontal scroll; touch targets >= 44-48px. |
| 7 | Cross-browser smoke (Chrome, Edge, Firefox, Safari) | The table, picker, and rotation view render and operate consistently. |

## 6. Postconditions
- The shift management UI meets WCAG 2.1 AA, is keyboard/screen-reader operable, and is fully usable at 360px across browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
