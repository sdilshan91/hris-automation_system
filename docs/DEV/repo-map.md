# Repository map

> Extracted from CLAUDE.md on 2026-08-23. It is a reference map, not per-session guidance — an agent
> can Glob for any of this — so it no longer costs context in every session. Keep it accurate; the
> agent/skill/hook tables in CLAUDE.md are the authoritative lists.

## Directory Structure

```
├── .env                           # API keys (gitignored, local only)
├── .env.example                   # Template for .env
├── .mcp.json                      # MCP servers (github, playwright, chrome-devtools, microsoft-learn) — loaded by Claude Code
├── .gitignore
├── docs/                          # Discipline-based documentation (source of truth)
│   ├── Architecture/              # System design, tech-radar, ADR index, tech doc, security reviews
│   │   ├── radar/                 #   tech-radar (moved from docs/radar/)
│   │   ├── advisory-reports/      #   /advisor output
│   │   ├── security-reviews/      #   /security-audit output
│   │   ├── hrm_technical_document_v4.0.md
│   │   └── {README,STATUS,PLANS,BLOCKERS,DECISIONS,INSTRUCTIONS}.md
│   ├── BA/                        # IEEE 830 user stories (by module) — was user-stories/
│   │   ├── {module-name}/US-{MOD}-001.md
│   │   ├── INDEX.md · STATUS.md   #   STATUS.md = implement-all source of truth
│   │   └── {README,PLANS,BLOCKERS,DECISIONS,INSTRUCTIONS}.md
│   ├── QA/                        # IEEE 829 test cases + findings/plans — was test-cases/
│   │   ├── {module-name}/TC-{MOD}-001.md
│   │   ├── TEST-STATUS.md · TEST-FINDINGS.md · BUG-STATUS.md · TRACEABILITY-MATRIX.md
│   │   ├── plans/                 #   COMPLETION-PLAN.md = the ONE living plan (dated changelog, rolls over in place)
│   │   │   └── archive/           #   superseded dated plans (full snapshots)
│   │   ├── reports-archive/       #   dated QA reports/snapshots (bug-report, coverage, triage, decisions-needed)
│   │   └── {README,STATUS,PLANS,BLOCKERS,DECISIONS,INSTRUCTIONS}.md
│   ├── DEV/                       # Build/run/CI conventions, tooling adoption
│   │   ├── TOOLING-ADOPTION-PLAN.md
│   │   ├── references/            #   vendored 3rd-party .NET reference docs (antipatterns, MediatR→Mediator)
│   │   └── {README,STATUS,PLANS,BLOCKERS,DECISIONS,INSTRUCTIONS}.md   # links local-dev/, ops/, perf/
│   ├── Frontend/                  # Angular 20 conventions + FE findings
│   │   └── {README,STATUS,PLANS,BLOCKERS,DECISIONS,INSTRUCTIONS}.md
│   ├── Design/                    # Visual/UX; /design-review output
│   │   ├── design-reports/
│   │   └── {README,STATUS,PLANS,BLOCKERS,DECISIONS,INSTRUCTIONS}.md
│   ├── vault/                     # Obsidian vault — shared agent memory (UNCHANGED; see Shared Memory)
│   └── superpowers/               # brainstorming/writing-plans specs+plans (UNCHANGED)
├── local-dev/ · ops/ · perf/      # Operational config/scripts (NOT docs — stay top-level)
├── src/
│   ├── frontend/                  # Angular 20 SPA
│   └── backend/                   # ASP.NET Core 10 API
├── .claude/
│   ├── agents/team/               # Agent definitions (with MCP tools)
│   │   ├── business-analyst.md
│   │   ├── frontend-dev.md
│   │   ├── backend-dev.md
│   │   ├── qa-engineer.md
│   │   └── browser-debugger.md    # Playwright-driven UI debugger (read-only)
│   ├── agents/review/             # Auxiliary read-only review agents (local, adapted)
│   │   ├── test-authenticator.md  # Flags fake/theatrical tests (report-only)
│   │   ├── integration-enforcer.md # Flags orphaned/unwired code (report-only)
│   │   └── principal-advisor.md   # Read-only technical-consultant synthesizer
│   ├── skills/                    # Slash command skills
│   │   ├── orchestrate.md         # Local + MCP pipeline
│   │   ├── analyze-module.md
│   │   ├── implement-story.md
│   │   ├── debug-ui.md            # Browser debugging via Playwright MCP
│   │   ├── design-review.md       # Designer's-eye visual + UX audit (report-only)
│   │   ├── fault-diagnosis.md     # Root-cause-before-fix discipline (local)
│   │   ├── error-recovery.md      # Stuck-loop breaker / failure-counter (local)
│   │   ├── retro.md               # Engineering retrospective from git + ledgers (local)
│   │   ├── advisor.md             # Technical-consultant advisory (report-only); + advisor/currency-scan.py
│   │   └── github-pipeline.md     # Remote pipeline (needs credits)
│   ├── hooks/                     # Automation hooks
│   │   ├── post-user-story-commit.sh
│   │   └── post-dev-commit.sh
│   └── settings.json              # hooks, permissions, skill overrides (NOT MCP servers — see .mcp.json)
└── .github/
    └── workflows/
        └── claude-agent-pipeline.yml  # GitHub Actions (future, needs credits)
```

