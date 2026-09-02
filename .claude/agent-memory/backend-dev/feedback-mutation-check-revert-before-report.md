---
name: feedback-mutation-check-revert-before-report
description: When mutation-checking a new test, revert the mutation BEFORE writing the report, and pair the mutation+revert in one bash call
metadata:
  type: feedback
---

When proving a new test actually kills a regression, back the source file up first, and make the
**revert unconditional and in the same tool call** as the mutation + test run. Revert **before**
writing the report, never after.

**Why:** an agent in this repo hit its turn ceiling mid-revert, and the leftover mutation in the
working tree was collected by the orchestrator as if it were the fix. Four agents in that same
session hit the 60-turn ceiling, so "I'll clean up at the end" is not a safe assumption. The caller
now asks for this explicitly on every mutation-check task.

**How to apply:** `cp <file> $SCRATCHPAD/<file>.ORIG` → mutate → build → run → `cp` back
**unconditionally** (use `;` not `&&` so a failed build still reverts) → verify with `md5sum` +
an empty `git diff --stat` on the mutated file, and quote that verification in the report. Report
the before/after test counts verbatim (e.g. "14 passed → 4 failed"), not a paraphrase.

Related: [[feedback-integration-tests-inmemory]] (never weaken a test to go green — the mutation
proves the test has teeth, so the test itself must stay untouched).
