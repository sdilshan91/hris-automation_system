---
name: project-worktree-frontend-setup
description: Agent git worktrees ship without src/frontend/node_modules, and a symlink to the main checkout's copy shows up as an untracked file
metadata:
  type: project
---

Agent worktrees under `.claude/worktrees/` have **no `src/frontend/node_modules`** — only the
main checkout at `/mnt/d/WORK/hris-automation_system/src/frontend/node_modules` has one. Without
it `npx ng test` / `npm run build` fail with `npm error could not determine executable to run`,
which reads like a broken Angular install rather than a missing dependency tree.

**Why:** worktrees are cheap git checkouts; nobody runs `npm install` in them (it would cost
minutes per agent run for an identical dependency tree).

**How to apply:** symlink it rather than installing —
`ln -s <main>/src/frontend/node_modules <worktree>/src/frontend/node_modules`. Angular resolves
through it fine (verified: full 4340-spec Karma run + `npm run build` both green through the
symlink).

**Then delete the symlink before reporting.** The repo's ignore rules do NOT cover it — `git
status` reports `?? src/frontend/node_modules`, so leaving it behind means handing back a dirty
working tree, which the no-commit contract in [[feedback-no-git-in-pipeline]] makes a visible
failure.

Also: verify the worktree base first (ISSUE-442). These worktrees are routinely hundreds of
commits stale; `git merge --ff-only test/local-subdomains` is the documented recovery.
