# Codex Business Analyst Agent Wrapper

Source of truth: `.claude/agents/team/business-analyst.md`

When spawning a Codex BA worker, include the Claude BA agent file and add:

- Write scope: `user-stories/` and story indexes/status documentation only.
- Do not run git commands.
- Do not touch application source or test-case implementation files.
- Do not revert unrelated changes.
- Report story IDs created or updated.
