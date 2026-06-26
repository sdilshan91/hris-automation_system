---
name: error-recovery
description: Detects when fix attempts are stuck in a loop and forces escalation before wasted effort compounds. Maintains a failure counter, escalates at 2/3/4 attempts, and blocks unbounded retries. Use when the same error survives 2+ fixes, a fix introduces a new failure, or complexity is rising without progress.
user_invocable: true
---

# Error Recovery (stuck-loop breaker)

> Adapted for this repo from the GodMode `error-recovery` skill (MIT). Maps onto this project's existing
> `/implement-all` remediation loop (≤3 attempts, then revert + stop) and the `test-integrity-guard` hook.

## Prime Directive

```
NO CONTINUED ATTEMPTS WITHOUT ACKNOWLEDGING THE FAILURE COUNT.
```

Before every fix attempt, state: **"This is attempt N for <issue>."** If N ≥ 3, you are not authorized to
continue without stepping back (and, in an unattended loop, without reverting). AI agents get stuck: they
retry the same approach, add complexity to fix complexity, and rationalize "one more attempt" long past
diminishing returns. This skill makes that visible and forces recovery.

## How this fits the existing loops
- **`/implement-all` remediation loop** already caps at **3 attempts** then reverts the story to `[ ]` and
  stops without a PR. This skill is the per-attempt discipline *inside* that cap — it stops you from
  wasting all three attempts on variations of one wrong idea, and it forbids the thing the loop forbids:
  weakening/skipping a test to go green.
- **`/test-all` is report-only** — there is no fix loop there at all. If you find yourself "fixing" during
  a test run, that itself is the stuck pattern: stop, log the finding, move on.

## When it activates (proactively)
- Same error after 2+ attempted fixes.
- A fix for problem A introduces problem B.
- Same file edited 3+ times for one issue without resolution.
- Line count / complexity rising with each attempt.
- Build/test results getting *worse*, not better.
- You're about to say "let me try one more thing" after 2+ failures.

## Failure counter (maintain per distinct issue)
```
FAILURE LOG — <issue>
  Attempt 1: <what you tried> → <result>
  Attempt 2: <what you tried> → <result>
  Attempt 3: <what you tried> → <result>   ← STOP
```
A *variation* of the same approach counts as a new attempt. Reverting and retrying counts. The counter
resets only when the issue is resolved or scope is explicitly changed.

## Severity levels
- **Yellow — 2 failures:** Log it: "Two attempts failed for <issue>." Identify what both had in common.
  Your next attempt MUST be a **fundamentally different** approach (config→code, patch→replace, add→remove).
  If you can't name a different approach, go straight to Orange.
- **Orange — 3 failures:** **STOP fixing.** Re-read the original error from scratch (see `fault-diagnosis`
  Phase 1). List what you *know*, what you *assumed*, and what you have *not verified*. Form a hypothesis
  that contradicts your prior assumption. Present the revised analysis. In `/implement-all`, this is the
  point the loop reverts and stops — don't burn attempt #4.
- **Red — 4+ failures:** **HALT. Do not continue without explicit human direction.** Present an honest
  assessment: every attempt (briefly), what each failure taught you, what you now think the real problem
  is, and your recommendation — including "I may not be able to solve this cleanly." Wait.

## Recovery strategies (apply in order)
1. **Re-read the literal error** — not your interpretation. For backend, pull the real stack/SQL from
   `src/backend/HRM.Api/Logs/hrm-<YYYYMMDD>.log` by `RequestId` (don't trust the HTTP body).
2. **Fundamentally different approach** — not a tweak.
3. **Simplify ruthlessly** — strip to the smallest reproducible case; remove moving parts.
4. **Verify assumptions** — confirm the file/handler you think runs actually runs; print values, check the
   resolved tenant context, check whether the test hits InMemory vs real Postgres.
5. **Rollback to last known-good** — if you've made it worse, `git restore` and start fresh from there.
6. **Escalate with honesty** — "I tried N approaches; none worked; here's what I know and recommend."

## Anti-patterns to block
| Anti-pattern | Do instead |
|---|---|
| "Let me try one more thing" (after 3+) | STOP — you said that last time. Escalate. |
| Adding complexity to fix complexity | Simplify. Remove code. |
| Random changes, ignoring the error text | Read the error; it's specific. |
| "Must be a cache/env issue" | Verify — clean build, read the log. |
| Widening scope instead of narrowing | Focus on the smallest failing case. |
| Editing the same file repeatedly | Step back — the cause may be elsewhere. |
| **Fixing the test instead of the code** | The test is usually right; fix what it tests. The `test-integrity-guard` hook will block skip/`.only`/deletion anyway. |

## Cognitive traps
| Rationalization | Reality |
|---|---|
| "This is a different issue" | If it appeared while fixing the original, count it. |
| "I almost had it last time" | Two near-misses is a pattern, not progress. |
| "The approach is right, just needs tweaking" | Three tweaks of one approach ≠ three attempts. |
| "Let me refactor first, then fix" | Refactoring while debugging creates two problems. Fix first. |
| "One more log statement will reveal it" | If three didn't, you're looking in the wrong place — re-trace. |

## Connections
- **`fault-diagnosis`** — recovery activates when diagnosis isn't converging (Phase 4 failed 3+ times).
- The **`/implement-all` remediation loop** is the enclosing budget; this skill governs each attempt within it.
- After recovery, prove the fix with **fresh verification output** (the verify gate / completion discipline) —
  never a "should work."
