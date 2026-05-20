---
name: implement-story
description: Implement ONE specific user story end-to-end (BE + FE + QA on one feature branch + PR). Use when you want manual control over which story; use /implement-all for the looped tracker-driven version.
user_invocable: true
---

# Implement One User Story

Implements a single user story by running `backend-dev`, `frontend-dev`, and `qa-engineer` sub-agents in parallel against ONE feature branch, then opening a PR.

## Usage

```
/implement-story US-AUTH-005
/implement-story US-CHR-007
```

## Relationship to `/implement-all`

- `/implement-story` = manual one-shot for a specific story you name.
- `/implement-all` = picks the next pending story from `user-stories/STATUS.md`, then runs essentially this same flow, and flips the status afterward.

Both produce **one branch + one commit + one PR per story** with FE + BE + QA bundled.

## Process

1. **Validate input** — story ID matches `US-[A-Z]+-\d{3}` and the story file exists at `user-stories/{module}/US-{ID}.md`.
2. **Pre-flight** — working tree clean, on `main`, pulled fresh. Abort otherwise.
3. **Branch** — create `feature/US-{ID}` via `mcp__github__create_branch` (or `git checkout -b`).
4. **Read** the story file and `docs/vault/modules/{module}.md` for prior context.
5. **Parallel sub-agents** — launch three Agent calls in one message (no worktree isolation; they write to non-overlapping paths):
   - `@backend-dev`  → implements in `src/backend/`, no git commands
   - `@frontend-dev` → implements in `src/frontend/`, no git commands
   - `@qa-engineer`  → writes test cases in `test-cases/{module}/`, no git commands
   Use the prompt templates from `.claude/skills/implement-all.md` (DRY).
6. **Verify** — `dotnet build src/backend/HRM.sln` and `ng build` in `src/frontend/`. Abort on failure (keep branch local).
7. **Test** — `dotnet test src/backend/HRM.sln --no-build`. Report failures but continue to PR (they go on the PR description as known issues).
8. **Commit** — single commit:
   ```
   feat(US-{ID}): {story title}

   Backend: {summary}
   Frontend: {summary}
   QA: {N test cases added}

   Refs: user-stories/{module}/US-{ID}.md
   ```
9. **Push + PR** — `mcp__github__push_files` + `mcp__github__create_pull_request`. PR body includes the AC checklist and TC-IDs added.
10. **Return** — print the PR URL.

## Does NOT touch STATUS.md

Unlike `/implement-all`, this skill leaves `user-stories/STATUS.md` alone. If you want the tracker updated, hand-edit STATUS.md or use `/implement-all` instead.

## Failure handling

Same as `/implement-all`:
- Build failures abort the push/PR step. Branch stays local.
- Sub-agent blockers stop the run with a clear message.
- Working tree dirty → abort, ask user to commit/stash first.
