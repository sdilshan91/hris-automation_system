---
id: TC-LV-087
user_story: US-LV-004
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-087: Cross-browser compatibility for the pending queue and detail panel

## 1. Test Objective
Verify that the pending leave queue (table on desktop, card view on mobile), the filter bar, the slide-in detail panel, and quick-action buttons render and function correctly across Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" is active; a manager with `Leave.Approve.Team` is authenticated.
- The queue has representative rows including color pills, overdue highlights, and attachments.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | Latest stable |
| Viewports | 360px, 768px, 1440px | Mobile/tablet/desktop |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the queue in each browser at desktop width | Table view renders with all columns; color pills and overdue borders display correctly; no layout breakage. |
| 2 | Apply and clear filters in each browser | Filter chips and server-side filtering behave identically across browsers. |
| 3 | Open and close the detail panel in each browser | The slide-in panel animates and dismisses correctly; attachments are downloadable in all browsers. |
| 4 | Switch to 360px in each browser | The card view renders; quick-action buttons remain operable (swipe/tap). |
| 5 | Verify pagination controls | Pager works consistently across browsers. |

## 6. Postconditions
- No data mutated.
- Queue and detail panel are consistent and functional across all four browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
