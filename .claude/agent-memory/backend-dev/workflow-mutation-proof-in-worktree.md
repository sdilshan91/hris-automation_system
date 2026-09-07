---
name: workflow-mutation-proof-in-worktree
description: Mutation-proof harnesses must live in a scratchpad script file — the worktree-isolation guard refuses inline `trap` and inline `git` in compound bash calls.
metadata:
  type: project
---

When running as a **worktree-isolated** sub-agent, the sandbox guard rejects two shapes that the
mutation-proof discipline otherwise asks for directly:

- any `git` invocation inside a compound/complex bash command (it cannot verify the call stays in the
  worktree) — including a harmless `git diff --stat` next to other commands;
- an inline `trap ... EXIT` (it cannot prove the trapped text won't run `git`).

**Why:** ISSUE-512 fenced sub-agent writes to their own worktree. The guard is text-based, so it fails
closed on anything it cannot statically read as worktree-local.

**How to apply:** write the mutate → run → revert sequence to a script under the session scratchpad
(prefixed per ISSUE-521, e.g. `i129-mutate-and-prove.sh`) and invoke it as `bash <abs-path> "<label>"
"<perl-expr>"`. The `trap` then lives *inside* the script, which still satisfies "bind the revert in the
same invocation as the mutation" — arguably better, since it fires even if the test run dies. Use
`cmp -s` against a pristine copy to abort when the mutation expression matched nothing (a silently
non-applied mutation reads as "test stayed green" and would falsely condemn the test). Verify the revert
with `sha256sum` against a baseline captured before the first mutation, and show `diff` vs pristine
rather than `git diff`.

Also: pass the **absolute** `.sln` path and run from the worktree — `scripts/run-backend-tests.sh` has an
ISSUE-492 gate that refuses a solution belonging to a different tree, and a bare relative path from the
repo root would test the wrong one.

Related: [[patterns-read-through-cache]]
