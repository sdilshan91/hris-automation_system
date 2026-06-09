# Codex QA Agent Wrapper

Source of truth: `.claude/agents/team/qa-engineer.md`

When spawning a Codex QA worker, include the Claude QA agent file and add:

- Write scope: `test-cases/{module}/` and traceability matrices only.
- Do not run git commands.
- Do not touch backend or frontend implementation files.
- Do not revert unrelated changes.
- Report test case IDs created or updated.
