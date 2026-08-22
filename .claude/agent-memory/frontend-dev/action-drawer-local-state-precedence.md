---
name: action-drawer-local-state-precedence
description: a slide-over that both previews an input entity AND mutates it (send/respond/withdraw) must compute currentEntity = localSignal ?? input(), not input() ?? local
metadata:
  type: feedback
---

When a slide-over takes an entity via `input()` AND performs lifecycle actions on
it (e.g. offer Send/Accept/Decline/Withdraw, US-REC-007), the action result must be
held in a local `signal` and the template/footer must read a computed
`currentEntity = localSignal() ?? input()` — **local takes priority over the
immutable input**.

**Why:** `input()` is set by the parent and does not change when the child mutates;
if `currentEntity = input() ?? local`, the input keeps winning and the badge/footer
never reflect the new status. The US-REC-007 offer-form spec failed exactly this way
("Expected 'Sent' to be 'Accepted'") until precedence was flipped. After the action,
also point the header badge, preview body, and footer `@if` at `currentEntity()`,
not the raw `input()`.

**How to apply:** any create-or-act drawer where the entity can change in-place
within the same open session (offers, future approval/sign-off drawers). The parent
still gets the truth via an output event and a list reload; the drawer just needs to
self-update its own view. Related: [[right-drawer-form-pattern]].
