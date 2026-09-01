---
name: fix-finding
description: Fix ONE finding (BUG-###/ISSUE-###) from docs/QA/TEST-FINDINGS.md end-to-end (BE/FE code + a regression TC + security/wiring review on one fix branch + PR). The finding-driven counterpart to /implement-story. Does NOT close the finding — run /verify-fix after the PR merges.
user_invocable: true
---

# Fix One Finding (finding-driven)

Implements the fix for a single **finding** from the report-only test ledger. This is the
finding-driven counterpart to `/implement-story` (which is story-driven and only accepts `US-###`).
It edits `src/`, adds a guarding regression test, and opens a PR — it is the **human-decided fix
process**, explicitly **outside** the report-only test loop (`/test-all`, `/test-us`, `@test-runner`).

> Fixing is deliberate: this skill exists because no existing driver accepts a `BUG-###`. See the
> remediation plan `docs/QA/plans/archive/BLOCKED-TC-REMEDIATION-PLAN-2026-07-02.md` (§4b) for why.

## Usage

```
/fix-finding BUG-093            # fix one finding
/fix-finding ISSUE-227          # issues work the same way
```

One finding per call. (To fix a whole phase in order, call this once per finding, merging each PR
before the next — do not stack colliding branches.)

## Relationship to the other skills

- `/fix-finding` = fix a finding, open a PR. Edits `src/`; **never touches the ledgers.**
- `/verify-fix {ID}` = run AFTER the PR merges: re-runs the affected TCs, flips `TEST-STATUS.md`, and
  marks the finding `RESOLVED` in `TEST-FINDINGS.md`. That is the ONLY skill that closes a finding.
- `/implement-story` / `/implement-all` = story-driven (`US-###`); unrelated to findings.

## Process

1. **Validate input** — ID matches `(BUG|ISSUE)-\d+` and exists in `docs/QA/TEST-FINDINGS.md` with
   status `OPEN`. If it's `RESOLVED`/`WONTFIX` → stop. If the finding is tagged **ENV/DATA/DEFERRED**
   (persona gap, perf-harness, unbuilt feature) → stop: "not code-clearable, see plan §6."
2. **Read the finding** — pull its module, layer (BE/FE/DB), root cause + `file:line`, affected TCs,
   and reproduction. Read `docs/vault/modules/{module}.md` for prior context.
3. **Pre-flight** — working tree clean, on `main`, pulled fresh. Abort otherwise.
4. **Branch** — `fix/{ID}-{slug}` via `mcp__github__create_branch` (or `git checkout -b`).
5. **Parallel sub-agents** (one message, non-overlapping paths; no commits by sub-agents):
   - owning dev agent — `@backend-dev` (BE/DB/EF-migration/seed) or `@frontend-dev` (FE) — implement
     the fix at the cited root cause. **Migrations via `dotnet ef` only** (never hand-written); a data
     backfill goes in a migration `migrationBuilder.Sql(...)`, not an ad-hoc script.
   - `@qa-engineer` — add/strengthen a **regression TC that fails pre-fix and passes post-fix**
     (see plan §4c for the known coverage holes). This is mandatory — a fix without a guarding test
     is not done.
6. **Anti-theater audit** — `@test-authenticator` confirms the new regression TC actually exercises
   the bug (not a tautology / happy-path-only). If it flags theater, send it back to `@qa-engineer`.
7. **Verify gate** — `dotnet build src/backend/HRM.sln` →
   `bash scripts/run-backend-tests.sh src/backend/HRM.sln --no-build` (**never raw `dotnet test`** —
   ISSUE-312: it can exit 0 on an ABORTED run, so a partial run reads as a full pass)
   → (FE) `ng build` + `ng test --watch=false`. Any failure enters the `/error-recovery` remediation
   loop: **max 3 attempts**, hand the verbatim errors back to the owning agent, re-run the whole gate.
   **Never weaken/skip/delete a test to go green.** If it can't be fixed cleanly in 3 attempts, revert
   the branch and stop without a PR.
8. **Security / wiring review** (conditional):
   - security-relevant finding (tenant-isolation, authz, authn, secrets, PII — e.g. BUG-003, BUG-040,
     BUG-119) → run `/security-audit` on the branch diff; block the PR on any CRIT/HIGH it raises.
   - wiring-sensitive finding (new handler/DI/route, tenant query filter — e.g. BUG-003, ISSUE-188) →
     run `@integration-enforcer` to confirm the fix is actually wired, not orphaned.
9. **Commit** — single commit:
   ```
   fix({ID}): {finding title}

   Root cause: {one line + file:line}
   Fix: {summary}
   Regression test: {TC-ID added}

   Refs: docs/QA/TEST-FINDINGS.md ({ID})
   ```
10. **Push + PR** — `mcp__github__push_files` + `mcp__github__create_pull_request`. PR body links the
    finding, the affected TCs, the new regression TC, and any `/security-audit` verdict.
11. **Merge gate** — [pr-pipeline](pr-pipeline.md). Clear → squash-merge, then run `/verify-fix {ID}`
    to close the finding. Held (a fix for a tenant-isolation or auth finding usually IS held, by
    design) → leave the PR open and say why.
12. **Return** — print the PR URL and, if it was held, the reminder: **run `/verify-fix {ID}` after this merges** to close the
    finding and flip the tracker.

## Guardrails (non-negotiable)
- **Does NOT touch the ledgers** — no writes to `TEST-FINDINGS.md` / `TEST-STATUS.md`. Closing the
  finding is `/verify-fix`'s job, post-merge. (This keeps "who closes a finding" in exactly one place.)
- Never weaken/skip/delete a test (the `test-integrity-guard` hook enforces this).
- Secrets in `.env`/user-secrets only (the `secret-guard` hook enforces this).
- One finding per branch/PR. Merge before starting the next (loop-stacking gotcha).
- If the fix requires a spec/AC decision (e.g. ISSUE-223 soft-delete, ISSUE-227 precedence) that isn't
  settled in the US → stop and surface it; do not guess the intended behavior.
