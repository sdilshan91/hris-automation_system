---
id: TC-LV-230
user_story: US-LV-011
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-LV-230: LOP management screen is keyboard/screen-reader accessible; bulk actions navigable; red/orange LOP highlight has non-color cues (WCAG 2.1 AA; §8)

## 1. Test Objective
Verify the HR LOP management screen meets WCAG 2.1 AA (§8): the LOP list/filters and the complex bulk actions (multi-select employee picker + date range, compulsory-leave date picker + "Apply to all", override dropdown) are fully keyboard operable and screen-reader friendly, and the red/orange LOP highlight in leave history is NOT conveyed by color alone (a text/icon cue accompanies it). Contrast meets >= 4.5:1 text / 3:1 UI.

## 2. Related Requirements
- User Story: US-LV-011
- UI/UX Notes §8 (LOP section, filters, bulk assignment, override; red/orange highlight)
- WCAG 2.1 AA

## 3. Preconditions
- HR Officer "Asha" authenticated; the LOP management screen shows entries with the auto/HR-assigned/employee-requested filters; at least one LOP entry highlighted.
- Keyboard-only operation + a screen reader (NVDA/VoiceOver) available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Standard | WCAG 2.1 AA | contrast >= 4.5:1 / 3:1 |
| LOP highlight | red/orange | must have non-color cue |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Tab through the LOP list, filters, and the bulk multi-select employee picker + date range | All controls are reachable and operable by keyboard in a logical order; focus is visible; the multi-select announces selection state to the screen reader. |
| 2 | Operate the compulsory-leave date picker + "Apply to all" by keyboard | The date picker and the apply-to-all action are keyboard-operable; the screen reader announces names/states; confirmation is announced via `aria-live`. |
| 3 | Open the override dropdown (convert LOP to another type + reason) | Dropdown and reason field are labeled, keyboard-operable, and announced; required state announced. |
| 4 | Inspect a red/orange LOP-highlighted entry | The LOP status is also conveyed by a text label/badge or icon (e.g. "LOP" tag), not by the red/orange color alone; a colorblind user can distinguish it. |
| 5 | Verify contrast | LOP highlight text, filter chips, and bulk-action buttons meet >= 4.5:1 (text) / 3:1 (UI). |

## 6. Postconditions
- The LOP management screen and bulk actions are keyboard/screen-reader accessible and the LOP highlight carries a non-color cue (WCAG 2.1 AA).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test
