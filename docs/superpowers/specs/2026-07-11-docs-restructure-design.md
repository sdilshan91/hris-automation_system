# Design Spec — Discipline-based `docs/` restructure

**Date:** 2026-07-11
**Status:** Approved (design) — implementation via `chore/docs-restructure`
**Author:** orchestrator (with user)

## 1. Problem

Project documentation is scattered across un-parented top-level dirs (`test-cases/`,
`user-stories/`, `advisory-reports/`) and a shallow `docs/` (loose files + `radar/`,
`superpowers/`, `vault/`). There is no per-discipline home and no uniform "front page"
(status / plans / blockers / decisions / instructions) a human or agent can open to
orient. The goal: a **discipline-based** structure — `Architecture, QA, DEV, BA, Design,
Frontend` — each with the same standard dashboards, **without** fragmenting the existing
shared knowledge (the Obsidian vault) or breaking the ~100 automation references.

## 2. Decisions (locked with user)

| # | Decision | Choice |
|---|----------|--------|
| D1 | Structure model | **Move** existing doc folders *into* discipline parents (not duplicate). |
| D2 | Vault | **Keep `docs/vault/` intact**; disciplines *link into* it, never absorb it. |
| D3 | Filenames | **Keep existing filenames**; add the standard dashboards *on top*. |
| D4 | Infra dirs | `local-dev/`, `ops/`, `perf/` **stay put** (operational config wired to nginx/Docker/CI — `local-dev/nginx.dev.conf` hardcodes `local-dev/certs/` paths). Disciplines link to them. |
| D5 | Skill-owned paths | `docs/superpowers/{specs,plans}` **stay** (brainstorming/writing-plans write there by hardcoded path). |
| D6 | Existing status files | Do **not** overwrite `user-stories/STATUS.md` or `test-cases/TEST-STATUS.md`; they serve the STATUS role. QA's new `STATUS.md` is a thin pointer to `TEST-STATUS.md`+`BUG-STATUS.md`. |

## 3. Target layout

```
docs/
├── Architecture/        ← docs/radar/, hrm_technical_document_v4.0.md(+pdf),
│   ├── radar/              observability-otel-grafana-plan.md, advisory-reports/,
│   ├── advisory-reports/   security-reviews/ (created; /security-audit repointed)
│   ├── security-reviews/
│   ├── hrm_technical_document_v4.0.md (+ .pdf)
│   ├── observability-otel-grafana-plan.md
│   └── README/STATUS/PLANS/BLOCKERS/DECISIONS/INSTRUCTIONS.md   (new)
├── QA/                  ← ALL of test-cases/* (TCs, matrices, every *-PLAN*.md,
│   ├── <module>/           TEST-FINDINGS.md, TEST-STATUS.md, BUG-STATUS.md, playbooks…)
│   ├── TEST-FINDINGS.md
│   ├── TEST-STATUS.md
│   ├── BUG-STATUS.md
│   ├── COMPLETION-PLAN-2026-07-11.md   (active) + the older CLOSED plans
│   └── README/STATUS/PLANS/BLOCKERS/DECISIONS/INSTRUCTIONS.md   (new dashboards)
├── BA/                  ← ALL of user-stories/* (module folders, INDEX.md, STATUS.md)
│   ├── <module>/
│   ├── INDEX.md
│   ├── STATUS.md        (existing — implement-all source of truth; kept as-is)
│   └── README/PLANS/BLOCKERS/DECISIONS/INSTRUCTIONS.md   (new; STATUS already present)
├── DEV/                 ← TOOLING-ADOPTION-PLAN.md; links to local-dev/, ops/, perf/
│   └── README/STATUS/PLANS/BLOCKERS/DECISIONS/INSTRUCTIONS.md   (new)
├── Frontend/           ← net-new (Angular 20 conventions, FE findings, angular-developer skill)
│   └── README/STATUS/PLANS/BLOCKERS/DECISIONS/INSTRUCTIONS.md   (new)
├── Design/             ← design-reports/ (created; /design-review repointed)
│   ├── design-reports/
│   └── README/STATUS/PLANS/BLOCKERS/DECISIONS/INSTRUCTIONS.md   (new)
├── vault/              ← UNCHANGED (shared agent memory)
└── superpowers/        ← UNCHANGED (skill specs/plans)
```

Top-level `test-cases/`, `user-stories/`, `advisory-reports/` **cease to exist** (moved).

## 4. Standard dashboards (per folder)

Each discipline gets six thin, *living* front-page files that **summarize + link**, never
duplicate, the detailed docs beside them and the relevant vault notes:

- **README.md** — what this discipline owns, folder map, who writes here.
- **STATUS.md** — current state at a glance (links the detailed status file where one exists).
- **PLANS.md** — index of active/closed plans + roadmap (QA: indexes every `*-PLAN*.md` with active/CLOSED status).
- **BLOCKERS.md** — open blockers (QA: open HIGH/MED from `TEST-FINDINGS.md`; Frontend: ISSUE-245 Angular-suite; etc.).
- **DECISIONS.md** — discipline decisions + an index into `docs/vault/decisions/` ADRs (no ADR content copied — link only).
- **INSTRUCTIONS.md** — how to work in this discipline (QA: exploratory playbook + env-setup + `/test-all`; BA: IEEE-830 authoring; Arch: conventions from CLAUDE.md; etc.).

## 5. Reference-update (the risk)

Every reference is a **mechanical path-prefix rewrite**, fully greppable/verifiable:

| Old prefix | New prefix |
|---|---|
| `test-cases/` | `docs/QA/` |
| `user-stories/` | `docs/BA/` |
| `advisory-reports/` | `docs/Architecture/advisory-reports/` |
| `docs/radar/` | `docs/Architecture/radar/` |
| `docs/TOOLING-ADOPTION-PLAN.md` | `docs/DEV/TOOLING-ADOPTION-PLAN.md` |
| `docs/hrm_technical_document_v4.0.md` | `docs/Architecture/hrm_technical_document_v4.0.md` |
| `docs/observability-otel-grafana-plan.md` | `docs/Architecture/observability-otel-grafana-plan.md` |
| `security-reviews/` (skill output) | `docs/Architecture/security-reviews/` |
| `design-reports/` (skill output) | `docs/Design/design-reports/` |

Scope: `.claude/agents/**`, `.claude/skills/**`, `.claude/hooks/**`, `.claude/settings*.json`,
`CLAUDE.md`, `.github/workflows/**`, vault wiki-links, and the user's `MEMORY.md` (outside the
repo). `perf/` is **not** rewritten (stays put; its 68 refs are internal). Rewrites are done
per-file with `git mv` first (history preserved), then token replacement, reviewed in the diff.

**Do NOT rewrite blindly:** skip prose/URLs where the token is incidental; the replacements
target path references only. Because the whole folder moves as a unit, an internal cross-ref
that used the `test-cases/` prefix still resolves after rewrite to `docs/QA/`.

## 6. Verification (before PR)

1. `git grep -nE '(^|[^/])(test-cases|user-stories|advisory-reports)/'` → **zero** hits outside `docs/` history and this spec.
2. `git grep -n 'docs/radar/'` / `docs/TOOLING-ADOPTION-PLAN` / etc. → zero old-path hits.
3. Every moved file resolves (`ls` the new paths); no dangling relative links (spot-check the dashboards' links).
4. Smoke the automation: a guard hook still fires; `implement-all` can find `docs/BA/STATUS.md`; `test-all` can find `docs/QA/TEST-STATUS.md` + `TEST-FINDINGS.md`.
5. `dotnet`/`ng` untouched — no src path references these doc dirs (confirmed: reference counts are all in docs/automation, not src).
6. Update `MEMORY.md` pointers (active plan path `docs/QA/COMPLETION-PLAN-2026-07-11.md`, findings ledger path).

## 7. Out of scope

No content rewrites, no plan reconciliation, no ADR authoring beyond index links, no infra-dir
moves, no vault reorganization. Purely structural + reference-integrity. The P0 ledger reconcile
and the 14-HIGH triage from `COMPLETION-PLAN-2026-07-11.md` remain separate, human-decided work.

## 8. Rollout

One `chore/docs-restructure` branch off `test/local-subdomains` → folders+dashboards → `git mv`
moves → reference rewrites → verification → single reviewable PR. No src build impact.
