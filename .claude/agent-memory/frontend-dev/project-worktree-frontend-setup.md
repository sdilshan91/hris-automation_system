---
name: project-worktree-frontend-setup
description: Agent git worktrees ship without src/frontend/node_modules — run `npm ci` in the worktree; symlinking the main checkout's copy is FORBIDDEN (ISSUE-326)
metadata:
  type: project
---

Agent worktrees under `.claude/worktrees/` have **no `src/frontend/node_modules`** — only the
main checkout at `/mnt/d/WORK/hris-automation_system/src/frontend/node_modules` has one. Without
it `npx ng test` / `npm run build` fail with `npm error could not determine executable to run`,
which reads like a broken Angular install rather than a missing dependency tree (there is no global
Angular CLI; `ng` resolves only through `src/frontend/node_modules/.bin`).

**How to apply:** run `cd <worktree>/src/frontend && npm ci` (~1-2 min, ~1145 packages). Then
`CHROME_BIN=/usr/bin/google-chrome npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox`.
`node_modules/` is a real directory, so `.gitignore` matches it and the tree stays clean — nothing
to delete afterwards.

**⚠ CORRECTED 2026-09-07.** This note previously recommended **symlinking** the main checkout's
`node_modules`. That is now **explicitly forbidden** by `.claude/rules/frontend.md`, and the ban
predates the correction:
- `docs/DEV/INSTRUCTIONS.md` forbids reusing a `node_modules` populated by a **Windows** `npm install`
  on this shared NTFS drive. **ISSUE-326** is the incident — a win32 esbuild binary broke a
  host-Linux `ng test`. The symlink works only while the main tree happens to be Linux-populated,
  which is not a property you can check at a glance. (See [[esbuild-host-build-platform-mismatch]].)
- A symlink also **dirties the worktree**: `.gitignore`'s `node_modules/` has a trailing slash, which
  matches directories only, and git does not treat a symlink as a directory — so it shows as
  `?? src/frontend/node_modules` and can land in a commit ([[feedback-no-git-in-pipeline]]).

The old "verified green through the symlink" claim was real but is not evidence it is *safe*; it
only ever tested the lucky case. Do not reinstate it.

Also: verify the worktree base first (ISSUE-442). These worktrees are routinely hundreds of
commits stale; cut from `origin/test/local-subdomains`, and `git merge --ff-only
test/local-subdomains` is the documented recovery.
