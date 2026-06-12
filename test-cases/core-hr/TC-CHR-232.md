---
id: TC-CHR-232
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-232: Responsive -- 360px viewport shows bottom sheet instead of modal for status change (NFR-4)

## 1. Test Objective
Verify that the status change UI is fully responsive. On a 360px viewport (mobile), the status change modal transforms into a bottom sheet. The timeline section renders as a compact card list. All form fields remain accessible and functional. This validates NFR-4 and the UI/UX notes.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "John Smith" (`emp-001-uuid`) exists with status `active`.
- Browser viewport is set to 360px width (mobile simulation).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport Width | 360px | Mobile breakpoint |
| Employee | John Smith (emp-001-uuid) | Status: active |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set the browser viewport to 360px width. Navigate to the employee profile. | Profile loads in mobile layout. Status badge is visible. "Change Status" button is accessible. |
| 2 | Click "Change Status". | A bottom sheet (not a centered modal) slides up from the bottom of the screen. It contains the New Status dropdown, Reason textarea, and Effective Date picker. |
| 3 | Verify all form fields are usable within the bottom sheet. | New Status dropdown opens correctly, Reason field can be typed into, Date picker works. No horizontal scrolling is required. |
| 4 | Fill in the form and submit. | The bottom sheet closes. Success toast appears. Status badge updates. |
| 5 | Scroll to the Employment History section. | Timeline entries display as a compact card list (not the full desktop vertical timeline with pseudo-elements). Each card shows the status badge, date, reason, and actor. |
| 6 | Verify badge color animation works on mobile. | Badge color transition animates smoothly (200ms ease) when status changes. |
| 7 | Test at 1920px viewport. | The status change form renders as a centered modal (not a bottom sheet). Timeline renders as a full vertical timeline. |

## 6. Postconditions
- Status change was completed successfully from the mobile viewport.
- UI correctly adapted between mobile (bottom sheet) and desktop (modal) layouts.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
