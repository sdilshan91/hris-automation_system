# Design System — living source of truth

This folder is the **one source of truth** for how the HRM SaaS UI looks and feels as a
**webview mobile app**. Both `@design-director` (who authors it) and `@frontend-dev` (who
implements it) read from here — design rules do **not** live in agent files or code comments.

> Spec: [`docs/superpowers/specs/2026-07-11-design-director-redesign-design.md`](../../superpowers/specs/2026-07-11-design-director-redesign-design.md)
> Loop: [`/redesign`](../../../.claude/skills/redesign.md) · Director: [`@design-director`](../../../.claude/agents/review/design-director.md)

## Locked decisions

- **Mobile model:** native-app shell (bottom tab bar · app-bar + back · bottom sheets · safe-area ·
  ≥44px touch · no hover-only · `100dvh`). Desktop keeps the sidebar. Two layouts, one codebase.
- **Aesthetic:** evolve the Notion-inspired language, formalized into tokens. No rebrand.
- **Tokens:** semantic CSS variables, **light + dark** via `prefers-color-scheme`. **No per-tenant
  theming** yet (structure for a future accent seam, build none now).
- **Rollout:** foundation first, then one module/screen per `/redesign` run.

## Files

| File | Owner | What it is |
|------|-------|------------|
| `tokens.md` | `@design-director` | Semantic token set: color (light+dark), spacing, type scale, radius, elevation, motion. The contract `@frontend-dev` turns into CSS variables. *(authored on the foundation run)* |
| `mobile-app-shell.md` | `@design-director` | Native mobile-shell spec: breakpoint, bottom-tab destinations, app-bar/back, bottom sheets, safe-area, gestures, webview hygiene, desktop↔mobile switch. *(foundation run)* |
| `ux-guidelines.md` | `@design-director` | Interaction states, motion (`prefers-reduced-motion`), touch minimums, content/microcopy rules. *(foundation run)* |
| `briefs/{screen}.md` | `@design-director` | Per-screen redesign briefs — build-ready, one per module/screen the loop touches. |
| `REDESIGN-STATUS.md` | `/redesign` | The loop checklist (foundation + per-module rows). |

`tokens.md`, `mobile-app-shell.md`, and `ux-guidelines.md` are **not** pre-written — the foundation
`/redesign` run authors them. This README + `REDESIGN-STATUS.md` are the only scaffold.

## Verification

Design changes are graded by [`/design-review`](../../../.claude/skills/design-review.md) (Design
Score + AI-Slop Score) with baselines in [`docs/Design/design-reports/`](../../Design/design-reports/).
A `/redesign` run must **raise or hold** both scores with **zero** WCAG-AA / contrast regressions.
