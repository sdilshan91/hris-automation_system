---
name: routerlink-breaks-sibling-spec
description: Adding RouterLink to a standalone component's imports breaks its existing spec with NG0201 No provider for ActivatedRoute — add provideRouter([]) to that spec
metadata:
  type: feedback
---

Adding `RouterLink` (or `routerLink` in a template) to an existing standalone
component's `imports` makes RouterLink request `ActivatedRoute` at construction.
Any pre-existing `.spec.ts` for that component that only set up `provideAnimationsAsync()`
+ service spies will start failing with `NG0201: No provider found for ActivatedRoute`.
Fix: add `provideRouter([])` (from `@angular/router`) to that spec's `providers`.

**Why:** US-REC-003 added a `[routerLink]` "Pipeline" link to the US-REC-001
`vacancy-list` component; all 13 of its existing specs went red even though the
isolated new specs were green. The failure only surfaces in the full `ng test` run,
not when running the new specs alone.

**How to apply:** whenever you add RouterLink/routerLink to a component that
already has a spec, update that spec in the same change (it's an in-scope sibling).
Run the FULL `ng test` suite, not just your new specs — a missing-provider
regression hides until the touched sibling spec runs. Pairs with
[[jasmine-optional-arg-spy]].
