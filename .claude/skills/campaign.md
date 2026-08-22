---
name: campaign
description: Batch driver for a large, homogeneous, mechanical backlog — N call-sites, N files, or N findings of the SAME class (e.g. 657 hand-written interfaces to migrate, 187 a11y violations). Surveys first, pilots one batch, then works module-by-module with one PR per batch. The campaign-scoped sibling of /implement-all (story-scoped) and /fix-finding (one finding).
user_invocable: true
---

# Campaign: batch-driven mechanical backlog

## Why this exists

`/implement-all` is **story-scoped**. `/fix-finding` is **one-finding-scoped** and spends a whole
branch, PR, regression TC and three audit gates on that one finding. Neither fits a backlog that is
large, homogeneous, and mechanical:

- **657 hand-written interfaces** across 83 `*.models.ts` files to migrate to generated types
  (`docs/QA/plans/COMPLETION-PLAN.md`).
- **187 WCAG violations** from ISSUE-389, where `click-events-have-key-events` and
  `interactive-supports-focus` co-occur on the same element 60+ times and are ONE fix.

Running `/fix-finding` 187 times is absurd. Doing all 187 in one PR is unreviewable. This skill is
the shape in between: **batch, verify, ship, repeat.**

## The rule this skill exists to enforce

> **Survey before you script. A backlog that looks uniform usually is not.**

This is not a general principle, it is *this repo's* scar tissue. **BUG-310** records a scripted
5-site migration that failed because it assumed a uniform shape that did not exist: five sites had a
`Result`-style failure channel and were mechanical; four returned a bare `int`/`long`/`void` and each
needed a **product decision** before any edit was correct. The script could not tell the difference,
so it produced wrong code for four of nine sites.

So: **Phase 1 is a survey, and it is not optional.** A campaign that skips it is a scripted migration
with extra ceremony.

---

## Usage

```
/campaign <name> [--dry-run] [--batch <module>] [--resume]
```

- `<name>` — a campaign defined in `docs/QA/plans/campaigns/{name}.md`. If the file does not exist,
  Phase 0 creates it and **stops for human review** — you do not get to invent the scope and execute
  it in the same breath.
- `--dry-run` — run Phases 0–2 (survey + pilot plan) and stop. No edits.
- `--batch <module>` — run exactly one batch, then stop.
- `--resume` — continue an in-flight campaign from its ledger.

Requires a clean working tree. Branches from the current trunk, **not** blindly from `main` —
confirm the trunk first (`git rev-parse --abbrev-ref HEAD` on a fresh clone is not authoritative here).

---

## Phase 0 — Define and FREEZE the work-list

Write `docs/QA/plans/campaigns/{name}.md` containing:

| Field | Meaning |
|---|---|
| **Goal** | One sentence. What is true when this is done. |
| **Detection command** | The **exact** shell command that enumerates the work-list. Must be re-runnable and deterministic. |
| **Baseline count** | What that command returns today. Recorded so progress is measured, not felt. |
| **Fix recipe** | The transformation, stated precisely enough that two people would produce the same diff. |
| **Out of scope** | What looks similar but is deliberately excluded, and why. |
| **Done means** | Detection command returns 0 — *or* returns only entries on the parked list. |

**The work-list is frozen at Phase 0.** New instances discovered mid-campaign do NOT get absorbed
silently — they are appended to the campaign file with a dated line. A campaign whose scope grows
while it runs is a campaign that never ends, and this repo already has a `COMPLETION-PLAN` that
"changes every time reality does" to absorb genuinely new work.

**Stop here and get a human sign-off** the first time a campaign file is created.

---

## Phase 1 — Survey (MANDATORY — this is the BUG-310 guard)

Take a **stratified sample** of the work-list — at minimum 10 items or 10%, whichever is larger, drawn
from *different* modules — and classify each:

- **MECHANICAL** — the fix recipe applies as written, no judgement required.
- **HETEROGENEOUS** — looks like the class but the recipe does not fit as written (different return
  shape, different failure channel, different ownership).
- **DECISION-REQUIRED** — a correct fix depends on a product or architectural answer nobody has given.

Write the classification into the campaign file **with the per-item evidence**, then:

> **If more than ~20% of the sample is not MECHANICAL, STOP and report.** The backlog is not one
> campaign; it is two or more, and running it as one will produce BUG-310 again. Propose the split.

`DECISION-REQUIRED` items are **parked**, never guessed. Park them into
`docs/QA/DEFERRED-FOLLOWUPS.md` with the question that needs answering, and exclude them from
"done means".

---

## Phase 2 — Pilot ONE batch, end to end

Pick the **smallest** module in the work-list. Run the full cycle on it alone: edit → verify gate →
PR. Do not start batch 2 until the pilot PR is reviewed.

The pilot is where the fix recipe gets corrected while the blast radius is one module. Update the
campaign file's **Fix recipe** with whatever the pilot taught you, and say what changed.

---

## Phase 3 — Batch loop

For each remaining module, in ascending order of size:

1. Branch `campaign/{name}-{module}` from the trunk.
2. Apply the fix recipe to that module's items **only**. Do not touch items outside the batch, and do
   not opportunistically fix adjacent things you notice — flag them per
   [`/auto-heal`](auto-heal.md) instead.
3. **Verify gate** — `dotnet build src/backend/HRM.sln` →
   `bash scripts/run-backend-tests.sh src/backend/HRM.sln --no-build`
   (**never raw `dotnet test`** — ISSUE-312: it exits 0 on an ABORTED run) →
   `npm run build` → `ng test --watch=false` → `npm run lint` for FE campaigns.
4. Any failure enters the [`/error-recovery`](error-recovery.md) loop: **max 3 attempts**, verbatim
   errors handed back, whole gate re-run. **Never weaken, skip or delete a test to go green.**
5. On green: commit, push, open one PR titled `chore({name}): {module} — {n}/{total}`.
6. Re-run the **detection command** and record the new count in the campaign file.

**Stop the whole campaign on the first *unexplained* failure.** Not the first failure — an item that
turns out to be HETEROGENEOUS is expected, park it and continue. Unexplained means the gate went red
in a way the recipe does not account for. That is evidence the recipe is wrong, and every subsequent
batch would propagate the same error. This repo's own lesson (BUG-311): *when a mapper needs a cast
to compile, the cast is usually hiding the bug, not solving it* — the same applies to a batch that
needs a special case to go green.

---

## Phase 4 — Close out

The campaign is done when the detection command returns 0, or only parked items. Then:

- Final line in the campaign file: baseline → final, PRs, parked items with their open questions.
- If the campaign fixed a **finding**, do NOT close it here — that is [`/verify-fix`](verify-fix.md)'s
  job, the only skill authorised to mark a finding RESOLVED.
- Move the campaign file to `docs/QA/plans/campaigns/done/`.
- If a **guard** could prevent the class from coming back, propose one. 657 hand-written interfaces
  did not appear overnight; without a lint rule or a CI check, they will accrue again. A campaign that
  ends without a guard has bought time, not a fix.

---

## Boundaries

- **Edits `src/`.** This is not a report-only skill. It is subject to every rule that governs
  `/implement-all`: the test-integrity rule, the 3-attempt cap, the decision gate, and every
  `PreToolUse` guard.
- **Never closes a finding.** Ledgers are `/verify-fix`'s lane.
- **Never absorbs new scope silently.** Append, date, and say so.
- **One PR per batch, never one PR per campaign.** A 657-item diff cannot be reviewed, and an
  unreviewable PR is an unreviewed PR.
