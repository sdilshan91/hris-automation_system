---
name: karma-headless-nosandbox-hang
description: ng test --browsers=ChromeHeadless hangs (connects, 0 output, 30s disconnect) on this Windows box; use ChromeHeadlessNoSandbox custom launcher
metadata:
  type: project
---

`npx ng test --watch=false --browsers=ChromeHeadless` HANGS on this environment:
Chrome connects, prints zero test results, then "Disconnected, because no message
in 30000 ms". The bundle builds fine — it's a launcher/sandbox issue, not a code
or compilation failure. Affects ALL specs (verified against sibling tenants spec).

**Why:** default `ChromeHeadless` on this Windows host needs `--no-sandbox` /
`--disable-gpu` / `--disable-dev-shm-usage` / `--headless=new` to actually run.

**How to apply:** a `src/frontend/karma.conf.js` defines a
`ChromeHeadlessNoSandbox` custom launcher and `angular.json`'s test target points
at it via `karmaConfig`. Run the suite with
`npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox` — it executes
reliably (3475 specs in ~well under a minute). Plain `--browsers=ChromeHeadless`
still hangs; prefer the NoSandbox launcher for any headless verify gate.

**Caveat (seen US-NTF-001):** `karma.conf.js` was ABSENT on a fresh branch off
`main` and `angular.json` had no `karmaConfig` — the wiring is apparently not
committed on `main` yet (or got reverted). If headless hangs, re-create
`karma.conf.js` (plugins: jasmine/chrome-launcher/jasmine-html-reporter/coverage
+ `@angular-devkit/build-angular/plugins/karma`; customLaunchers
`ChromeHeadlessNoSandbox` base `ChromeHeadless` with the four flags above) and add
`"karmaConfig": "karma.conf.js"` to the karma builder options before assuming a
real failure.
