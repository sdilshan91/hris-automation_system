---
id: TC-ATT-050
user_story: US-ATT-004
module: Attendance
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-ATT-050: Approval queue table/cards, inline approve/reject comment area, and bulk-selection controls are accessible and responsive (WCAG 2.1 AA, 360px)

## 1. Test Objective
Verify the UI/UX Section 8 + accessibility expectations: the approval queue renders as an accessible Notion-style table/list with expandable rows, inline Approve/Reject controls with a slide-down comment area, bulk-selection checkboxes plus a Bulk Approve toolbar action, and status pills that convey state by text (not color alone). The whole surface is keyboard operable and screen-reader friendly, and is fully usable at a 360px viewport (card layout).

## 2. Related Requirements
- User Story: US-ATT-004
- UI/UX Notes: Section 8 (table columns, expandable rows, inline approve/reject with slide-down comment, bulk checkboxes + Bulk Approve, status pills, pending badge, 360px card layout)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`; several PENDING requests in the queue.
- Tested on Chrome + a screen reader (NVDA/VoiceOver) and an automated axe scan.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive range |
| Status pills | amber Pending / green Approved / red Rejected | must not rely on color alone |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run an axe (WCAG 2.1 AA) scan on the approval queue | No critical/serious violations; contrast >= 4.5:1 for text and pill labels. |
| 2 | Tab through the queue with the keyboard only | Each row's expand toggle, the selection checkbox, and the inline Approve/Reject buttons are reachable and operable in a logical order with a visible focus indicator. |
| 3 | Open the inline comment area (slide-down) via keyboard | The reason/comment textarea receives focus, is labeled, announces the required state and (for reject) the 10-char minimum; focus returns sensibly on submit/cancel. |
| 4 | Verify status pills and badge with a screen reader | Pending/Approved/Rejected are announced as text (not by color only); the pending-approval badge count is announced. |
| 5 | Resize to 360px | The table collapses to a card layout; bulk checkboxes, Approve/Reject, and the comment area remain fully visible and operable without horizontal scroll; touch targets >= 44-48px. |
| 6 | Bulk selection by keyboard | Select-all and per-row checkboxes are keyboard-togglable; the Bulk Approve toolbar action is reachable and announces how many items are selected. |

## 6. Postconditions
- The approval queue and its actions meet WCAG 2.1 AA, are keyboard/screen-reader operable, and remain usable at 360px.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
