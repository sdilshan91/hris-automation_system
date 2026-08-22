---
name: child-parent-circular-import
description: A child component that imports a value/helper from its parent component (which imports the child) creates a runtime circular import → "Cannot access X before initialization"
metadata:
  type: feedback
---

When a **parent** standalone component imports a **child** component for its
`imports: []`, the child must NOT import anything back from the parent's module —
even a plain exported const/function (e.g. a shared `GUID_PATTERN` /
`isEntraTenantId`). Under Karma/webpack this surfaces as a hard runtime error
`Uncaught ReferenceError: Cannot access '<ChildComponent>' before initialization`
(thrown in afterAll, executes 0 specs), not a TS compile error — so `ng build`
passes but `ng test` fails to even start.

**Why:** the two files form a circular ES-module dependency; the child evaluates
first (parent imports it), but the parent's exported symbols aren't initialized yet.

**How to apply:** break the cycle. Either (a) inline the tiny helper locally in the
child (cheapest for one regex/function), or (b) extract the shared helper into a
third leaf module both import. Hit this on US-AUTH-016 where the onboarding-wizard
child imported `GUID_PATTERN` from `sso-settings.component.ts` (the parent) —
fixed by defining the pattern locally in the wizard. Related:
[[routerlink-breaks-sibling-spec]].
