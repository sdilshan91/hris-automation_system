# Codex Command: Implement Story

Claude source of truth:

- `.claude/commands/implement-story.md`
- `.claude/skills/implement-story.md`
- Prompt templates in `.claude/skills/implement-all.md`

Use this when the user asks Codex to run `/implement-story US-XXX-000`, `implement-story US-XXX-000`, or to implement one named story.

## Codex Adaptation

1. Validate the story ID matches `US-[A-Z]+-[0-9]{3}`.
2. Locate the story under `user-stories/{module}/`.
3. Read `CLAUDE.md`, `.claude/dev-instructions.md`, the story file, and related vault notes.
4. Verify the working tree is clean and the current branch is `main`.
5. Create `feature/US-XXX`.
6. If Codex sub-agent tools are available, spawn four parallel agents with controlled write scopes:
   - Backend: `src/backend/`
   - Frontend: `src/frontend/`
   - DB: persistence-specific backend files such as EF configurations, migrations, seed data, query filters, and database indexes
   - QA: `test-cases/{module}/`
7. Give backend, frontend, and QA agents the matching prompt template from `.claude/skills/implement-all.md`. Give DB the wrapper at `.codex/agents/team/db-engineer.md`, the story file, and the backend context. Add this Codex-specific rule to every agent:
   - "You are not alone in the codebase. Do not revert unrelated changes or edits made by other agents."
8. Run an integration/bug-fix pass locally:
   - Review agent outputs for overlapping edits.
   - Fix compile, contract, migration, routing, and traceability issues.
   - Keep bug fixes in the same feature branch.
9. Verify:
   - `dotnet build src/backend/HRM.sln`
   - `npm.cmd run build` in `src/frontend` on Windows, otherwise `npm run build`
   - `dotnet test src/backend/HRM.sln --no-build` if a test project exists; report failures but do not hide them.
10. Commit all story work as one commit using the message format in `.claude/skills/implement-story.md`.
11. Push and open a PR using GitHub MCP if available, otherwise `git push` and `gh pr create` if configured.
12. Return the PR URL.

Do not modify `user-stories/STATUS.md`; that is the main difference from `implement-all`.
