---
name: project-worktree-frontend-setup
description: A fresh agent worktree has no src/frontend/node_modules — run npm ci in it; symlinking the main checkout's copy is forbidden
metadata:
  type: project
---

Agent worktrees under `.claude/worktrees/` have **no `src/frontend/node_modules`**. Without it
`npx ng build` / `npx ng test` fail with `npm error could not determine executable to run`, which
reads like a broken Angular install rather than a missing dependency tree (there is no global
Angular CLI on this box — `ng` resolves only through `src/frontend/node_modules/.bin`).

**Fix: run `npm ci` inside the worktree's `src/frontend`** (~1-3 min).

**Do NOT symlink the main checkout's `node_modules`.** This memory previously recommended the
symlink; [.claude/rules/frontend.md](../../rules/frontend.md) now forbids it, and the rule wins:

- the main checkout's tree can be populated by a **Windows `npm install`** on this shared NTFS
  drive, and a win32 esbuild binary then breaks a host-Linux `ng test` (`ISSUE-326`, and see
  [[esbuild-host-build-platform-mismatch]]);
- `.gitignore`'s `node_modules/` has a **trailing slash**, so it matches directories only — a
  symlink is not a directory and shows up as `?? src/frontend/node_modules`, dirtying the tree.

**Why:** correctness of the toolchain beats the ~2 minutes `npm ci` costs.

**How to apply:** first command in any new worktree, before any build/test. Delete `dist/` when
done — see [[worktree-disk-pressure]]. Also verify the worktree base (ISSUE-442): these are
routinely hundreds of commits stale; cut from `origin/test/local-subdomains`, not `main`.
