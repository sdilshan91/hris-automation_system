---
name: right-drawer-form-pattern
description: How to build a Notion-style right slide-in drawer that is full-screen on mobile, mirroring the leave-management modal pattern but sliding from the right
metadata:
  type: feedback
---

For "drawer slides in from the right, full-screen on mobile" requirements, the
working CSS structure is: a fixed `inset-0` wrap with `flex justify-end
pointer-events-none`, and a panel `pointer-events-auto ... w-full sm:max-w-md`.
The Angular `@drawer` animation goes `translateX(100%)` -> `translateX(0)` on
:enter and back on :leave. A separate `drawer-backdrop` (fixed inset-0) handles
the click-to-close. The panel itself is `flex flex-col h-full` with a scrollable
`flex-1 overflow-y-auto` body sandwiched between a sticky header and footer.

**Why:** US-ATT-003 §8 asked for a right drawer; the existing leave-management
modals (leave-application, my-leave-requests cancel dialog) are *centered* pops,
not right slide-ins, so there was no exact in-repo template to copy.

**How to apply:** reuse this skeleton for any "drawer from the right" story
(distinct from the centered `dialogPop`/`modalPop` confirmations). Keep the
backdrop element separate from the wrap so `pointer-events-none` on the wrap lets
clicks through to the backdrop while the panel stays interactive.
Pairs with [[signal-async-dom-detectchanges]] for testing the submit flow.
