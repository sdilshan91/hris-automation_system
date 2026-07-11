# Design: `@design-director` agent + `/redesign` skill

**Date:** 2026-07-11
**Status:** Approved (brainstorming) → implementing
**Author:** design brainstorming session

## Problem

The HRM SaaS web UI will be embedded in a **webview and shipped as a mobile app**. That
requires a **modern, fully responsive, app-like** design — not just a website that reflows.
Today the repo has:

- `@frontend-dev` — *builds* Angular features from stories. Its design guidance is a thin
  embedded "Notion-inspired" section; it is story-driven and **not** webview/mobile-app aware.
- `/design-review` + `@browser-debugger` — **report-only** designer's-eye audit that *grades*
  the rendered UI (AI-slop, hierarchy, WCAG, goodwill) but never prescribes a target or edits.

So there is a *builder* and a *critic*, but no **design-system + UX authority** that establishes
a cohesive, mobile-webview-ready look across the whole app and drives the app to it.

## Decisions (locked during brainstorming)

1. **Role — skill-driven workflow (director → builder).** The "new agent" is a report-only
   **design-director persona** plus a `/redesign` orchestration skill. `@frontend-dev` remains the
   **only** agent that edits `src/frontend/`. No second code-editing agent is created.
2. **Mobile model — native-app shell.** Below a breakpoint, swap the desktop sidebar for a
   native-feeling mobile shell: bottom tab bar, top app-bar with contextual back, bottom sheets,
   safe-area insets, touch gestures. Two coordinated layouts from one responsive codebase.
3. **Aesthetic — evolve the current Notion-inspired language.** Keep clean/minimal/neutral;
   formalize it into real design tokens and elevate it (type scale, dark mode, mobile polish).
   No jarring rebrand.
4. **Rollout — foundation first, then screen-by-screen loop.** Run 1 ships the design system +
   mobile app-shell + shared primitives (one big, carefully-reviewed PR). Runs 2..N each redesign
   one module/screen to the new system (one reviewable PR per run), mirroring `/implement-all`.
5. **Token scope — dark mode yes, per-tenant theming no.** Tokens are semantic CSS variables with
   light + dark themes driven by `prefers-color-scheme` (the device theme inside the webview).
   Per-tenant theming is **out of scope**; tokens are structured so a tenant accent *could* be
   layered later, but no seam or UI is built now.

## Architecture

Three artifacts, plus two small edits to existing files. No new pipeline machinery beyond them.

### 1. `@design-director` agent — `.claude/agents/review/design-director.md`

- **Persona:** senior product designer + design-systems engineer, mobile-app-first.
- **Report-only on code.** Tools: `Read, Glob, Grep, Bash, Write, Edit`, with Write/Edit scoped to
  `docs/vault/design/` and `docs/Design/design-reports/` **only** — never `src/`. Same report-only
  boundary as `@browser-debugger` / `@principal-advisor`.
- **No browser tools of its own** — it *directs* `@browser-debugger` for rendered evidence
  (screenshots, `browser_evaluate` design-system extraction, `lighthouse_audit`), the same pattern
  `/design-review` already uses.
- **Outputs:**
  - the **design-system spec** (`docs/vault/design/tokens.md`, `mobile-app-shell.md`,
    `ux-guidelines.md`),
  - structured **per-screen redesign briefs** (`docs/vault/design/briefs/{screen}.md`) — target
    layout, tokens to apply, mobile-shell behavior, component swaps, and an acceptance checklist
    `@frontend-dev` can implement without re-deciding design,
  - an audit report to `docs/Design/design-reports/` when it runs a full pass.
- **Boundary vs `/design-review`:** design-review *grades*; design-director *prescribes*.
  design-review becomes the **verify** step of the loop.

### 2. `/redesign` skill — `.claude/skills/redesign.md`

The director → builder → verify orchestration loop.

**Run 1 — Foundation PR:**
1. Director audits the current rendered UI (via `@browser-debugger`) and authors the design system:
   semantic CSS-variable **tokens** (color/space/type/radius/elevation), **light + dark**
   (`prefers-color-scheme`), evolving the Notion-inspired language.
2. `@frontend-dev` implements: the token layer, the **native mobile app-shell** (bottom tab bar,
   app-bar + back, bottom sheets, `env(safe-area-inset-*)`, `100dvh`, no hover-only affordances,
   ≥44px targets), and shared component primitives — desktop keeps the sidebar layout.
3. Verify gate: `dotnet build`/`dotnet test` (unaffected but run for safety) → `npm run build` →
   `ng test` (headless) → `/design-review` score. On green: commit
   `feat(design): design-system foundation + mobile app-shell`, push, open PR.

**Runs 2..N — one module/screen per PR:**
Director writes the screen brief → `@frontend-dev` applies it → verify gate (build/tests green, no
WCAG-AA/contrast regressions, `/design-review` Design Score + AI-Slop Score delta ≥ 0) → commit
`feat(design/{module}): redesign {screen} to design system` → PR. Scoped/resumed by
`docs/vault/design/REDESIGN-STATUS.md`.

**Inherited hard rules (from `/implement-all`):** never weaken/skip/delete a test to go green;
`@frontend-dev` does not commit (the skill owns commit/PR); one PR per run, opened not auto-merged;
clean working tree required to start.

### 3. `docs/vault/design/` — the living design system

- `README.md` — folder contract + how the two agents use it.
- `REDESIGN-STATUS.md` — the loop checklist (foundation + per-module rows), like `BA/STATUS.md`.
- `tokens.md`, `mobile-app-shell.md`, `ux-guidelines.md` — authored by the director on Run 1
  (not pre-built here; that is the skill's deliverable).
- `briefs/{screen}.md` — per-screen redesign briefs.

### Edits to existing files

- **`.claude/agents/team/frontend-dev.md`** — replace the embedded "Design Language
  (Notion-inspired)" block with a **pointer** to `docs/vault/design/` so there is one source of
  truth. (In-scope change; flagged.)
- **`CLAUDE.md`** — add `@design-director` to the Agent Team table and `/redesign` to the Skills
  table.

## The mobile app-shell (distinctive deliverable)

Below a breakpoint the desktop sidebar is swapped for an app shell:
- **Bottom tab bar** (primary nav), **top app-bar** with contextual back, **bottom sheets** for
  modals/actions.
- **`env(safe-area-inset-*)`** padding (notch / home indicator), **≥44px** touch targets, **no
  hover-only** affordances, momentum scroll, `100dvh` viewport handling.
- Webview hygiene: controlled overscroll/pull-to-refresh, sane `user-scalable`, controlled
  tap-highlight.
- Desktop keeps the existing sidebar — **two coordinated layouts, one responsive codebase**, both
  driven by the shared tokens.

## Success criteria

- Foundation PR renders an app-like mobile shell in the webview + a real light/dark token system;
  `dotnet`/`ng` gates green; `/design-review` produces a baseline score.
- Each screen run **raises or holds** the `/design-review` Design Score and AI-Slop Score with
  **zero** WCAG-AA / contrast regressions, gates green.
- One reviewable PR per run; `REDESIGN-STATUS.md` reflects progress.

## Risks

- **Foundation PR is large and load-bearing** (Confidence: 80%). It touches shared layout every
  feature depends on, so it needs careful review before the screen loop stacks on top. The
  screen-by-screen loop is what contains overall size — each later PR is small.
- **Lane overlap with `@frontend-dev`.** Mitigated: the director never edits `src/`; the skill uses
  `@frontend-dev` as the sole builder; the design-language pointer removes the duplicate source of
  truth.

## Out of scope

- Per-tenant theming (accent/logo override) and any theme-management UI.
- Replacing Angular Material or Tailwind; migrating the (deprecated) Karma test runner.
- Backend changes.
