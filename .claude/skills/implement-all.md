---
name: implement-all
description: Loop driver that picks the next pending user story from user-stories/STATUS.md, implements it end-to-end (BE + FE + QA on one feature branch), opens a PR, and marks it done. Run once per story; rerun to continue.
user_invocable: true
---

# Implement-All: Story-by-Story Automation Loop

Drives the development pipeline one user story at a time, sourced from `user-stories/STATUS.md`.

## Usage

```
/implement-all                # picks next [ ] story across all modules in priority order
/implement-all auth           # restricts to authentication module
/implement-all core-hr        # restricts to core-hr module
/implement-all US-AUTH-005    # implements one specific story by ID (overrides scope)
```

Module keys are listed in the table at the bottom of `user-stories/STATUS.md`. Story IDs use the form `US-{MODULE}-{NNN}` (e.g. `US-CHR-007`).

## What this skill does in ONE invocation

It does NOT loop forever — it does **one full story per call**. Rerun the skill to do the next.
This pacing matches the "pause for review" PR strategy: each PR can be reviewed and merged before the next branch is cut from a fresh `main`.

```
1. Read user-stories/STATUS.md
2. Resolve scope (module arg, story ID arg, or full priority order)
3. Pick the FIRST `[ ]` story in that scope
   - If none → report "all stories in scope are done" and exit
4. Read the story file (acceptance criteria, FRs, NFRs)
5. Read docs/vault/modules/{module}.md and docs/vault/agents/*.md for prior context
6. Verify working tree is clean and on `main`, pulled fresh
7. Mark the story `[~]` in STATUS.md and commit `chore(status): start US-XXX`
8. Create branch `feature/US-{MODULE}-{NNN}` from current main
9. Launch THREE sub-agents IN PARALLEL on this same working tree
   (no isolation:worktree — they edit non-overlapping paths):
     - @backend-dev   → writes to src/backend/   (NO git commits)
     - @frontend-dev  → writes to src/frontend/  (NO git commits)
     - @qa-engineer   → writes to test-cases/    (NO git commits)
   Each agent receives an explicit instruction: "Implement only this one story.
   Do NOT run git add/commit/push — the orchestrator commits at the end."
10. After all three sub-agents return:
    a. Run `dotnet build src/backend/HRM.sln` — abort and report on failure
    b. Run `ng build` in src/frontend/ — abort and report on failure
    c. Run `dotnet test src/backend/HRM.sln --no-build` — report failures but continue
11. Stage and commit the combined changes:
       feat(US-XXX): {story title}

       Backend: {1-line summary}
       Frontend: {1-line summary}
       QA: {N test cases added}

       Closes: refs user-stories/{module}/US-XXX.md
12. Push branch via MCP (`mcp__github__push_files`) or `git push -u origin <branch>`
13. Open PR via MCP (`mcp__github__create_pull_request`) titled:
       "feat: US-XXX — {story title}"
    Body includes:
       - Link to the story file
       - Acceptance criteria checklist
       - Test case IDs added
       - Build/test results from step 10
14. Switch back to `main`. Mark STATUS.md `[~]` → `[x]` for the story,
    update the Tally counters, commit `chore(status): mark US-XXX done`, push to main.
    (The status flip lives on main so future runs see the latest state immediately,
    even before the PR merges. The PR itself doesn't need to merge to unblock the next story.)
15. Print: "PR #N opened for US-XXX. Review and merge, then run /implement-all again."
```

## Argument parsing

```
arg pattern             → behavior
─────────────────────────────────────────────────────────
(none)                  → first [ ] story across all modules in priority order
US-{MOD}-{NNN}          → that exact story (even if [x]; warn and ask before redoing)
{module-key}            → first [ ] story in that module (see STATUS.md table)
{module-key} {count}    → loop up to N stories in that module sequentially
                          (only use if user explicitly wants batching; each story
                           still gets its own branch+PR)
```

## Sub-agent prompts (templates)

Each agent gets a self-contained prompt with: the story ID, the absolute path to the story file, the explicit "do not commit" rule, and a pointer to the vault.

**Backend prompt template:**
```
You are implementing exactly ONE user story end-to-end on the backend.

Story: US-{ID} — read it at user-stories/{module}/US-{ID}.md
Vault context: check docs/vault/modules/{module}.md and docs/vault/agents/backend-dev.md

Implement in src/backend/ following CLAUDE.md, dev-instructions.md, and your agent guide:
  - Domain entities, EF configs, migrations (if schema changes)
  - MediatR command/query handlers
  - FluentValidation validators
  - API controller endpoints
  - Unit tests in HRM.Tests/Unit (≥70% coverage for this feature)
  - Integration tests in HRM.Tests/Integration (Testcontainers)

IMPORTANT — DO NOT:
  - Run `git add`, `git commit`, or `git push`
  - Create branches or open PRs (the orchestrator does this)
  - Touch src/frontend/ or test-cases/
  - Modify other stories' code

Verify with `dotnet build src/backend/HRM.sln` before reporting back.
Report a 5-bullet summary of files created/modified.
```

**Frontend prompt template:**
```
You are implementing exactly ONE user story end-to-end on the frontend.

Story: US-{ID} — read it at user-stories/{module}/US-{ID}.md
Vault context: check docs/vault/modules/{module}.md and docs/vault/agents/frontend-dev.md

Implement in src/frontend/ following CLAUDE.md, dev-instructions.md, and your agent guide:
  - Standalone components, signals, OnPush change detection
  - Reactive forms with validators
  - Service that calls the backend API for this story
  - Routing entry (lazy loaded)
  - Unit tests (Jasmine + Karma, ≥70% coverage)

UI must match the Notion-inspired design language from the agent guide
(Angular Material + Tailwind, NO Bootstrap). Mobile-responsive 360px+.

IMPORTANT — DO NOT:
  - Run `git add`, `git commit`, or `git push`
  - Create branches or open PRs
  - Touch src/backend/ or test-cases/
  - Modify other stories' code

Verify with `ng build` (in src/frontend/) before reporting back.
Report a 5-bullet summary of files created/modified.
```

**QA prompt template:**
```
You are writing IEEE 829 test cases for exactly ONE user story.

Story: US-{ID} — read it at user-stories/{module}/US-{ID}.md
Vault context: check docs/vault/modules/{module}.md and docs/vault/agents/qa-engineer.md

Write test cases in test-cases/{module}/ following your agent template:
  - 1+ happy path
  - 2+ negative tests
  - 1+ boundary test
  - Security tests (authz, multi-tenant isolation)
  - Performance test if the story has NFR latency/throughput
  - Accessibility test if the story has UI

Update test-cases/{module}/TEST-MATRIX.md and root TRACEABILITY-MATRIX.md
to link the new TCs back to US-{ID} and its acceptance criteria.

IMPORTANT — DO NOT:
  - Run `git add`, `git commit`, or `git push`
  - Create branches or open PRs
  - Touch src/backend/ or src/frontend/
  - Modify other modules' test cases

Report the list of TC-IDs you created and which ACs each covers.
```

## Failure modes & recovery

| Symptom | Action |
|---|---|
| `dotnet build` fails after agents finish | Print errors, leave branch local, mark STATUS.md back to `[ ]`, do NOT push or open PR. |
| `ng build` fails | Same as above. |
| Sub-agent reports "blocked — missing dependency" | Mark STATUS.md `[~]` with note `blocked: <reason>`, do NOT branch/push, report to user. |
| Working tree not clean at step 6 | Abort. Ask the user to commit/stash first. Never auto-stash. |
| Story file missing or malformed | Abort. Ask the user to fix the story first. |
| MCP PR creation fails | Branch is already pushed; print manual `gh pr create` command for the user to run. |

## Continuous mode (optional)

If the user wants to chain multiple stories in a single session without manual reruns:

```
/loop /implement-all auth
```

The built-in `/loop` skill will re-fire `/implement-all auth` after each completion. Stops automatically when the module reports "all done". Use only when you trust the agents to run unattended and don't need to review each PR before the next branch is cut.

## State machine

```
[ ] pending  ──/implement-all──►  [~] in-progress  ──build passes──►  [x] done (PR open)
   ▲                                  │
   └────────── build/agent failure ◄──┘
```

The STATUS.md flip happens BEFORE the PR is merged, intentionally — the next `/implement-all` invocation should pick the next story even if you haven't merged the prior PR yet, otherwise the loop stalls. Branches stack on top of each other; if there's a dependency, the agent for story N+1 can read the un-merged code on `feature/US-XXX-N` and rebase if needed.
