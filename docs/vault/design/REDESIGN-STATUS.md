# Redesign Status — `/redesign` loop checklist

Source of truth for the [`/redesign`](../../../.claude/skills/redesign.md) loop. One row per item.
Legend: `[ ]` pending · `[~]` in progress · `[x]` done · `[b]` blocked.

## Foundation (do first — load-bearing)

- [ ] **foundation** — design tokens (light+dark CSS vars) · native mobile app-shell (bottom tabs,
  app-bar+back, bottom sheets, safe-area, `100dvh`, ≥44px touch) · shared component primitives.
  _PR:_ — _Design Score:_ — _AI-Slop:_ —

## Modules (screen-by-screen, after foundation — priority order)

| # | Module | Status | PR | Design Score Δ | Brief |
|---|--------|--------|----|----|-------|
| 1 | Authentication (login/reset/MFA) | [ ] | — | — | — |
| 2 | Dashboard | [ ] | — | — | — |
| 3 | Core HR — Employees / Departments / Org tree | [ ] | — | — | — |
| 4 | Leave Management | [ ] | — | — | — |
| 5 | Attendance | [ ] | — | — | — |
| 6 | Recruitment | [ ] | — | — | — |
| 7 | Payroll | [ ] | — | — | — |
| 8 | Performance Management | [ ] | — | — | — |
| 9 | Admin Console (System + Tenant) | [ ] | — | — | — |
| 10 | Onboarding / Offboarding | [ ] | — | — | — |
| 11 | Training & Benefits | [ ] | — | — | — |
| 12 | Reports & Analytics | [ ] | — | — | — |
| 13 | Notifications & Audit | [ ] | — | — | — |

> Order mirrors the CLAUDE.md **Module Priority**. `/redesign {module}` can jump to a specific row;
> plain `/redesign` takes the next `[ ]` after the foundation is `[x]`.
