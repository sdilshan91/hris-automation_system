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

## Keeping PRs mergeable — the merge queue (decided 2026-09-04)

Six PRs open at once produced **six cascading conflicts in one session**. Every single one was in
`TEST-FINDINGS.md`, `GAP-CLOSURE-QUEUE.md` or an agent `MEMORY.md`. **None was in `src/`.**
Each merge dirtied every other open PR, and the rebasing cost more time than the work.

### The rule: a GitHub merge queue on the working branch

GitHub serialises merges and rebases each entry itself, so a PR can never go stale between
"CI green" and "merged". **It must be configured with batching**, or it makes idle time worse rather
than better — this repo's backend gate runs ~30-60 minutes, so five sequential entries would be a
five-hour queue.

**Settings that matter** (Settings → Branches → rule for `test/local-subdomains` → Require merge queue):

| Setting | Value | Why |
|---|---|---|
| `grouping_strategy` | `ALLGREEN` | batch merges only if the whole batch is green |
| `max_entries_to_merge` | `5` | **five PRs cost ONE CI run, not five** — this is the setting that protects idle time |
| `max_entries_to_build` | `5` | build the batch together |
| `min_entries_to_merge_wait_minutes` | `5` | brief wait so entries actually accumulate into a batch |
| `check_response_timeout_minutes` | `120` | the backend gate has taken 60+ min under agent contention |
| `merge_method` | `SQUASH` | matches existing practice |

**This must be enabled through the web UI.** The `merge_queue` ruleset rule is rejected by the REST
API (`Invalid rule 'merge_queue'`, no field named, on every parameter variation) — verified
2026-09-04 on a public repo with admin rights, so it is not a plan or permission gate.

⚠ **Do NOT reach for the branch-protection API as a substitute.** It does not carry merge-queue
settings, and marking `Backend (build + test)` required **blocks every docs-only PR**, because that
job reports `skipping` on them. That was tried and reverted the same day.

### Until the queue is on — and worth doing anyway

1. **Sync the base into your branch before opening the PR.** Cheap, correct, and it removes
   conflicts that exist at creation time. It does **not** prevent the cascade, because these
   conflicts appear *after* opening when a sibling merges — so it is hygiene, not the fix.
2. **Prefer keeping ledger writes out of feature PRs.** A feature PR that touches only code and
   tests has almost nothing to collide on; bookkeeping batched into one docs-only PR merges in
   ~4 minutes instead of ~60. Every conflict in the 2026-09-02/03 session was bookkeeping colliding
   with bookkeeping.
3. **`merge=union` helps LOCALLY ONLY — GitHub does not honour it.** Corrected 2026-09-04 after the
   claim above was written and proved wrong the same day. Controlled A/B on this repo:

   | merge of the same two branches | result |
   |---|---|
   | local, `-c merge.union.driver=false` | **CONFLICT** in `TEST-FINDINGS.md` |
   | local, attribute honoured | **clean** |
   | GitHub (`mergeable`) | **`CONFLICTING`** |

   GitHub's merge machinery ignores `.gitattributes` merge drivers, so **every PR touching an
   append-only ledger still goes `DIRTY` behind a sibling merge and still cannot auto-merge.** What
   union actually buys is that the fix is a *trivial local rebase* (auto-resolved, nothing to hand-merge)
   instead of manual conflict surgery. That is worth having — it is not conflict prevention.

   **This also limits the merge queue.** The queue rebases entries with GitHub's machinery, so it will
   hit the same ledger conflicts. It solves the *code* cascade; it does **not** solve this one.

   ⚠ **The only thing that actually prevents ledger conflicts is not writing to a ledger from two open
   PRs at once.** Batch bookkeeping into one docs PR and merge it before opening the next. Every conflict
   in the 2026-09-02/04 sessions would have been prevented by that and by nothing else.

   `GAP-CLOSURE-QUEUE.md` stays excluded from union regardless — ticking an item rewrites an existing
   row, and union would silently duplicate it (which it did; see #605).

