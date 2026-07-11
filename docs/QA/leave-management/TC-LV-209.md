---
id: TC-LV-209
user_story: US-LV-010
module: Leave Management
priority: high
type: accessibility
status: fail
created: 2026-06-14
exec_note: "FAIL 2026-07-01 — /leave/my-requests reached via inject-axe + soft-nav as employee@acme.test; opened the cancel-confirmation dialog (clicked row Cancel → clicked 'Keep request' to close, NO DB write). GOOD: it IS a role=dialog + aria-modal=true with an accessible name (aria-labelledby='cancel-dialog-title' → 'Cancel leave request?'), and the reason textarea #cancel-reason is properly labeled. DEFECTS: (1) BUG-109 class — hand-rolled <div role=dialog> (my-leave-requests.component.ts:187, NOT a CDK MatDialog: no cdkTrapFocus / no (keydown.escape) / no cdkFocusInitial): focus is NOT moved into the dialog on open (activeElement=BODY) and Escape does NOT close it; (2) BUG-096 class — axe serious color-contrast on the '(optional)' hint text, color #a3a3a3 (rgb 163,163,163) 14px on white ≈2.6:1 (<4.5:1 AA). NOTE: this Pending request makes reason optional (aria-required=false) — the TC's mandatory-reason/announced-error arm (approved request, BR-5) was NOT exercised this pass (no approved future request was cancelled to avoid a write); structural reason-field labeling verified."

# TC-LV-209: Cancel confirmation dialog -- keyboard/screen-reader accessible, mandatory-reason field labeled with announced errors, usable at 360px+ (WCAG 2.1 AA; Section 8)

## 1. Test Objective
Verify the cancellation confirmation dialog meets WCAG 2.1 AA: it is fully keyboard operable (focus trapped within the modal, Escape closes, focus returns to the trigger), screen-reader friendly (`role="dialog"`, `aria-modal`, labeled title), the mandatory cancellation-reason field has an associated label and its required/error state is announced, and the dialog remains usable from 360px upward with full-width touch-friendly action buttons (Section 8, NFR -- accessibility).

## 2. Related Requirements
- User Story: US-LV-010
- UI/UX Notes: Section 8 (confirmation dialog with reason field; mobile full-width)
- Business Rules: BR-5 (reason mandatory for approved)

## 3. Preconditions
- Employee "Jane Smith" authenticated; an APPROVED future request R (reason mandatory) is open in "My Leaves".
- Keyboard-only operation and a screen reader (NVDA/VoiceOver) available; viewport tested at 360px and up.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Standard | WCAG 2.1 AA | contrast >= 4.5:1 text / 3:1 UI |
| Reason field | labeled, required | error announced when empty |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the cancel dialog with the keyboard (Enter/Space on "Cancel") | Dialog opens with `role="dialog"` + `aria-modal="true"`; focus moves into the dialog; focus is trapped (Tab cycles within); Escape closes it and returns focus to the trigger. |
| 2 | Tab to the reason field | The textarea has an associated `<label>` (programmatic name); it is marked required (`aria-required`); a screen reader announces its name and required state. |
| 3 | Attempt to confirm with an empty reason (approved request) | The "Confirm cancellation" button is disabled OR an inline error appears that is announced via `aria-live`/`aria-describedby`; the field is marked `aria-invalid` and not by color alone. |
| 4 | Enter a reason and confirm | The action completes; a success toast ("Leave request cancelled successfully.") is announced; the request card shows the Cancelled badge (text, not color alone). |
| 5 | Resize to 360px | The dialog is full-width with clearly separated, >=44px-tall action buttons; no content clipping or horizontal-scroll trap. |
| 6 | Verify contrast | Dialog text, the required-field error text, and the action buttons meet >= 4.5:1 (text) / 3:1 (UI) contrast. |

## 6. Postconditions
- The cancel dialog is keyboard- and screen-reader-accessible, the mandatory reason field is labeled with announced errors, and it is usable from 360px up (WCAG 2.1 AA).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
