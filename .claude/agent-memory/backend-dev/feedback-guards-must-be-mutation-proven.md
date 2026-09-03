---
name: feedback-guards-must-be-mutation-proven
description: Any new guard test must be mutation-proven (break the thing, watch it go red, revert with checksum) and prefer deriving from the source artifact over string-matching it
metadata:
  type: feedback
---

When adding a guard/regression test, **prove it bites**: mutate the thing it guards, show the
test goes red, then revert and **confirm the revert with a checksum** before reporting. Report
the mutation output. A guard that was never observed failing is not known to be a guard.

**Why:** the user's standing position is that *"a bad guard is worse than a documented gap"* —
a green test that cannot fail creates false confidence, which is worse than an honestly
documented hole. This repo has a track record of exactly this failure (see the G13 commit,
"make a self-certifying tautology into a real guard — and prove it with two mutations", and the
`@test-authenticator` agent that exists solely to hunt "test theater").

**How to apply:**
- Prefer **deriving** from the source artifact over **string-matching** it. When a test fixture
  hand-copies something that ships elsewhere (an ops SQL script, a config, a contract), the copy
  is blind to edits of the original. Extracting the real statements from the real file and
  executing them turns "asserts a string exists" into a causal binding. String-match guards are
  the weaker fallback, acceptable only when execution genuinely isn't possible.
- When you add a fail-closed sanity check (e.g. "parse found nothing"), make its failure message
  say what to do — and explicitly forbid re-hardcoding the mirror it replaced.
- The user will ask **"is this claim true in *every* environment?"** Prefer wording anchored to
  facts checkable in the repo (a committed connection string names role X) over environment-
  dependent ones (role X is a superuser). Lead with the indisputable half.
- If you judge a proposed guard to be theatre, the user explicitly wants you to **say so and not
  write it**, with reasoning. Declining is an accepted answer.

Related: [[user-wants-precision-not-pessimism-in-doc-claims]]
