---
id: TC-LV-160
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: automated
exec_note: "2026-07-05 ISSUE-041 fixed: ProcessLeaveYearEndJob + ProcessCarryForwardExpiryJob now wrap the per-tenant unit of work in a Polly WaitAndRetryAsync policy (retryCount 3, exponential 2^n s backoff, onRetry Serilog warning). The retry-fires behaviour is bound to automated regression -> HRM.Tests.Unit.LeaveYearEndJobRetryTests.{YearEndJob,CarryForwardExpiryJob}_RetriesTransientTenantFailure_ISSUE041 (transient fault clears on the 3rd attempt; asserts the per-tenant call is invoked 3x, i.e. retried). RESIDUAL manual/blocked: full max-3 exhaustion + Serilog log-line inspection + persistent-failure-continues-batch over the param-less GLOBAL job remains barred by the no-cross-tenant-write policy (RunAsync iterates all active tenants; no per-tenant trigger exists)."
created: 2026-06-14
---

# TC-LV-160: Job failures are logged (Serilog) and retried (Polly, max 3, exponential backoff) (NFR-4)

## 1. Test Objective
Verify reliability of the carry-forward/expiry jobs: a transient failure during processing is logged via Serilog with tenant context and retried via Polly up to 3 times with exponential backoff; a persistent failure surfaces after retries are exhausted without silently corrupting balances (NFR-4).

> **Regression automation (ISSUE-041):** `HRM.Tests.Unit.LeaveYearEndJobRetryTests` drives each job's public `RunAsync` against a real DI scope whose `ILeaveCarryForwardService` throws on its first two invocations then succeeds, asserting the per-tenant call is invoked 3 times — i.e. the Polly retry actually fires (pre-fix it was caught-and-skipped after one throw). Covers Step 1 (transient-clears-on-retry). Steps 2-4 (Serilog inspection, persistent-failure exhaustion, no-partial-corruption) remain the residual manual scenario noted above.

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-4
- Cross-reference: Resilient retry/circuit-breaker (Polly) and Serilog tenant-scoped logging (project infrastructure).

## 3. Preconditions
- Tenant "acme"; a year-end fixture for which a downstream operation can be faulted (e.g. a transient DB/timeout fault injected for one employee batch).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Max retries | 3 | Polly |
| Backoff | exponential | NFR-4 |
| Fault | transient (clears on retry 2) | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inject a transient fault that clears after the 2nd attempt; run the job | Polly retries with exponential backoff; the operation succeeds on retry, and the batch completes correctly. |
| 2 | Inspect logs | Serilog records the failure(s) and retry attempts with `TenantId`/`TenantSubdomain` in the log context (NFR-4). |
| 3 | Inject a persistent fault (never clears); run the job | After 3 retries the attempt is abandoned for that unit; the failure is logged at error level; processing of unaffected employees is not silently dropped. |
| 4 | Verify no partial corruption | A failed employee/batch does not leave a half-written carry_forward without its paired expired (transactional/idempotent boundary), so a later re-run (TC-LV-153) can complete it cleanly. |

## 6. Postconditions
- Transient failures self-heal via retry; persistent failures are logged and surfaced; balances remain consistent for a safe re-run.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
