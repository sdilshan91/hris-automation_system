---
name: test-all
description: Loop driver that picks the next untested user story from test-cases/TEST-STATUS.md, executes its test cases against the running stack, and LOGS bugs/issues/enhancements (severity, status, root cause, repro). REPORT-ONLY — never fixes. One story per call; rerun (or /loop) to continue.
user_invocable: true
---

# Test-All: Story-by-Story Test-Execution Loop (report-only)

Drives **test execution** one user story at a time, sourced from `test-cases/TEST-STATUS.md`. The testing
counterpart to `/implement-all`. **It finds and documents defects; it NEVER fixes them.** Fixing is a
separate step you decide on after reviewing the findings ledger.

## Usage

```
/test-all                  # next untested story across all modules in priority order
/test-all admin-console     # restrict to one module
/test-all US-CHR-007        # test one specific story by ID (overrides scope)
```

Module keys + the per-US checklist live in `test-cases/TEST-STATUS.md`.

## What this skill does in ONE invocation

One full story per call — it does **not** loop forever. Rerun (or wrap in `/loop`) to continue. This pacing
lets you review the findings ledger between stories.

```
1. Read test-cases/TEST-STATUS.md
2. Resolve scope (module arg, US-ID arg, or full priority order)
3. Pick the FIRST `[ ]` (not-tested) story in that scope
   - If none → report "all stories in scope are tested" and exit
4. Pre-flight the running stack (API :5000, FE :4200). If down → STOP, tell the user to start it.
   (Do NOT fabricate verdicts. Note Docker state for Testcontainers-backed tests.)
5. Mark the story `[~]` (testing) in TEST-STATUS.md
6. Run the /test-us flow for it via @test-runner (execute every bound TC; record PASS/FAIL/BLOCKED;
   flip each TC `status:`; append every defect to test-cases/TEST-FINDINGS.md with the full schema).
   REPORT-ONLY — the agent must not edit src/, must not fix, must not open a PR.
7. Flip the tracker based on outcome:
     - all TCs PASS, no findings          → `[x]` tested-clean
     - executed but ≥1 finding logged     → `[!]` tested-with-findings (append finding IDs in the note)
     - could not execute (stack/dep/data) → `[b]` blocked (note the reason)
8. Print: per-TC verdict summary, new finding IDs (severity), and "run /test-all again for the next story."
```

There is **NO remediation loop**. A failing test produces a *finding*, not a fix attempt.

## Argument parsing

```
arg pattern        → behavior
──────────────────────────────────────────────────────
(none)             → first [ ] story across all modules in priority order
US-{MOD}-{NNN}     → that exact story (even if already tested; warn before re-testing)
{module-key}       → first [ ] story in that module
```

## TEST-STATUS.md state machine

```
[ ] not-tested ──/test-all──► [~] testing ──@test-runner done──► outcome
   ▲                                                              │
   │                                  all PASS, 0 findings ──────►│ [x] tested-clean
   │                                  executed, ≥1 finding  ──────►│ [!] tested-findings
   │                                  cannot execute        ──────►│ [b] blocked
   └─────────── re-test (manual or after a fix lands elsewhere) ◄──┘
```

`[x]`/`[!]`/`[b]` all mean "this run is done." A human (or a future re-test run) moves a story back to `[ ]`
after a fix lands. The skill itself never closes a finding and never edits product code.

## Continuous mode

```
/loop /test-all              # re-fires after each story; stops when scope reports "all tested"
/loop /test-all payroll      # walk one module end-to-end
```

Use when you want an unattended sweep that accumulates a findings ledger for you to triage afterward.
Because nothing is auto-fixed and no PRs are opened, this is safe to run unattended — the worst case is a
longer ledger to review.

## Guardrails (non-negotiable)
- **REPORT-ONLY**: no `src/` edits, no test weakening/skipping, no branches/PRs, no remediation loop.
- Findings are logged `OPEN`; the skill never sets downstream fix states or fixes the cause.
- A blocked TC/story keeps a `blocked:` reason; never invent a pass.
- Stack must be running; if not, stop cleanly rather than guessing.
