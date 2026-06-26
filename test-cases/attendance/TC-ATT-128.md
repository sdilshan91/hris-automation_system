---
id: TC-ATT-128
user_story: US-ATT-009
module: Attendance
priority: medium
type: accessibility
status: blocked
created: 2026-06-15
---

# TC-ATT-128: Accessibility & responsive -- lock button + confirm modal, locked-period banner, side-by-side reconciliation table (stacks on mobile), payroll stepper; WCAG 2.1 AA, 360px

## 1. Test Objective
Verify WCAG 2.1 AA accessibility and responsive behaviour for the US-ATT-009 UI surfaces (§8): the "Attendance Lock" action button + confirmation modal, the prominent locked-period banner, the side-by-side reconciliation table (which must stack vertically on mobile), and the payroll-process stepper (Lock -> Generate -> Review -> Finalize -> Publish) -- keyboard-navigable, screen-reader friendly, sufficient contrast, usable down to 360px.

## 2. Related Requirements
- User Story: US-ATT-009
- UI/UX: §8 (lock button + confirm modal, locked banner, side-by-side reconciliation table stacking on mobile, payroll stepper)
- Standard: WCAG 2.1 AA
- Cross-cutting a11y precedent: TC-ATT-116 (US-ATT-008), TC-ATT-099 (US-ATT-007)

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated; a period available to lock; a generated summary for the reconciliation view.
- Test with keyboard only, a screen reader (NVDA/VoiceOver), and an axe-core scan; viewports 360px and 1920px.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| viewport min | 360px | mobile responsive |
| viewport max | 1920px | desktop |
| components | lock button, confirm modal, locked banner, reconciliation table, payroll stepper | §8 surfaces |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Tab to the "Attendance Lock" button and activate via Enter/Space | Reachable in logical tab order, visible focus ring, has an accessible name; activates the confirm modal. |
| 2 | Confirm modal keyboard + SR behaviour | Focus moves into the modal and is trapped; Esc cancels; confirm/cancel are reachable; modal has role=dialog + aria-label; focus returns to the trigger on close. |
| 3 | Locked-period banner | Announced to screen readers (role=status / aria-live), text "Attendance locked for May 2026 payroll" has >= 4.5:1 contrast; not conveyed by colour alone. |
| 4 | Reconciliation side-by-side table at 1920px | Proper table semantics (th/scope, caption); mismatch-highlighted cells convey state by more than colour (icon/text), >= 4.5:1 contrast. |
| 5 | Reconciliation view at 360px | The two sides STACK vertically (attendance on top, payroll below per §8); no horizontal scroll/clipping; reading/tab order remains logical. |
| 6 | Payroll-process stepper | Current/complete/upcoming steps exposed to SR (aria-current), keyboard-navigable, state not colour-only. |
| 7 | axe-core automated scan on each surface | No critical/serious violations (labels, contrast, roles, names). |
| 8 | Cross-browser render (Chrome, Edge, Firefox, Safari) | Consistent layout/behaviour across browsers. |

## 6. Postconditions
- The payroll-integration UI surfaces meet WCAG 2.1 AA, are fully keyboard + screen-reader operable, and render correctly from 360px to 1920px across browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test

## 8. Notes
- The payroll stepper and the payroll-input column of the reconciliation table belong to PAYROLL-MODULE pages (§8 names the payroll preparation/run pages); the a11y of the attendance-owned surfaces (lock button/modal, locked banner, attendance side of reconciliation) is verified now. The stepper a11y is verified on the attendance-side rendering and re-checked when the Payroll UI lands. **Reported to caller.**
- Consistent with the one-a11y-TC-per-story precedent (TC-ATT-011/024/035/050/066/083/099/116).
