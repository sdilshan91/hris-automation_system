---
name: proving-spec-arm-fails
description: How to prove a new/corrected spec arm actually fails against unmodified code, and how to run ng test filtered to a few spec files
metadata:
  type: feedback
---

When fixing a bug whose spec encoded the broken behaviour, the user requires evidence that the
corrected spec arm **fails against unmodified code** — a before/after, not an assertion that it would.

**Why:** a green spec that asserts the bug is why the break shipped; "fixed the code" without a
detector that demonstrably fires leaves the next regression invisible. Fixing code without fixing the
spec is explicitly out of contract on these queue items.

**How to apply:**
1. Make both edits (source + spec), run the filtered suite, confirm green.
2. Back up the changed **source** files, then restore only those from git:
   `git show "HEAD:$f" > "$f"` (leaves the corrected specs in place — no `git checkout`, no stash,
   so git state is never mutated).
3. Re-run filtered; capture the failure lines. Restore the backups, `cmp -s` each file against the
   backup to prove the tree is byte-identical to the verified-green state, then re-run the FULL suite.

**Filtered runs** (whole suite is ~4.3k tests / minutes; a filtered run is ~1 min):
`npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox --include='**/foo.spec.ts' --include='**/bar.spec.ts'`

**Readable Karma output:** strip the ANSI/cursor churn or every progress line drowns the failures —
`... 2>&1 | sed 's/\x1b\[[0-9;]*m//g; s/\x1b\[1A\x1b\[2K//g' | grep -E "FAILED$|Expected|TOTAL"`.

**Lint delta, not lint total:** `npm run lint` has ~322 pre-existing errors (a11y debt). Prove you
added none by running `npx eslint <changed files>` on your version AND on the HEAD version of the
same files and comparing counts. See [[karma-headless-nosandbox-hang]].
