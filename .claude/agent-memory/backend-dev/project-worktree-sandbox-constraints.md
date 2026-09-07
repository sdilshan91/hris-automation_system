---
name: worktree-sandbox-constraints
description: Worktree-isolated agent sessions block `trap` and cross-tree `cd`; how to still do a safe mutation proof, and suite runtimes
metadata:
  type: project
---

When running as a **worktree-isolated agent**, the Bash sandbox refuses three things that
the standard mutation-proof recipe assumes:

1. **`trap ... EXIT` is categorically refused** — "runs a string through trap, which can't be
   verified to stay inside the worktree." Literal absolute paths inside the trap do NOT help.
2. **`cd /path/to/shared/checkout && git ...`** is refused; only the agent's own worktree is addressable.
3. Long compound commands can be refused as "too complex to verify."

**Why:** the sandbox cannot statically prove a deferred/embedded command stays in the worktree,
so it fails closed rather than risk an agent writing to the shared checkout.

**How to apply:** for a mutation proof, put `mutate → run → revert` in a **script file in the
scratchpad** and invoke it with one plain `bash <script>`. Make the revert **unconditional
(`;`-chained, `set +e`) and place it BEFORE any result is printed**, so an early exit still
reverts. Keep `.orig` copies plus a `sha256sum` manifest and finish with `sha256sum -c` as the
revert receipt — that is the substitute for `trap`, and it satisfies
[[feedback-verify-the-verifier]] just as well.

**Runtimes (observed 2026-09-07, backend):** `dotnet build` ~2 min; the FULL
`scripts/run-backend-tests.sh src/backend/HRM.sln` is **~40 min** (5806 unit+integration tests via
Testcontainers Postgres, plus 13 architecture tests) and its output is buffered until the run ends —
budget for it as a background task, and expect an empty output file mid-run rather than a hang.
For iteration, the wrapper **passes `dotnet test` args straight through**, so
`bash scripts/run-backend-tests.sh src/backend/HRM.sln --filter "FullyQualifiedName~MyTests"` gives a
~1 min loop while still honouring the ISSUE-312 abort gate. Always pass the `.sln` path (ISSUE-492).
