---
name: frontend-test-run-setup
description: Running ng test/build in a fresh git worktree needs npm ci first and an explicit CHROME_BIN; karma's headless launcher is non-default.
metadata:
  type: project
---

A fresh `isolation: worktree` checkout has **no `src/frontend/node_modules`** — `npx ng` fails with
"could not determine executable to run" until `npm ci` runs (~1 min, 1145 packages). Karma also does
not find a browser on its own here; the working invocation is:

```
cd src/frontend
CHROME_BIN=/usr/bin/google-chrome npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox
```

**Why:** `npm ci` warns that esbuild's postinstall was skipped by the allow-scripts policy — ignore
it, `npm run build` still completes. The default `Chrome` launcher in `karma.conf.js` is headful and
the custom `ChromeHeadlessNoSandbox` launcher exists precisely because plain `ChromeHeadless` hangs
on this box; `CHROME_BIN` is unset in the agent environment even though `/usr/bin/google-chrome`
exists.

**How to apply:** budget ~1 min install + ~1 min build + ~1.5 min for the full suite (4350 specs as
of 2026-09-05) before reporting a verify gate. Scope a single spec with
`--include='**/name.spec.ts'` while iterating. Repo-wide `npm run lint` carries hundreds of
pre-existing errors — run `npx eslint <touched files>` instead; note `main-layout.component.ts` has
two pre-existing `click-events-have-key-events` / `interactive-supports-focus` errors on its mobile
overlay (ISSUE-389 debt) that are not yours.
