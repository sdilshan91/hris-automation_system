# Documentation

Discipline-based documentation for the HRM SaaS platform. Each discipline folder carries the
same standard dashboards — **README · STATUS · PLANS · BLOCKERS · DECISIONS · INSTRUCTIONS** —
that summarize and link the detailed docs beside them.

| Discipline | Owns | Was |
|---|---|---|
| [Architecture/](Architecture/) | System design, tech doc, tech-radar, ADR index, advisory + security reviews | `docs/radar/`, `advisory-reports/`, tech doc |
| [BA/](BA/) | IEEE 830 user stories (by module); `STATUS.md` = `/implement-all` source of truth | `user-stories/` |
| [QA/](QA/) | IEEE 829 test cases, findings ledger, completion/coverage plans | `test-cases/` |
| [DEV/](DEV/) | Build/run/CI conventions, tooling, per-story research reports | net-new + `docs/TOOLING-ADOPTION-PLAN.md` |
| [Frontend/](Frontend/) | Angular 20 conventions, FE findings | net-new |
| [Design/](Design/) | Visual/UX audits (`/design-review`) | net-new |

## Not restructured (intentionally)
- **[vault/](vault/)** — the Obsidian **shared agent memory** (ADRs, module domain rules, handoffs, incidents). Disciplines *link into* it (e.g. `Architecture/DECISIONS.md` indexes `vault/decisions/`); it is not absorbed.
- **[superpowers/](superpowers/)** — brainstorming/writing-plans specs + plans (skill-owned path).
- **`local-dev/`, `ops/`, `perf/`** (repo root) — operational config/scripts wired into nginx/Docker/CI, not docs. Linked from `DEV/` and `QA/`.

See the restructure spec: [superpowers/specs/2026-07-11-docs-restructure-design.md](superpowers/specs/2026-07-11-docs-restructure-design.md).
