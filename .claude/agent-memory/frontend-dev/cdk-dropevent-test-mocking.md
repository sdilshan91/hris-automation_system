---
name: cdk-dropevent-test-mocking
description: Mock a CdkDragDrop event in specs by casting a partial literal through `unknown`; same-column reorder needs the SAME container object for both previous/container refs
metadata:
  type: feedback
---

To unit-test a `(cdkDropListDropped)` handler without the real CDK harness, build a
partial event literal and cast `as unknown as CdkDragDrop<T>` — a direct
`as CdkDragDrop<T>` fails (TS2352: missing isPointerOverContainer/distance/dropPoint/event).
Provide `{ previousContainer:{data}, container:{data}, previousIndex, currentIndex,
item:{data} }`.

Crucially: the handler typically branches on
`event.previousContainer === event.container` to tell a same-column reorder from a
cross-column move. In a test, a same-column reorder must pass the **same object
reference** for both — two separate `{ data: col }` literals are `!==` and wrongly
hit the cross-column path.

**Why:** US-REC-003 Kanban board; the reorder-within-column test first failed
because I used two distinct container literals, so the component ran the
cross-column transfer (and asserted no server call wrongly).

**How to apply:** reuse a `dropEvent(from, to, card)` factory for cross-column
moves; for same-column reorder share one `const sameContainer = { data: col }`.
