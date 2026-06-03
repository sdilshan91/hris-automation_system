---
argument-hint: [module | US-XXX-NNN]
description: Pick the next pending user story from user-stories/STATUS.md and build it end-to-end (BE + FE + QA on one feature branch + PR). One story per invocation.
---

# Implement-All — story-by-story automation loop

The full specification for this command lives at [.claude/skills/implement-all.md](.claude/skills/implement-all.md). Read that file in full before doing anything else, then execute its workflow for the argument below.

**Argument:** `$ARGUMENTS`

## Argument resolution

- **Empty** → pick the first `[ ]` story in [user-stories/STATUS.md](user-stories/STATUS.md) across all modules in priority order.
- **Module key** (`auth`, `core-hr`, `leave`, `attendance`, `recruitment`, `payroll`, `performance`, `admin`, `onboarding`, `notifications`, `reports`) → pick the first `[ ]` story in that module.
- **Specific story ID** (`US-AUTH-005`, `US-CHR-007`, etc.) → implement that exact story. If it's already `[x]`, stop and ask the user before redoing.

## Execution summary (see skill file for full detail)

1. Read [user-stories/STATUS.md](user-stories/STATUS.md), pick the target story
2. Verify clean working tree on `main`
3. Flip story to `[~]` in STATUS.md, commit `chore(status): start US-XXX`
4. Create branch `feature/US-XXX` (via `mcp__github__create_branch` or `git checkout -b`)
5. Launch **three sub-agents in parallel in a single message**:
   - `@backend-dev` → `src/backend/` only (NO git commands)
   - `@frontend-dev` → `src/frontend/` only (NO git commands)
   - `@qa-engineer` → `test-cases/{module}/` only (NO git commands)
6. After all three return: `dotnet build src/backend/HRM.sln`, `ng build` in `src/frontend/`, `dotnet test`
7. Single combined commit: `feat(US-XXX): <story title>` with BE/FE/QA summary in body
8. Push branch + open PR via GitHub MCP
9. Switch to `main`, flip STATUS.md `[~]` → `[x]`, commit `chore(status): mark US-XXX done`, push
10. Print PR URL and stop. Do NOT loop — one story per invocation.

## Stop conditions

- All `[ ]` exhausted in scope → report "all stories in scope are done" and exit cleanly
- `dotnet build` or `ng build` fails → abort, leave branch local, flip STATUS back to `[ ]`, print errors
- Working tree dirty at start → abort, ask user to commit/stash first
- Sub-agent reports blocker → mark STATUS as `[~] blocked: <reason>` (with note), do not push

Use TodoWrite to track the 10 steps as you execute.
