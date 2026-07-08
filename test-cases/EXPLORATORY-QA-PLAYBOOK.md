# Exploratory QA Playbook (bug-hunt / dogfood)

> Methodology adapted (Apache-2.0) from the **`dogfood`** skill in
> [vercel-labs/agent-browser](https://github.com/vercel-labs/agent-browser), retargeted to this
> repo's stack: our **Playwright MCP + Chrome DevTools MCP** browsers (not the agent-browser CLI),
> our **`TEST-FINDINGS.md`** ledger + finding schema (not its report template), and our
> **multi-tenant personas**. Used by `@test-runner` and `@browser-debugger` for the *exploratory*
> portion of their work.

## What this is (and is not)

This is the discipline for **unscripted, user-like exploration** of the running HRM UI to surface
bugs / UX issues / a11y gaps — the "poke it like a real user and see what breaks" mode, as opposed
to executing a specific bound test case step-by-step. Use it when:

- a TC says "UI-layer per the steps" with no bound automated spec, or
- you're doing a free bug-hunt / smoke pass on a screen or a just-changed feature, or
- `/design-review` or `/debug-ui` turns up a functional symptom worth chasing.

It does **not** replace bound-TC execution under `/test-all`, and it does **not** change the
**REPORT-ONLY** contract: findings go to `test-cases/TEST-FINDINGS.md`; you never edit `src/`, never
weaken a test, never open a PR. Fixing is the human-decided step.

## Core rules (the part worth internalizing)

1. **Repro is everything — but match the evidence to the issue.**
   - **Interactive / behavioral bug** (functional, UX, an error only thrown *on action*): capture a
     **step sequence** — a screenshot of the *before* state, of the *action*, and of the *broken
     after* state — so a reader can replay it without a browser. If video tools are available in the
     session, a short repro clip is a bonus; otherwise the before/action/after screenshots are the
     baseline requirement.
   - **Static / visible-on-load bug** (typo, placeholder text left in, clipped/overflowing text,
     misalignment, a console error present *on load*): a **single annotated screenshot** is enough.
     No step sequence. Don't over-evidence a typo.
2. **Verify reproducibility with at least one retry *before* collecting evidence.** If it doesn't
   reproduce consistently, it's not a valid finding (yet) — note it as flaky/needs-repro, don't log
   it as a confirmed bug. (Ties into `/fault-diagnosis`: reproduce reliably before hypothesizing.)
3. **Explore and document in one pass — append immediately, never batch.** The moment you find an
   issue, stop and write it to `TEST-FINDINGS.md` before moving on. If the session dies, nothing is
   lost. (This matches the existing test-runner rule.)
4. **Black-box the *discovery*.** Find issues as a user would — don't read the app's Angular/C#
   source to decide *what to test*. (This does **not** override `/fault-diagnosis`: once you have a
   symptom, you still read the **Serilog log** `src/backend/HRM.Api/Logs/hrm-<YYYYMMDD>.log` by
   `RequestId` to root-cause it. Reading *runtime evidence* ≠ white-boxing the exploration.)
5. **Check the console every page.** Many defects are invisible in the UI but show as JS exceptions
   or failed 4xx/5xx requests — pull `browser_console_messages` + `browser_network_requests`.
6. **Test like a user, not a robot.** Run realistic end-to-end workflows (create → edit → delete),
   click what a real user would click, enter realistic data, use `browser_type` (character-by-
   character) for inputs where timing matters.
7. **Depth over breadth: aim for 5-10 well-documented findings, not 20 vague ones.** If you find a
   cluster in one area, dig there.

## Per-page exploration checklist (8 passes)

Run these on each page/feature in scope. Snapshot (`browser_snapshot`) to get element refs, then:

1. **Visual scan** — annotated screenshot. Layout, alignment, spacing, clipped text, broken
   icons/images, dark/light rendering, contrast.
2. **Interactive elements** — click every button/link/control. Does it do something? Is there
   feedback (loading indicator, toast)?
3. **Forms** — fill + submit. Test empty submit, invalid input, boundary values. Does validation
   reject valid input or accept invalid?
4. **Navigation** — follow every path; check breadcrumbs, back button, deep links, orphaned routes,
   dead ends (no way back/forward).
5. **States** — empty, loading, error, and full/overflow states. (Empty state = message + action +
   visual, not "No items.")
6. **Console** — JS exceptions, failed requests (4xx/5xx), CORS, unhandled promise rejections,
   deprecation warnings.
7. **Responsiveness** — where relevant, re-check at 375 / 768 / 1024 / 1440 (Chrome DevTools
   `emulate`); no horizontal scroll, touch targets ≥44px.
8. **Auth & tenant boundaries** — the HRM-critical one. Test: not-logged-in, wrong role/persona,
   and **cross-tenant** access. Confirm tenant isolation (Critical Rule #1): a JWT for tenant A on
   tenant B's subdomain must be denied (BUG-003 class — expect `403 cross_tenant_denied`), and data
   never bleeds across tenants. Always state which tenant/persona was active for every finding.

## Issue taxonomy (calibrate what to look for → map to our ledger)

Severities map straight to our `TEST-FINDINGS.md` severities:

| Severity | Definition |
|----------|------------|
| **critical** | Blocks a core workflow, causes data loss, crashes the app, or leaks across tenants |
| **high** | Major feature broken/unusable, no workaround |
| **medium** | Works but with noticeable problems; a workaround exists |
| **low** | Minor cosmetic / polish |

Categories to hunt (each becomes a **BUG** or, if it's a gap/enhancement, an **ISSUE/ENH** in the
ledger):

- **Visual / UI** — misalignment, clipped/overlapping text, inconsistent spacing, broken
  icons/images, dark-mode glitches, responsive breaks, z-index, contrast.
- **Functional** — broken links (404/wrong dest), controls that do nothing, validation wrong,
  bad redirects, silent failures, state not persisted on refresh/nav, double-submit/race, broken
  search/filter/pagination, upload/download failures.
- **UX** — confusing nav, missing loading/feedback, perceived delay >300ms, unclear errors, no
  confirm on destructive actions, dead ends, inconsistent patterns, bad defaults, poor empty states.
- **Content** — typos, outdated/incorrect text, lorem/placeholder left in, truncation with no
  tooltip, wrong/missing labels, inconsistent terminology.
- **Performance** — page loads >3s, janky scroll/animation, large layout shifts, excessive requests,
  slowdown over time, unoptimized images. (Cross-check with Chrome DevTools `lighthouse_audit` /
  `performance_*` — that's where the numbers live.)
- **Console / Errors** — JS exceptions, failed 4xx/5xx, deprecation, CORS, mixed content, unhandled
  rejections.
- **Accessibility** — missing alt text, unlabeled inputs, poor keyboard nav, focus traps,
  insufficient contrast, missing ARIA on dynamic content. (Pair with `@axe-core` via Playwright MCP
  and the Lighthouse a11y signal — see also `/design-review`.)

## Logging a finding

Append to `test-cases/TEST-FINDINGS.md` using the **existing finding schema** — do **not** invent a
new format: **type** (BUG/ISSUE/ENH), **severity**, **status** (`OPEN`), **layer** (UI/API/DB),
module / US / TC, **root cause + confidence**, **reproduction steps** (numbered, each referencing
its screenshot path), **active tenant + persona**, and **evidence** (screenshot/console/network
paths under `.playwright-artifacts/`). Flip the TC and `TEST-STATUS.md` per the normal `/test-all`
rules. Never print full JWTs/passwords — presence + claims only.

## Relationship to our other skills/agents

- **`/design-review`** = is it *designed* (visual-taste grading). **This playbook** = does it *work*
  when you poke it (functional/UX bug-hunt). **`/debug-ui`** = deep single-symptom diagnosis. Same
  browsers, different intent.
- Root-causing a found symptom → **`/fault-diagnosis`** (read the Serilog log by `RequestId`).
- This is the exploratory complement to `@qa-engineer`'s scripted IEEE-829 TCs — findings from here
  can become new regression TCs the human commissions later.
