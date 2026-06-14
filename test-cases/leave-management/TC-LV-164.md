---
id: TC-LV-164
user_story: US-LV-008
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-LV-164: Preview report -- responsive 360px+, keyboard/screen-reader navigable, color not the sole indicator (NFR, Section 8)

## 1. Test Objective
Verify the carry-forward preview UI meets WCAG 2.1 AA: the projection table is readable and usable from 360px upward, is fully keyboard- and screen-reader-operable, and carry-forward (blue) vs expired/forfeited (gray strikethrough) are distinguished by text/label in addition to color -- so color is never the sole indicator (Section 8).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5
- UI/UX Notes: Section 8 (carry-forward = blue, expired/forfeited = gray strikethrough)
- Standard: WCAG 2.1 AA

## 3. Preconditions
- HR Officer "Priya" with leave-config permission; a populated preview for tenant "acme" containing both carry-forward and forfeiture rows.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewports | 360px, 768px, 1280px, 1920px | responsive |
| Carry-forward encoding | blue + text label | not color-only |
| Expired encoding | gray strikethrough + text label | not color-only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the preview report at 360px | The projection table reflows to a readable layout (e.g. horizontally scrollable or stacked cards); no overlap/clipping; values readable. |
| 2 | Navigate by keyboard only (Tab/arrows/Enter) | Filters (department/employee/leave type), rows, and any preview controls are reachable and operable with a visible focus ring. |
| 3 | Verify carry-forward vs expired are not color-only | Carry-forward rows show a text label/icon in addition to blue; expired/forfeited rows show strikethrough plus an "Expired/Forfeited" text label; text contrast >= 4.5:1. |
| 4 | Run an axe audit + screen-reader pass | No critical violations; carry-forward and forfeiture amounts and statuses are announced meaningfully. |

## 6. Postconditions
- The preview report is responsive, keyboard/SR-accessible, and conveys carry-forward/expiry status beyond color.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
