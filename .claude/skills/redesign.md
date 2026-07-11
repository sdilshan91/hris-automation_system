---
name: redesign
description: "Design-system + UX redesign loop for the HRM webview mobile app. Run 1 ships the design-token foundation + native mobile app-shell; runs 2..N redesign one module/screen each to the system. Director (@design-director) prescribes → @frontend-dev builds → /design-review verifies → one reviewable PR per run. Foundation-first, resumable via docs/vault/design/REDESIGN-STATUS.md."
user_invocable: true
---

# /redesign — design-system + UX redesign loop

The **design counterpart to `/implement-all`**. It drives the HRM UI toward a modern, fully
responsive, **app-like** design so it can ship inside a **webview as a mobile app**. It sequences
three existing agents into reviewable PRs:

**`@design-director` (prescribes) → `@frontend-dev` (builds) → `/design-review` (verifies).**

`@design-director` and `/design-review` are **report-only**; `@frontend-dev` is the **only** agent
that edits `src/frontend/`. This skill owns the commit/push/PR — the sub-agents do not.

## Usage

```
/redesign                 # do the next pending item in docs/vault/design/REDESIGN-STATUS.md
/redesign foundation      # force the foundation run (tokens + mobile app-shell + primitives)
/redesign {module}        # redesign a specific module/screen (e.g. /redesign employees)
```

Run continuously with `/loop /redesign` — it re-fires until `REDESIGN-STATUS.md` reports all done.
**Requires a clean working tree**; PRs are opened, not auto-merged, so review the stack after.

## Locked decisions (from the spec — do not re-litigate)

See [`docs/superpowers/specs/2026-07-11-design-director-redesign-design.md`](../../docs/superpowers/specs/2026-07-11-design-director-redesign-design.md).

- **Mobile model:** native-app shell (bottom tab bar, app-bar + back, bottom sheets, safe-area,
  ≥44px touch, no hover-only, `100dvh`); desktop keeps the sidebar. Two layouts, one codebase.
- **Aesthetic:** evolve the Notion-inspired language, formalized into tokens. No rebrand.
- **Tokens:** semantic CSS variables, **light + dark** via `prefers-color-scheme`. **No per-tenant
  theming** (structure for a future accent seam, build none now).
- **Rollout:** foundation first, then one module/screen per run.

## Prerequisites

1. **Clean working tree** on the base branch.
2. **App running** for the verify pass — frontend `http://localhost:4200` (`ng serve` in
   `src/frontend/`), backend per `src/frontend/src/environments/`. Pre-flight read-only; abort with a
   clear message if down.
3. **Browser MCP connected** (Playwright + Chrome DevTools) so `@browser-debugger` / `/design-review`
   can run. If absent, tell the user to reload and stop.
4. **A tenant + persona** to sign in as (default `acme`, QA personas `Admin@123!`). State the active
   tenant on every finding (Critical Rule #1).

## The loop (one item per invocation)

### Step 0 — pick the item
Read `docs/vault/design/REDESIGN-STATUS.md`. If the **foundation** row is `[ ]` (or the arg is
`foundation`), do the foundation run. Otherwise pick the first `[ ]` module (or the arg's module),
mark it `[~]`, and cut `design/{slug}` from a fresh base branch.

### Step 1 — director prescribes
Dispatch **`@design-director`**:
- **Foundation run:** author/refresh `docs/vault/design/{tokens.md, mobile-app-shell.md,
  ux-guidelines.md}` from an audit of the current rendered UI (it directs `@browser-debugger` for
  evidence).
- **Module run:** write `docs/vault/design/briefs/{screen}.md` — a build-ready brief (target layout
  desktop + mobile-shell, tokens to apply, component swaps, states, a11y, acceptance checklist).

The director commits **nothing** to `src/`; it only writes under `docs/vault/design/` (report-only).

### Step 2 — builder implements
Dispatch **`@frontend-dev`** with the spec/brief as its contract. It edits **only** `src/frontend/`:
- **Foundation:** implement the tokens as semantic CSS variables (light + dark), the native mobile
  app-shell (bottom tab bar, app-bar + back, bottom sheets, `env(safe-area-inset-*)`, `100dvh`,
  touch sizing, no hover-only), and the shared component primitives. Desktop sidebar unchanged.
- **Module:** apply the brief to that screen, reusing the shared primitives.

`@frontend-dev` does **not** commit or push (this skill owns git). It leaves a clean, buildable tree.

### Step 3 — verify gate
Run in order; any failure enters remediation:
1. `dotnet build HRM.sln` → `dotnet test` (from `src/backend` — unaffected, run for safety).
2. `npm run build` (from `src/frontend`).
3. `ng test` (headless).
4. **`/design-review`** on the changed routes (`--diff`) → capture Design Score + AI-Slop Score.

**Remediation loop** (mirrors `/implement-all`, max 3 attempts): hand the verbatim failure back to
the owning agent (`@frontend-dev` for build/test, `@design-director` if the brief itself was wrong)
and re-run the whole gate. **Never weaken/skip/delete a test to go green** (the `test-integrity-guard`
enforces this). If it can't go green in 3 attempts, revert the item to `[ ]` and stop **without** a PR.

**Design acceptance:** the change must **raise or hold** the Design Score and AI-Slop Score with
**zero** WCAG-AA / contrast regressions vs the baseline in `docs/Design/design-reports/`. A score
regression is a gate failure, not a merge-anyway.

### Step 4 — commit, push, PR
On green:
- Foundation: `feat(design): design-system foundation + mobile app-shell`
- Module: `feat(design/{module}): redesign {screen} to design system`

Push, open a PR (reference the spec + the brief + the design-score delta), then flip the
`REDESIGN-STATUS.md` row `[~]` → `[x]`. One PR per run.

## Guardrails

- **Report-only agents stay report-only.** `@design-director` and `/design-review` never edit `src/`
  or open PRs; only `@frontend-dev` edits code and only this skill commits.
- **Foundation is load-bearing.** It touches shared layout every feature depends on — call it out in
  the PR and get it reviewed before the loop stacks module PRs on top.
- **Out-of-lane discoveries** from any sub-agent are healed via [`/auto-heal`](auto-heal.md): file to
  `docs/QA/TEST-FINDINGS.md`, fold into the completion plan, re-prioritize. Design findings that are
  really functional bugs go to the normal fix cycle, not this loop.
- **Multi-tenant:** state the active tenant for every finding; never print full JWTs/passwords.

## Relationship to other skills

- **`/design-review`** = *grades* the rendered UI (the verify step here). **`/redesign`** = *drives*
  the UI to the target and ships it.
- **`/debug-ui`** = does it *work* (console/network/DOM) — a different axis; run it if a redesign
  surfaces a functional defect.
- **`/implement-all`** = builds *features* from stories; **`/redesign`** = elevates the *design/UX*
  layer. New feature screens built by `/implement-all` should already reference
  `docs/vault/design/`.
