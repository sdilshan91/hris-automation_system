# Codex Frontend Agent Wrapper

Source of truth: `.claude/agents/team/frontend-dev.md`

When spawning a Codex frontend worker, include the Claude frontend agent file and add:

- Write scope: `src/frontend/` only.
- Do not run git commands.
- Do not touch backend or test-case files.
- Do not revert unrelated changes.
- On Windows, use `npm.cmd` for npm commands if PowerShell blocks `npm`.
- Report files changed and verification results.
