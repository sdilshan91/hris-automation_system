# Codex Project Instructions

This repository was originally set up for Claude Code. Preserve the Claude setup and treat it as a first-class source of truth.

## Source Of Truth

Read these files before doing project or automation work:

- `CLAUDE.md` - project overview, architecture, agent workflow, build commands, critical rules.
- `.claude/dev-instructions.md` - owner preferences for frontend, backend, local services, and story automation.
- `.claude/skills/*.md` - detailed workflow specs for story automation.
- `.claude/agents/team/*.md` - role-specific instructions for BA, frontend, backend, QA, and browser debugging.
- `docs/vault/Home.md` and `docs/vault/README.md` - shared memory conventions.
- `user-stories/STATUS.md` - tracker used to pick the next story.

Do not move, rename, rewrite, or simplify `.claude/` files unless the user explicitly asks for Claude changes.

## Codex Compatibility

Codex-facing wrappers live in `.codex/`. They mirror the Claude commands and point back to `.claude/` so there is not a second workflow to keep in sync.

When the user asks for one of these Claude-style workflows in Codex, follow the matching wrapper:

- `implement-all`, `/implement-all`, or "next story" -> `.codex/commands/implement-all.md`
- `implement-story`, `/implement-story`, or a specific `US-XXX-000` story -> `.codex/commands/implement-story.md`
- `orchestrate`, `analyze-module`, `debug-ui`, `github-pipeline` -> read the matching file in `.claude/skills/` and adapt it for Codex.

## Agent Execution

If sub-agent tools are available and the user explicitly asks for agents, delegation, parallel agent work, or a story automation command:

- Use parallel agents only for disjoint write scopes.
- Backend agent owns `src/backend/`.
- Frontend agent owns `src/frontend/`.
- DB agent owns persistence-specific backend concerns only: EF configurations, migrations, seed data, query filters, database indexes, and database notes. Coordinate with backend before touching shared application/domain files.
- QA agent owns `test-cases/`.
- Business analyst owns `user-stories/` and related index/status documentation.
- Browser debugger is read-only and may inspect the running app, console, network, and DOM.

All implementation agents must be told:

- They are not alone in the codebase.
- They must not revert unrelated changes.
- They must not run `git add`, `git commit`, `git push`, create branches, or open PRs.
- The orchestrator owns git, verification, status updates, and PR creation.

## Git And Status Safety

- Check `git status --short` before story automation or broad edits.
- Do not overwrite user changes.
- For `/implement-all`, only update `user-stories/STATUS.md` according to the state machine in `.claude/skills/implement-all.md`.
- For `/implement-story`, do not touch `user-stories/STATUS.md`.
- Prefer GitHub MCP tools when available for branches, pushes, issues, and PRs. If not available, use normal git/`gh` commands when configured.

## Build And Test Commands

Backend:

```bash
dotnet restore src/backend/HRM.sln
dotnet build src/backend/HRM.sln
dotnet test src/backend/HRM.sln --no-build
```

Frontend:

```bash
cd src/frontend
npm install
npm start
npm run build
npm test
npm run lint
```

On Windows PowerShell, prefer `npm.cmd` if `npm` is blocked by execution policy.

## Non-Negotiables

- Tenant isolation is mandatory in queries, cache keys, API calls, tests, and UI flows.
- User stories follow IEEE 830 / ISO/IEC/IEEE 29148 style.
- Test cases follow IEEE 829 / ISO/IEC/IEEE 29119 style.
- Preserve traceability between story IDs, acceptance criteria, code, and test cases.
- Secrets stay in `.env`, user-secrets, or local environment variables. Never commit real secrets.
