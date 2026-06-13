---
name: research-story
description: Feasibility / GO–NO-GO gate for ONE user story before it is coded. Reads the story, explores the current codebase + vault, and writes research/US-{ID}.md with a verdict. Run this before /implement-story or /implement-all when a story is large, risky, or architecturally unclear.
user_invocable: true
---

# Research a User Story (GO / NO-GO Gate)

A **lightweight feasibility gate** inspired by the RPI (Research → Plan → Implement)
workflow. It runs *before* implementation to catch non-viable, under-specified, or
architecture-breaking stories early — when a question is cheaper than a rewrite
(see the "Think before coding" rule in CLAUDE.md).

It does **not** write product code, branches, or PRs. Its only output is a research
document with a verdict.

## Usage

```
/research-story US-LV-002
/research-story US-CHR-009
```

## When to use it

- A story is **large, cross-cutting, or touches multi-tenancy / auth** (high blast radius).
- Acceptance criteria are **vague or look contradictory**.
- You're unsure it fits the **Clean Architecture + CQRS** layering or the EF/tenant model.
- Before a long unattended `/implement-all` run, to avoid burning the remediation loop
  on a story that was never feasible as written.

Small, well-specified CRUD stories don't need this — go straight to `/implement-story`.

## Process

1. **Validate input** — story ID matches `US-[A-Z]+-\d{3}` and the file exists at
   `user-stories/{module}/US-{ID}.md`. Abort with a clear message if not.
2. **Load context** (read-only — no code changes):
   - The story file (goal, acceptance criteria, NFRs).
   - `docs/vault/modules/{module}.md` and `docs/vault/decisions/` for prior domain rules / ADRs.
   - `user-stories/STATUS.md` for dependencies on not-yet-built stories.
3. **Explore the codebase** — use the `Explore` agent (or Grep/Glob) to map what already
   exists for this area: entities, CQRS handlers, controllers, EF configs, Angular
   features/services, and existing test cases. Identify what is reusable vs. net-new.
4. **Assess across four lenses** (keep each to a few bullet points — this is a gate, not a design doc):
   - **Requirements clarity** — are the AC testable and unambiguous? List any that aren't.
   - **Technical feasibility** — does it fit Clean Architecture + CQRS, EF/migrations, and
     the three-layer tenant isolation model? Name the concrete files/layers it will touch.
   - **Risks & blockers** — multi-tenancy leaks, missing dependencies, schema/migration
     impact, breaking changes to shared contracts, external integrations.
   - **Effort & approach** — rough size (S/M/L), recommended sequencing, and whether it
     should be split into smaller stories.
5. **Verdict** — one of:
   - **GO** — feasible and clear; proceed to `/implement-story` or `/implement-all`.
   - **GO-WITH-CONDITIONS** — proceed only after the listed conditions are met
     (e.g. "split into two stories", "confirm tenant-scoping of X", "depends on US-XXX merging first").
   - **NO-GO** — do not implement as written; state why and what must change first.
   Include a **confidence %** on the verdict.
6. **Open questions** — if anything is genuinely ambiguous, list the questions and
   **stop**; surface them to the user rather than guessing (CLAUDE.md advisor stance).
7. **Write the report** to `research/US-{ID}.md` (create `research/` if absent) using the
   template below, and print the verdict + report path.

## Output template (`research/US-{ID}.md`)

```markdown
# Research: US-{ID} — {story title}

- **Module:** {module}
- **Date:** {today}
- **Verdict:** GO | GO-WITH-CONDITIONS | NO-GO  (Confidence: {N}%)

## Requirements clarity
- ...

## Technical feasibility
- Touches: {layers/files}
- Reuses: {existing handlers/entities/components}
- Net-new: {...}

## Risks & blockers
- ...

## Effort & approach
- Size: S | M | L
- ...

## Conditions (if GO-WITH-CONDITIONS)
- [ ] ...

## Open questions
- ...

Refs: user-stories/{module}/US-{ID}.md
```

## Relationship to the other skills

- `/research-story` → **decide** whether/how to build (this skill).
- `/implement-story US-{ID}` → build one named story end-to-end.
- `/implement-all` → loop: pick next pending story and build it.

A NO-GO verdict means **do not** run `/implement-story` / `/implement-all` for that story
until the conditions are resolved.

## Does NOT

- Write or modify application code, branches, commits, or PRs.
- Touch `user-stories/STATUS.md`.
- Run the build/test verify gate (that's the implement skills' job).
