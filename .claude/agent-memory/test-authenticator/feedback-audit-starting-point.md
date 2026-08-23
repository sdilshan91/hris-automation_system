---
name: feedback-audit-starting-point
description: The user mutation-tests their own changes before asking for an audit; start from residual risk, never re-derive what they already verified
metadata:
  type: feedback
---

When this user requests a test-authenticity audit they usually hand over a list of mutations they have
**already run** ("removing each pairing reddens its arm", "the guard's first two versions were decoration,
both fixed") and say *do not re-derive, build on it*.

**Why:** they do the cheap verification themselves; paying an agent to repeat it produces a report whose
first half they already know, and buries the residual risk that is the only thing they wanted.

**How to apply:** confirm the stated baseline cheaply (one filtered run of the suite), then spend the budget
on the mutations they did **not** run — vacuity paths, aliasing, unasserted preconditions, and the specific
value each assertion fails to pin. Report residual risk first; state the confirmed baseline in one line.
Related: [[static-scan-guards-vacuity]].
