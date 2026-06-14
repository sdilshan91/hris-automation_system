---
id: TC-LV-160
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-160: Job failures are logged (Serilog) and retried (Polly, max 3, exponential backoff) (NFR-4)

## 1. Test Objective
Verify reliability of the carry-forward/expiry jobs: a transient failure during processing is logged via Serilog with tenant context and retried via Polly up to 3 times with exponential backoff; a persistent failure surfaces after retries are exhausted without silently corrupting balances (NFR-4).

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
