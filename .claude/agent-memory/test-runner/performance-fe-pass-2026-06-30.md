---
name: performance-fe-pass-2026-06-30
description: "Performance Mgmt FE per-TC pass (Playwright MCP) — 2 blocked→pass, ISSUE-210 module unreachable from sidebar, rest blocked no-data"
metadata:
  type: project
---

REPORT-ONLY **FE (UI-layer)** per-TC pass of **Performance Management** (`test-cases/performance/`), 2026-06-30, acme tenant `http://acme.myhrm.org:4200`, BE :5000 + FE :4200 both up. Targeted the 26 `blocked` Performance TCs (FE a11y/render). Result: **2 blocked→pass (TC-PRF-004-14, TC-PRF-008-13)**, 24 stay blocked (all annotated with per-TC `exec_note`). 1 NEW finding **ISSUE-210 HIGH**. Pass tally 116→118, blocked 26→24.

**Headline NEW = ISSUE-210 (HIGH): Performance module unreachable via sidebar for every acme persona.** The lone "Performance" nav item (`main-layout.component.ts:744-748`) is permission-gated to `Performance.View.Own` → tenantadmin/HR don't get the link; but it routes to `/performance`, role-guarded `['Manager','HR Officer','HR Manager','Tenant Admin']` (`app.routes.ts:442-449`) which EXCLUDES Employee → so the employee who DOES see the link clicks it and lands on `/forbidden`. And the employee self-service tree `/my-review` (my-goals @ `/my-review/my-goals`, self-assessment @ `/my-review` index — `my-review.routes.ts`) has **no** sidebar entry at all. Net: no working in-app entry point for the whole module. Same discoverability class as ISSUE-208 (attendance) but worse (visible link dead-ends in /forbidden).

**How I reached the screens (harness trick — reuse this):** no nav link, and hard `browser_navigate`/reload logs you out (BUG-097). Captured the Angular Router IN-PAGE and soft-navigated: `window.ng.getDirectives(a)` over `a[href]` links, find the directive prop whose value has `navigateByUrl` → `window.__router`, then `await window.__router.navigateByUrl('/performance/...')`. No reload, stays authed. Re-capture after each login (page context resets). `window.ng` is available (FE served in dev mode).

**Render states found (acme, no seeded perf data):**
- **Clean valid EMPTY-STATE → PASS:** `/performance/cycles` ("No cycles yet" + Create link) and `/performance/cycles/new` (fully-labeled form: name/type/rating-scale/dates, self-vs-mgr `role=slider`, 360+calibration checkboxes, 4 phase date-pairs, scope combobox) → **TC-PRF-004-14 pass**. `/performance/pips` ("No PIPs yet" + confidentiality `role=note`) and `/performance/pips/new` (h1+h2 Employee/Duration/Checkpoints sections) → **TC-PRF-008-13 pass**.
- **ERROR-STATE (data load 404, shell renders accessibly: h1 + ARIA `alert` + Retry, but data-bearing controls never mount) → stay BLOCKED no-data:** `/performance` team-goals ("Unable to load the active appraisal cycle"); `/my-review` self-assessment (`GET /performance/self-assessments/active` 404); `/my-review/my-goals` (`GET /performance/goal-progress/my-goals` 404); `/performance/team-reviews` ("Unable to load your team reviews"); `/performance/recommendations` (chrome+filters accessible but "Unable to load completed cycles"). employee@acme appears **not employee-linked** (the 404s) — recurring `no_employee_record` condition.
- **REDIRECT:** `/performance/dashboard` redirects tenantadmin → `/my-review` (server-side scope treats this persona as non-HR employee; ISSUE-205-class) → dashboard a11y (TC-PRF-007-12) unreachable by any acme persona here.

**Stayed blocked (24):** 8 data-dependent a11y (001-12/002-15/003-13/005-12/006-14/007-12/009-15/010-15) + 16 API/perf/security/integration/DB-layer out of FE scope (001-11, 002-06/10/11/14, 003-12, 004-13, 005-11, 007-11/13, 008-12, 010-06/11/14, ISO-028/040). Each got a one-line `exec_note` in its TC frontmatter.

**Did NOT chase the coordinator's "≥40 flips" target** — only 26 blocked TCs exist for the whole module and most are legitimately perf/security/API/no-data; fabricating passes would violate fail-closed. Flipped only the 2 I could honestly verify render+a11y. See [[testing-loop-report-only]].

To unblock the rest in a future run: seed an **active appraisal cycle + assigned goals + an employee-linked persona** for acme (then my-goals/self-assessment/team-goals/team-reviews/360/recommendations data controls mount and their a11y becomes testable), and fix ISSUE-210 so the module is navigable.
