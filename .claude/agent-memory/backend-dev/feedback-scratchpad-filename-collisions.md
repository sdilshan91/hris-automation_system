---
name: feedback-scratchpad-filename-collisions
description: The session scratchpad is shared across concurrent sessions — generic backup/checksum filenames get clobbered mid-run and break the mutation-proof revert check
metadata:
  type: feedback
---

Name every mutation-proof backup and checksum file after the **finding id** — `i128-svc.sha256`,
`i128-svc.bak` — never generic names like `svc.sha`, `svc.bak`, `orig.cs`, `pre.sha`.

**Why:** on 2026-09-07 during ISSUE-128 the pair `svc.sha` + `svc.bak` was written, the mutation ran
for ~5 minutes, and in that window a **concurrent session** wrote its own `svc.sha` (for
`PayrollAdjustmentService.cs`) to the same path. The closing `sha256sum -c` then reported
`PayrollAdjustmentService.cs: FAILED` — a false alarm that looks exactly like an unreverted mutation,
and worse, `cp svc.bak <file>` was one race away from restoring **another session's file** over the
one being fixed. A `ls` of the scratchpad shows dozens of these generic names from prior runs
(`svc.orig`, `c.orig`, `u.orig`, `pre.sha`, `t2.sha`), so the collision is routine, not exotic.

**How to apply:** prefix scratchpad artifacts with the issue/bug id in the same bash call that creates
them. When a `sha256sum -c` fails, **read the reported path first** — if it names a file you never
touched, it is a collision, not mutation residue; confirm the real state with `git diff` plus a grep
for the mutated construct before concluding anything. Related: [[feedback-mutation-check-revert-before-report]].
