---
name: worktree-disk-pressure
description: /mnt/d fills to 100% from accumulated worktree node_modules + dist; it surfaces as karma DISCONNECTED flakes and a corrupted node_modules, not as a disk error
metadata:
  type: project
---

`/mnt/d` is a **51 GB** volume and `.claude/worktrees/` accumulates one full `node_modules`
(~1-2 GB) plus a `dist/` per agent worktree. On 2026-09-07 there were **37 worktrees / 29 GB** and
the volume hit **100% full**.

**It does not announce itself as a disk error.** What you actually see:

- a full `ng test` run that dies partway with `beforeAll` 5000 ms timeouts in an unrelated suite
  (e.g. `MainLayoutComponent`) followed by `Disconnected, because no message in 30000 ms` — looks
  exactly like a pre-existing flaky suite;
- afterwards, a **damaged `node_modules`**: `@angular/cli` silently absent and `npx ng` failing
  with `npm error could not determine executable to run`;
- only `git` says it plainly: `fatal: ... index.lock write error. Out of diskspace`.

**Why it matters:** the flake reads as a red gate caused by *your* change. Re-running the same
suite alone passes, which is the tell.

**How to apply:** if a full Karma run DISCONNECTs or a suite times out in `beforeAll`, run
`df -h /mnt/d` before triaging the test. Recover with `rm -rf <your worktree>/src/frontend/dist`
and re-run `npm ci` (a fresh `npm ci` also reclaims a bloated partial tree). Never delete another
agent's worktree. Delete your own `dist/` before reporting. See
[[project-worktree-frontend-setup]].
