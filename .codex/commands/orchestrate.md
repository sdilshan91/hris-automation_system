# Codex Command: Orchestrate

Claude source of truth:

- `.claude/skills/orchestrate.md`
- `.claude/agents/team/business-analyst.md`
- `.claude/agents/team/frontend-dev.md`
- `.claude/agents/team/backend-dev.md`
- `.claude/agents/team/qa-engineer.md`

Use this when the user asks Codex to run `/orchestrate`, `orchestrate`, or the full local agent pipeline.

## Codex Adaptation

1. Read the Claude source files above, `CLAUDE.md`, and `.claude/dev-instructions.md`.
2. Ask for or infer the target module. Follow the module priority in `CLAUDE.md` if the user asks for the next module.
3. Stage 1: run business analysis first.
   - Preferred write scope: `user-stories/{module}/`, `user-stories/INDEX.md`, and `user-stories/STATUS.md`.
   - Use the business analyst role spec.
4. Stage 2: run implementation and QA in parallel only when scopes are disjoint.
   - Frontend owns `src/frontend/`.
   - Backend owns `src/backend/`.
   - QA owns `test-cases/{module}/` and traceability matrices.
5. Tell all worker agents:
   - They are not alone in the codebase.
   - Do not revert unrelated changes.
   - Do not run `git add`, `git commit`, `git push`, create branches, or open PRs unless this run explicitly delegates GitHub MCP operations to that role.
6. Stage 3: the orchestrator verifies frontend/backend API alignment, story/test traceability, and build/test results.
7. Use GitHub MCP for branch, PR, and issue operations when available. Otherwise, report the exact `git` or `gh` fallback commands used or needed.

Prefer the story-by-story `.codex/commands/implement-all.md` loop for normal development. Use this full pipeline for greenfield modules or when the user explicitly asks for the original BA -> FE/BE/QA workflow.
