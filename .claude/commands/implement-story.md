---
argument-hint: US-XXX-NNN
description: Implement ONE specific user story end-to-end (BE + FE + QA on one feature branch + PR). Manual single-shot version that does NOT touch STATUS.md.
---

# Implement-Story — single-story build

Full specification: [.claude/skills/implement-story.md](.claude/skills/implement-story.md). Read it before executing.

**Argument:** `$ARGUMENTS` (required — must match `US-[A-Z]+-\d{3}`)

## Workflow

1. Validate the story ID and locate the file at `user-stories/{module}/US-XXX.md`. Abort if missing.
2. Verify clean working tree on `main`. Abort if dirty.
3. Create branch `feature/US-XXX`.
4. Read the story plus `docs/vault/modules/{module}.md` for context.
5. Launch three sub-agents in parallel **in a single message** (no worktree isolation; they write to non-overlapping paths):
   - `@backend-dev` → `src/backend/`, NO git commands
   - `@frontend-dev` → `src/frontend/`, NO git commands
   - `@qa-engineer` → `test-cases/{module}/`, NO git commands
   Use the prompt templates from `.claude/skills/implement-all.md` to keep behavior identical between the two commands.
6. Verify with `dotnet build src/backend/HRM.sln` and `ng build` in `src/frontend/`. Abort on failure.
7. Run `dotnet test src/backend/HRM.sln --no-build` — report failures on the PR but don't block.
8. Single commit: `feat(US-XXX): <story title>` with BE/FE/QA summary in body.
9. Push branch + open PR via GitHub MCP. Title: `feat: US-XXX — <story title>`. Body includes AC checklist and TC IDs.
10. Print the PR URL.

## Difference from `/implement-all`

This command does NOT touch [user-stories/STATUS.md](user-stories/STATUS.md). Use it when you want manual control over which story is built and don't want the tracker auto-updated. Use `/implement-all` when you want the looped, tracker-driven flow.
