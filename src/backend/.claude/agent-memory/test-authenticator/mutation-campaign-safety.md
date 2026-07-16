---
name: mutation-campaign-safety
description: Mutation campaigns must never leave mutants in the working tree — a concurrent session committed 8 of mine into HEAD (0cc05dea, CAL-8)
metadata:
  type: feedback
---

Never leave mutation-test mutants sitting in the working tree across a long build/test cycle, and
announce the mutation window before starting. Restore after EVERY batch, not at the end.

**Why:** On 2026-07-16 (CAL-8/ISSUE-305 re-audit) I applied 8 mutants to 5 production files, and while
the ~5-minute build+test cycle ran, the user's concurrent session ran `git add` + `git commit`. Commit
`0cc05dea` ("feat(leave,payroll): tenant fiscal leave year end-to-end") captured all 8 deliberate
defects (`MUTANT-M2/M5/M6/M13a-c/M15a-b`) as real production code. The user's own "818 tests green"
gate did not catch it — because 7 of the 8 mutants were exactly the zero-resistance sites the audit
was reporting, so the suite stayed green with the bugs in. This repo's standing warning "⚠ A CONCURRENT
SESSION IS LIVE — verify branch before every commit" cuts both ways: it also means *my* temporary
edits are commit-visible to someone else.

**How to apply:**
- Prefer mutating ONE site at a time, restore immediately after each run. A wide screening batch is
  fast but widens the window in which someone can commit the mutants.
- Always tag mutants with a greppable marker (`// MUTANT-Mn`) — that marker is what let me detect the
  poisoned commit via `git grep -n "MUTANT" <sha> -- '*.cs'`. Never mutate without a marker.
- Before reporting, always run `git grep --cached -n "MUTANT"` and `git grep -n "MUTANT" HEAD` — not
  just a working-tree grep. Working tree clean ≠ index/HEAD clean.
- Back up to the scratchpad before mutating, but do NOT blind-`cp` restore: the user may have edited
  the same file meanwhile and a whole-file restore silently reverts their work. Restore by surgical
  string-replace of the mutant only (this is how I avoided clobbering their 10:53 edits).
- Kill stale `testhost` processes before building (they lock `HRM.*.dll` and the build silently fails
  to copy → tests run against a STALE binary and every mutant falsely "survives"). Always prove the
  mutant is live: compare DLL mtime > mutated-source mtime.

Related: [[fake-test-patterns-cal8]]
