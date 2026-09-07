---
id: US-LV-013
module: Leave Management
priority: Should Have
persona: System Admin / Platform Operator (primary) · HR Officer (secondary, recovery)
status: draft
created: 2026-09-07
sprint: backlog
acceptance_criteria_count: 7
findings: ENH-001
---

# US-LV-013: On-Demand Leave Accrual / Recalculation Trigger

> **Traceability:** this is the **parked half** of [[ENH-001]]. The other half — an unfiled defect where 4 of
> 5 entitlement mutations enqueued no recalculation — was fixed in **PR #679** (`66168e95`,
> *"recalculate entitlements on every mutation, not just rule updates"*). What remains is the manual trigger
> the finding is actually titled after.

## 1. Description
**As a** platform operator or QA engineer verifying leave-accrual behaviour,
**I want to** invoke a tenant's accrual/recalculation run on demand through the API instead of waiting for
the daily recurring job,
**So that** accrual effects can be observed, verified and recovered within a test or support session rather
than on a 24-hour cycle.

## 2. Be honest about who this is for
**This is primarily a test-enablement and operator capability, not an end-user feature.** The BA contract
forbids inventing a persona, so this story does not dress it up as one.

[[ENH-001]]'s own title states the need in verification terms: *"all accrual-effect verification depend on
the daily recurring `LeaveAccrualJob`, which cannot be invoked via the API."* US-LV-002's test hints already
assume a capability that does not exist — *"Test Hangfire job: Trigger accrual job and verify ledger entries
are created correctly"* (`US-LV-002.md:89`). There is no way to do that today.

The secondary, genuinely operational case is **recovery**: when a run is missed (job storage outage, a
tenant onboarded after the daily fire, a corrected entitlement rule that must take effect before tomorrow),
an HR Officer or support engineer needs to force a run rather than tell the customer to wait a day.

Value should therefore be judged as *"unblocks a class of tests and removes a 24-hour support latency"* —
not as user-facing functionality. If that value does not justify the build, the honest outcome is to close
[[ENH-001]] as WONTFIX rather than to inflate the story.

## 3. Preconditions
- The Leave module is enabled for the tenant and leave types with entitlement rules exist (US-LV-001, US-LV-002).
- The caller holds `Leave.ConfigurePolicy`.
- Hangfire job storage is reachable.

## 4. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A tenant has employees with active entitlement rules and no accrual for the current period | An authorised caller POSTs the trigger endpoint | Accrual runs **for that tenant only**, `leave_ledger` accrual rows are created, and the response reports how many employees and ledger rows were processed |
| AC-2 | An accrual run has already been processed for the current period | The trigger is invoked again | The run is **idempotent** — no duplicate accrual ledger rows are created, and the response distinguishes "processed" from "already up to date". (Leave balances are money-adjacent; a double credit is a real defect, not a cosmetic one) |
| AC-3 | A caller in Tenant A invokes the trigger | The run executes | **Only Tenant A's** employees and ledger rows are touched. Tenant B's `leave_ledger` row count and balances are byte-identical before and after |
| AC-4 | A caller does not hold `Leave.ConfigurePolicy` | They invoke the trigger | **403**, and no accrual work is performed |
| AC-5 | A trigger run is in progress for a tenant | A second trigger arrives for the same tenant | The second request does not start a concurrent overlapping run — it is rejected (**409**) or coalesced onto the running one; it never produces interleaved double credits |
| AC-6 | A trigger is invoked | The run completes or fails | The invocation is **audited** — who triggered it, for which tenant, when, and the outcome. A manual mutation of leave balances must be attributable |
| AC-7 | A tenant has 5,000 employees | The trigger is invoked | The endpoint returns without blocking the HTTP request for the whole run (see OQ-1), and the caller can determine when the run has finished |

## 5. Functional Requirements (IEEE 830 §3.2)
- FR-1: The system SHALL expose an on-demand accrual/recalculation trigger endpoint on `LeaveEntitlementsController`, gated by `RequirePermission("Leave.ConfigurePolicy")` — the permission every one of that controller's existing endpoints already uses (`LeaveEntitlementsController.cs:37,55,73,102,129,147,171,193,216,238,280,323`).
- FR-2: The trigger SHALL execute **scoped to the calling tenant**. `LeaveAccrualJob.RunAsync` today iterates *all* active tenants; the on-demand path must not. The per-tenant accrual body needs to be reachable independently of the all-tenant loop.
- FR-3: The trigger SHALL reuse the existing accrual logic — including the per-tenant leave-year resolution `AccrualLeaveYearFor(today, fiscalYearStartMonth)` (`LeaveAccrualJob.cs:47`, ISSUE-305) — and SHALL NOT fork a second accrual implementation. Two accrual engines that can disagree is a worse outcome than no trigger.
- FR-4: The trigger SHALL be idempotent for an already-processed period (AC-2).
- FR-5: The trigger SHALL prevent overlapping concurrent runs for the same tenant (AC-5).
- FR-6: The trigger SHALL write an audit record of the invocation and its outcome (AC-6).
- FR-7: The response SHALL report a machine-readable summary: employees processed, ledger rows created, leave year accrued into, and whether the run was a no-op.

## 6. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: **Tenant isolation** — the run resolves its tenant from `ITenantContext`, never from a client-supplied tenant id. A cross-tenant trigger parameter SHALL NOT exist; this endpoint mutates balances, so a tenant id in the request body would be a privilege-escalation surface.
- NFR-2: **Background-job tenant context** — if the work is dispatched to Hangfire, the tenant must be carried explicitly into the job and re-established there (the platform convention already covered by `BackgroundJobTenantContextTests`); a job that runs without tenant context is exactly the untenanted path RLS exists to catch.
- NFR-3: The endpoint SHALL be safe to invoke repeatedly — this is a verification tool and will be called in loops by tests.
- NFR-4: The daily recurring `LeaveAccrualJob` SHALL continue to work unchanged; this story adds an entry point, it does not replace the schedule.

## 7. Business Rules
- BR-1: Only `Leave.ConfigurePolicy` holders may trigger an accrual run.
- BR-2: A manual run produces exactly the same ledger effects as the scheduled run for the same date — the trigger is an *invocation* mechanism, never an alternative *calculation*.
- BR-3: A manual run never double-credits an already-accrued period.
- BR-4: A manual run is always attributable to the user who triggered it.
- BR-5: The trigger operates only on the caller's own tenant.

## 8. Data Requirements
- **Input:** none beyond the authenticated tenant context. Optional `asOfDate` is deferred to OQ-2.
- **Output:** run summary — `employeesProcessed`, `ledgerRowsCreated`, `leaveYear`, `wasNoOp`, and (if async) a run/job identifier.
- **Storage:** existing `leave_ledger` rows (transaction type `accrual`); an audit row for the invocation. No new tables.

## 9. UI/UX Notes
- **Deliberately API-first.** Given the primary persona is an operator/test harness, a UI is not required for
  the story to deliver its value. If a surface is wanted, the natural home is a "Recalculate accruals" action
  on the leave-entitlement admin page with a confirmation dialog and the run summary shown afterwards —
  proposed, not required.

## 10. Dependencies
- **US-LV-002** — owns entitlements, the accrual job (FR-5) and the ledger this writes to; its test hint at line 89 is what this unblocks.
- **US-LV-008** — carry-forward/expiry shares the ledger and the leave-year boundary.
- **[[ENH-001]]** — the enqueue half is already fixed (PR #679); this is the remainder.

## 11. Open Questions — MUST be answered before this story is `ready`
- **OQ-1 (synchronous or enqueued).** Does the endpoint run accrual inline and return the summary, or enqueue
  a Hangfire job and return an identifier? *Recommendation (confidence 70%):* **enqueue**, because
  `LeaveAccrualJob` batches up to 5,000 employees per tenant per leave type and an inline run would hold an
  HTTP request open for that. But enqueuing makes AC-1's "response reports how many rows were processed"
  impossible in the same response — which changes the acceptance criteria. **This fork must be decided
  before the story is buildable**; it is the reason this story is not `ready`.
- **OQ-2 (as-of date).** May a caller request accrual as of a specific date (valuable for testing year-end and
  fiscal-year-boundary behaviour), or is "today" the only supported input? *Recommendation:* allow it in
  non-production only, or omit it — an arbitrary as-of date against real balances is a data-integrity risk.
- **OQ-3 (is this worth building at all).** Stated plainly because §2 requires it: if the answer is "tests can
  seed the ledger directly and support can wait a day", the correct disposition is to close [[ENH-001]] as
  WONTFIX. **A human should confirm the capability is wanted before anyone builds it.**

## 12. Assumptions & Constraints
- No new permission is introduced; `Leave.ConfigurePolicy` matches every sibling action on the controller.
- No schema change is expected.
- The accrual calculation itself is out of scope and must not be modified — only its reachability.

## 13. Test Hints
- **The unblocking test:** the one US-LV-002 already asks for — trigger the job and assert `leave_ledger` accrual entries are created correctly. Today this cannot be written.
- Idempotency: trigger twice in the same period; assert the ledger row count is identical after the second call and that the response reports a no-op.
- Isolation: snapshot Tenant B's `leave_ledger` count and balances, trigger in Tenant A, re-assert Tenant B is byte-identical.
- Authorization: invoke as an Employee and as a Manager (neither holds `Leave.ConfigurePolicy`) → 403 each; as HR Officer → success.
- Concurrency: fire two triggers for the same tenant simultaneously; assert no interleaved duplicate credits.
- Audit: trigger a run and assert an audit row naming the triggering user, tenant and outcome.
- Equivalence: run the scheduled job and the manual trigger against equivalent fixtures for the same date and assert **identical** ledger effects (guards BR-2 against a forked second engine).
- Fiscal year: for an Apr–Mar tenant, trigger in February and assert the accrual is labelled with the correct leave year (regression guard for ISSUE-305).
