---
name: verify-fix
description: Close out a merged fix — re-run the finding's affected test cases against the running stack, flip TEST-STATUS.md, and mark the finding RESOLVED in TEST-FINDINGS.md with the PR#. The authorized close-out step for the human-decided fix cycle. Writes only to docs/QA/.
user_invocable: true
---

# Verify a Fix (close-out)

Runs AFTER a `/fix-finding` PR has **merged**. It re-executes the test cases the finding blocked/failed,
updates the trackers, and — on green — marks the finding `RESOLVED`. This is the **only** authorized path
that closes a finding: `@test-runner`/`/test-us`/`/test-all` are report-only and never set downstream fix
states. `/verify-fix` performs the `RESOLVED` transition itself because it is the human-decided fix cycle,
not the test loop — so the rule "the testing loop never closes its own findings" still holds.

## Usage

```
/verify-fix BUG-093            # re-run the finding's affected TCs, close if green
/verify-fix BUG-003 --iso      # cross-module ISO-suite re-run (for a systemic isolation fix)
```

## Process

1. **Validate input** — ID matches `(BUG|ISSUE)-\d+`, exists in `TEST-FINDINGS.md`, and its fix PR has
   **merged to `main`** (confirm via `mcp__github__pull_request_read` / `git log`). If not merged → stop:
   "fix not merged yet." Pull `main` fresh.
2. **Pre-flight the stack** — API `http://localhost:5000` and FE `http://localhost:4200` respond; note
   Docker (for Testcontainers) and Redis. If down, STOP and say how to start it — never fabricate a pass.
3. **Gather scope:**
   - default: the TCs listed on the finding + every `docs/QA/**/TC-*.md` whose `status:` is
     `fail`/`blocked` and references this finding ID.
   - `--iso`: the **full cross-module isolation suite** (all `TC-*-ISO-*` / tenant-isolation arms) — use
     for systemic fixes like BUG-003 where the invariant touches every module.
4. **Re-open for re-test** — flip the affected `TEST-STATUS.md` rows and TC `status:` back so they get
   re-executed (the report-only runner only re-runs `[ ]`/`draft`; a stale `[x]` would be skipped).
5. **Dispatch `@test-runner` in verification mode** — pass the explicit TC list (or ISO scope), the
   stack URLs, and personas:
   ```
   VERIFICATION RE-RUN of {ID}'s fix. REPORT-ONLY: do NOT edit src/, do NOT fix, do NOT open a PR.
   Execute ONLY these TCs: {list | ISO-suite}. Pick the layer by each TC's test-type. Record PASS/FAIL/
   BLOCKED, flip each TC status: and its TEST-STATUS row. If a TC that this fix targeted still FAILS,
   append the re-test evidence to the finding (do NOT close it). You only ever set findings to OPEN —
   never RESOLVED. Return a per-TC verdict table.
   ```
6. **Close-out (this skill, not the runner):**
   - **all targeted TCs green** → confirm `TEST-STATUS.md` shows `[x]`/`[!]`, then edit `TEST-FINDINGS.md`:
     set the finding `Status: RESOLVED` with the fixing **PR#** and the verifying date. If a regression TC
     was added, note its ID.
   - **any targeted TC still red** → leave the finding `OPEN`, append the re-test evidence, and report that
     the fix did not clear it (candidate for a fresh `/fix-finding` pass).
7. **Return** — per-TC verdict table, the finding's new status, and the ledger paths.

## Guardrails (non-negotiable)
- **Writes only to `docs/QA/`** — `TEST-STATUS.md`, `TEST-FINDINGS.md`, and executed TC `status:`
  frontmatter. Never edits `src/` or `docs/BA/`.
- **Never edits a test's logic** — no changes to `*.spec.ts` / `*Tests.cs` bodies or TC steps (the
  `test-integrity-guard` hook enforces this). A test that's wrong is a `TEST`-layer finding, not an edit.
- **Never fabricate a pass.** A blocked re-run stays blocked with a reason; only close a finding when its
  targeted TCs are genuinely green with evidence.
- `RESOLVED` is set by this skill only — `@test-runner` still writes `OPEN` exclusively.
