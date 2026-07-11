---
name: fault-diagnosis
description: Root-cause-before-fix discipline for any bug, test failure, 500, flaky/order-dependent test, or unexpected behavior in the HRM stack. Trace backward to the source (Serilog/RequestId, EF/Postgres, Angular) instead of patching the symptom. Use BEFORE proposing any fix.
user_invocable: true
---

# Fault Diagnosis (HRM stack)

> Adapted for this repo from the GodMode `fault-diagnosis` skill (MIT). Stack-agnostic discipline,
> retargeted to ASP.NET Core 10 + EF/PostgreSQL + Angular 20 and this project's debug practices.

## Prime Directive

```
NO FIX WITHOUT ROOT-CAUSE INVESTIGATION FIRST.
```

Guessing at fixes wastes time and adds defects; quick patches mask the real problem. If you have not
completed Phase 1, you are not authorized to propose a fix. **Treating a symptom is failure.**

**Report-only boundary:** when running under `/test-all`, `/test-us`, or as `@test-runner`, diagnosis
ends at a **root-caused finding** (logged to `docs/QA/TEST-FINDINGS.md`). You do NOT fix — the fix is
a separate, human-decided step. Phases 1–3 still apply; Phase 4 becomes "write the finding," not "edit src/."

## When to use
Any bug, test failure, 500, flaky test, perf regression, build failure, or integration breakdown —
**especially** under time pressure, when "one quick fix" seems obvious, or when a previous fix didn't hold.
Don't skip because it "looks simple": simple bugs have root causes too, and systematic work is *faster*
than guess-and-check.

## Phase 1 — Root-cause investigation (before ANY fix)

1. **Read the error completely.** Stack trace, line numbers, error codes — they usually contain the answer.
   For a backend failure, **read the Serilog file** `src/backend/HRM.Api/Logs/hrm-<YYYYMMDD>.log` and
   **correlate by `RequestId`** to get the real exception/stack/SQL. In Development, `HRM.*` logs at Debug
   and EF Core SQL (`Microsoft.EntityFrameworkCore.Database.Command`) at Information. **Never infer a
   root cause from the HTTP response body when a log line exists.** (Run the backend WITHOUT a VS Code
   debugger attached — it breaks on first-chance `ValidationException` and fakes a hang.)
2. **Reproduce reliably.** Exact steps, every time? If not reproducible, gather more data — do not guess.
3. **Examine recent changes.** `git diff`, recent commits, new migrations/config, FE↔BE contract drift.
4. **Gather evidence at every component boundary.** This is a multi-layer system
   (`request → TenantResolutionMiddleware → auth → Controller → MediatR handler → AppDbContext → Postgres`).
   Instrument each boundary ONCE to see *where* it breaks before investigating *why*:
   ```
   - What tenant/context is resolved?  (ITenantContext.TenantId vs CurrentUser.TenantId — the BUG-003 split)
   - What does the handler receive vs return?
   - What SQL does EF actually emit?  (read it from the log, don't assume)
   - What row state exists?  (psql / a read endpoint)
   ```
   Pin which layer fails first, then dig there — don't fix the layer where the error merely *surfaced*.
5. **Trace data flow backward** (see "Backward tracing" below) until you reach the original source.

### Known root-cause classes in THIS codebase (check these first)
- **InMemory-masks-Postgres** — a behavior passes against the EF InMemory provider but throws on real
  Postgres (manual `BeginTransactionAsync` under `EnableRetryOnFailure` → `InvalidOperationException`;
  `string.Contains` over `jsonb` untranslatable; case-sensitivity). If a green unit test contradicts a
  live 500, suspect the provider gap. (This was BUG-068 and BUG-007.)
- **Tenant context split (BUG-003)** — authz reads `CurrentUser.TenantId` (token) while data/filters read
  `ITenantContext.TenantId` (subdomain-resolved). A spoofable `X-Tenant-Subdomain` with no token↔header
  guard makes them diverge. Root locus: `TenantResolutionMiddleware` / US-AUTH-007.
- **EF read-modify-write not atomic** — `count++` then SaveChanges races under concurrency (BUG-045).
- **UTC-vs-tenant-tz** — `TimeOnly.FromDateTime(UtcNow)` compared to a naive local shift time (ISSUE-065).
- **Missing audit_logs** — a requirement says "audited" but only Serilog fired; verify the `audit_logs`
  row, not the log line.

## Phase 2 — Pattern analysis
Find a **working example** of the same pattern in this codebase (a sibling Feature handler, an existing
entity config, a passing isolation test). List **every** difference between working and broken — "that
can't matter" is how root causes hide. Read any reference implementation completely, not skimmed.

## Phase 3 — Hypothesis & minimal test
State ONE hypothesis: *"I believe X is the root cause because Y."* Make the **smallest** change that tests
it — one variable at a time. Confirmed → Phase 4. Not confirmed → form a NEW hypothesis; do **not** pile
fixes on top. If you don't understand something, say so and investigate — don't pretend.

## Phase 4 — Fix the source (or, in report-only mode, file the finding)
1. **Failing test first** — simplest reproduction (xUnit for BE, Karma/Jasmine for FE). For a
   Postgres-only bug, the reproduction must hit **real Postgres** (Testcontainers), not InMemory, or it
   won't catch it. Never weaken/skip a test to go green (the `test-integrity-guard` hook enforces this).
2. **One fix at a time** at the root — no "while I'm here" refactors.
3. **Verify** with fresh output: the failing test passes, nothing else broke.
4. **If the fix fails:** STOP, count attempts. `<3` → back to Phase 1 with new info. **`≥3` → escalate to
   `error-recovery` and question the architecture** — repeated fixes each surfacing new coupling means a
   flawed design, not a failed hypothesis. Discuss before attempting fix #4.

## Backward tracing (when the error is deep in the stack)
Trace from the symptom up the call chain to the origin, then fix at the origin:
```
Symptom: 500 on POST /api/v1/recruitment/applicants/{id}/convert
  → immediate: ApplicantConversionService.ConvertAsync:154 BeginTransactionAsync throws
  → caller: convert command handler
  → why: DbContext configured with EnableRetryOnFailure (execution strategy) — manual tx incompatible
  → SOURCE: the retry-strategy + manual-transaction combination (fix here, not at the call site)
```
When manual tracing stalls, add **temporary** instrumentation *before* the dangerous operation (log the
inputs, tenant context, and `Environment.StackTrace`), run once, capture, then remove it. In tests, write
to stdout/`ITestOutputHelper` — a suppressed logger hides the evidence.

## Flaky / order-dependent tests (find the polluter)
If a test passes alone but fails in the suite (or vice-versa), a sibling test is leaking shared state
(DB rows in the shared `acme` tenant, a static, a left-open record). **Bisect by running tests in
isolation** and watching for the polluting side effect:
- **Backend (xUnit):** `dotnet test --filter "FullyQualifiedName~<Class>"` per suspect class; check whether
  the shared DB/state mutates. Real-Postgres integration tests must clean their own rows (this repo's
  recruitment surfaces have no hard-delete API — a test that seeds un-deletable rows IS a polluter).
- **Frontend (Karma):** `ng test --include='**/<one>.spec.ts' --watch=false --browsers=ChromeHeadless`
  per spec; a spec that mutates a shared service/singleton without resetting it pollutes the next.
Fix the **leak** (isolate/clean up state), not the assertion.

## Condition-based waiting (kill arbitrary sleeps)
Flaky timing usually comes from guessing a delay. **Wait for the actual condition**, not a fixed sleep:
- Playwright: prefer `browser_wait_for` on the real signal over a fixed timeout.
- Hangfire / async jobs (AutoClockOut, export, OT flag): poll the **resulting row/state** (`waitUntil` the
  job's effect is observable), don't `sleep N`. Always bound the poll with a timeout + clear message.
- An arbitrary timeout is only correct when testing genuine timed behavior (a debounce/throttle window) —
  then wait for the triggering condition first and comment WHY the fixed wait is justified.

## Cognitive traps (STOP — return to Phase 1)
| Rationalization | Truth |
|---|---|
| "Quick fix now, investigate later" | The first fix sets the pattern. Do it right from the start. |
| "It's probably X, let me fix that" | Seeing a symptom ≠ understanding the root cause. |
| "Emergency, no time for process" | Systematic diagnosis is faster than flailing. |
| "Fix the test, it's being annoying" | The test is usually right. Fix what it tests (and the guard will block weakening it). |
| "One more fix attempt" (after 2+) | 3+ failures = architectural problem → `error-recovery`. |
| "Must be a cache/env issue" | Verify it. Read the log. Run a clean build. |

## After resolution
- If it was a real defect, write a short post-mortem in `docs/vault/incidents/` (root cause, the trace,
  the fix, the guard that now prevents recurrence) — not as a code comment.
- In report-only runs, the equivalent output is the `TEST-FINDINGS.md` entry (root cause + confidence +
  repro + evidence), and the verdict in `TEST-STATUS.md`.

**95% of "no root cause found" cases are incomplete investigation.** Trace one more level before giving up.
