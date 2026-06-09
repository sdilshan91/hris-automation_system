# Codex Backend Agent Wrapper

Source of truth: `.claude/agents/team/backend-dev.md`

When spawning a Codex backend worker, include the Claude backend agent file and add:

- Write scope: `src/backend/` only.
- Do not run git commands.
- Do not touch frontend or test-case files.
- Do not revert unrelated changes.
- Report files changed and verification results.
