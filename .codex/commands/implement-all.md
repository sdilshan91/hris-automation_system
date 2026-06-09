# Codex Command: Implement All

Claude source of truth:

- `.claude/commands/implement-all.md`
- `.claude/skills/implement-all.md`

Use this when the user asks Codex to run `/implement-all`, `implement-all`, "next story", or a scoped variant such as `implement-all auth`.

## Codex Adaptation

1. Read the two Claude source files above in full.
2. Read `CLAUDE.md`, `.claude/dev-instructions.md`, and `user-stories/STATUS.md`.
3. Resolve the argument exactly like the Claude command:
   - Empty: first pending `[ ]` story across modules in priority order.
   - Module key: first pending `[ ]` story in that module.
   - Story ID: that exact story, warning before redoing a completed story.
4. Verify the working tree is clean and the current branch is `main`.
5. Mark the selected story `[~]` in `user-stories/STATUS.md` and commit `chore(status): start US-XXX`.
6. Create `feature/US-XXX`.
7. If Codex sub-agent tools are available, spawn four parallel agents with controlled write scopes:
   - Backend: `src/backend/`
   - Frontend: `src/frontend/`
   - DB: persistence-specific backend files such as EF configurations, migrations, seed data, query filters, and database indexes
   - QA: `test-cases/{module}/`
8. Give backend, frontend, and QA agents the matching prompt template from `.claude/skills/implement-all.md`. Give DB the wrapper at `.codex/agents/team/db-engineer.md`, the story file, and the backend context. Add this Codex-specific rule to every agent:
   - "You are not alone in the codebase. Do not revert unrelated changes or edits made by other agents."
9. After agents finish, run an integration/bug-fix pass locally:
   - Review agent outputs for overlapping edits.
   - Fix compile, contract, migration, routing, and traceability issues.
   - Keep bug fixes in the same feature branch.
10. Verify:
   - `dotnet build src/backend/HRM.sln`
   - `npm.cmd run build` in `src/frontend` on Windows, otherwise `npm run build`
   - `dotnet test src/backend/HRM.sln --no-build` if a test project exists; report failures but do not hide them.
11. Commit all story work as one commit using the message format in `.claude/skills/implement-all.md`.
12. Push and open a PR using GitHub MCP if available, otherwise `git push` and `gh pr create` if configured.
13. Switch back to `main`, mark `[~]` -> `[x]`, update tally counters, commit `chore(status): mark US-XXX done`, and push.
14. Return the PR URL and stop. Do not automatically continue to the next story unless the user explicitly asked for batching.

## Failure Handling

Follow `.claude/skills/implement-all.md` exactly:

- Build failure: leave the branch local, revert the status from `[~]` to `[ ]`, and do not open a PR.
- Agent blocker: mark `[~] blocked: <reason>` and report the blocker.
- Dirty worktree at start: stop and ask the user how to handle their changes.
