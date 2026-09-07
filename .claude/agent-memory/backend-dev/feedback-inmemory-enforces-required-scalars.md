---
name: inmemory-enforces-required-scalars
description: EF Core InMemory throws DbUpdateException on a NULL required scalar, so removing a `?? string.Empty` coercion turns silent corruption into a loud failure — and reddens unrelated tests
metadata:
  type: feedback
---

EF Core's **InMemory provider enforces required scalar properties**: writing `null` into a non-nullable
mapped `string` fails `SaveChangesAsync` with `DbUpdateException: Required properties '{...}' are missing`,
it does **not** store null. Observed 2026-09-07 mutating `ReviewSignoffService.AppendSignoff`.

**Why:** it changes what a defensive `?? string.Empty` coercion is actually buying you. The coercion is not
"belt and braces" — it is the only reason a null-name bug persisted a **blank but valid** row instead of
throwing. Delete the coercion and the same defect becomes a 500 at `SaveChanges`. For an evidentiary log
(sign-off, audit) loud is the right answer; for a best-effort field it is not. Decide deliberately.

**How to apply:**
- Before deleting a `?? string.Empty` / `?? ""` on a mapped required column, check *every* caller can still
  supply non-null. Make the parameter non-nullable so the compiler carries the rule, and pin it with
  `new NullabilityInfoContext().Create(parameterInfo).WriteState == NullabilityState.NotNull` — that goes
  red if someone re-widens to `string?`, which string-matching the source would not (see
  [[guards-must-be-mutation-proven]]).
- Expect mutation runs to redden **more** arms than you targeted: a null reaching a required column aborts
  the whole `SaveChanges`, so every test that merely drove that workflow fails too, not just the arm
  asserting the value. Read the extra reds as "the save aborted", not as flakiness.
- `??` only catches null. A blank/whitespace value satisfies `IsRequired()` (NOT NULL) perfectly happily —
  if "unidentifiable" is the thing you are guarding, the test is `string.IsNullOrWhiteSpace`.
