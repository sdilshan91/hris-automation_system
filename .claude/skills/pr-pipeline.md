---
name: pr-pipeline
description: The autonomous commit → push → PR → merge pipeline, and the merge gate that bounds it. Agents may carry a loop or long task all the way to a merged PR without asking, EXCEPT where this gate holds the PR open for a human. Use whenever a loop driver (/implement-all, /fix-finding, /campaign) reaches a green verify gate and is deciding whether to merge.
user_invocable: true
---

# PR pipeline (autonomous) and the merge gate

> Authorized by the user on **2026-09-01**, replacing the previous "PRs are opened, not
> auto-merged" rule. Encodes Engineering-Discipline rule #7.

## What is authorized

An agent running a loop or long task may **commit, push, open a PR, and merge it** without
asking each time. No per-PR permission request. The point is that `/loop /implement-all` and
`/loop /fix-finding` complete end-to-end instead of stacking unreviewed branches.

## The merge gate

Merge **only** when all three hold. Any miss → **leave the PR open**, say why in the turn
summary, and move to the next queue item. A held PR is a normal outcome, not a failure.

**1. Verify gate green.**
`dotnet build` → `bash scripts/run-backend-tests.sh` (never raw `dotnet test` — ISSUE-312)
→ `npm run build` → `ng test` headless. A test may **never** be weakened, skipped or deleted
to reach green; that is what the 3-attempt remediation cap is for.

**2. No CRIT/HIGH from the three audit agents.**
`@integration-enforcer` (is it actually wired?) · `@test-authenticator` (is the test real?) ·
`/security-audit` (tenant isolation, authz, injection, secrets, PII). MED/LOW are filed as
findings and do not block.

**3. The diff touches no high-blast-radius path.** These stay open for a human **however
green the gates are**:

| Held path | Why |
|---|---|
| `**/Migrations/**`, any new EF migration | Least reversible thing in the repo; applied automatically on startup by `DbInitializer` |
| Auth/JWT — `**/Auth*`, `JwtService`, token/refresh handling | A wrong merge here is an authentication bypass |
| Tenant isolation — `TenantInterceptor`, `TenantResolutionMiddleware`, `AppDbContext.OnModelCreating` query filters, `ITenantContext` | Critical Rule #1; a cross-tenant leak is the worst outcome this platform has |
| `.github/**`, `.claude/hooks/**`, `.claude/settings.json`, `scripts/run-backend-tests.sh` | These are the brakes. Never let the loop merge a change to its own brakes |

Detection is on the **diff**, not the story description: `git diff --name-only origin/main...HEAD`.

## Merge mechanics

- **Squash-merge** into `main`, keeping the `feat(US-XXX)` / `fix(BUG-XXX)` subject.
- **Rebase on fresh `origin/main` first** (rule #8) and re-run the verify gate if the rebase
  moved anything — a gate green against a stale base proves nothing.
- Delete the branch after merge.
- Never `--force`, never `--no-verify` (the `no-verify-guard` hook denies it anyway).

## Closing findings

With the pipeline autonomous, `/verify-fix` may flip a finding to `FIXED`/`VERIFIED` on its
own **when the re-run TC evidence is green** — that is an evidence-backed transition, not a
judgment call. **`WONTFIX` stays human-only**: it is a product decision, and an agent
retiring its own inconvenient finding is exactly the failure mode the report-only boundary
exists to prevent. `/test-all` and `@test-runner` remain fully report-only regardless.

## When the loop is blocked

A genuine doubt does **not** halt the queue. File a `DECISION` finding, park that item at the
decision-gate, re-sort, continue with the next unblocked item, and report every parked
question in the turn summary. Never guess in the dark to keep moving.
