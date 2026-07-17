# Deferred Follow-ups Register

**Purpose.** A single, durable home for "**verify or do this later**" items — the deferrals, known
residual risks, needs-infra test gaps, and structural clean-ups that surface *while shipping something
else*. Previously these lived only as inline `Follow-up (LOW)` notes buried in `TEST-FINDINGS.md`
RESOLVED blocks, where they were easy to lose. This register indexes them so they can actually be
picked up.

**Convention (how this stays useful).**
- When a fix/PR **defers** something — a documented deferral, a residual risk, a "would be nice / more
  correct" refactor, an env/infra-gated test gap, or a change that would make a whole **bug class**
  impossible — add a row here **in addition to** the note in the source finding. Don't rely on the
  inline note alone.
- Each row links back to its source finding/PR so the full context is one hop away.
- When one is picked up, move it to **Done** (or delete it) and flip the source finding if relevant.
- Skim this register when starting a work session or planning a batch — some items unblock over time
  (e.g. an item blocked by another bug becomes actionable once that bug lands).

Status legend: **OPEN** · **BLOCKED** (waiting on another item) · **DONE**.

---

## Open

| # | Item | Source | Why deferred / value | Effort |
|---|------|--------|----------------------|--------|
| DF-1 | **Remodel date-only `timestamptz` columns to `date`** — `onboarding_checklist_instance.start_date` + `onboarding_task_instance.due_date`, `asset.issue_date`, `exit_interview.interview_date`, `offboarding_instance.last_working_day` + `offboarding_task_instance.due_date`. They hold date-only values; making them `date` (and the properties `DateOnly`) makes the whole **`.Date`→timestamptz Kind bug class impossible**, not just patched. Needs a migration + DTO/reader touches. | [[BUG-289]] · [[BUG-290]] (PR #344/#345) | Structural — kills the bug class the `SpecifyKind(Utc)` patches only guard site-by-site. Medium risk (existing-data migration + read-path changes). | M |
| DF-3 | **Testcontainers arms for the InMemory-only money paths** — the storage-quota `SumAsync`/plan-join (BUG-114) and the entitlement recalc + `GetLedgerBalanceAsync` ordering (BUG-118). Both proven only on InMemory (the repo's standing InMemory-masks-Postgres class). | [[BUG-114]] (#332) · [[BUG-118]] (#333) | Defence-in-depth on money paths; low SQL-translation risk but currently unproven on Postgres. | M |
| DF-4 | **Onboarding recalc enqueue: outbox for exactly-once** — `UpdateRuleAsync` enqueues the recalc *after* the commit (best-effort, matches `PayrollRunService`). A Hangfire-storage outage between commit and enqueue silently skips the recalc. An outbox row committed with the rule edit would make it exactly-once. | [[BUG-118]] (#333) auditor note | Durability on AC-5; rare window, but it's the exact bug-class BUG-118 fixed. | M–L |
| DF-5 | **BR-6 template variant cap → plan-configurable** — `MaxLanguageVariants` is a constant `2`; the spec says plan-configurable. Wire it to `PlanLimitResolver` (needs a new plan-limit key). | [[BUG-122]] (#337) | Matches the plan-limit pattern used elsewhere; low urgency. | S–M |
| DF-6 | **Dashboard PDF: embed the tenant logo image** — the branded PDF uses the tenant `PrimaryColor` band; the `LogoUrl` image isn't embedded (needs an HTTP fetch + image bytes in the renderer). | [[ISSUE-126]] (#340) | Completes the FR-8 branding; color branding already ships. | M |
| DF-7 | **Distributed (Redis) magic-link limiter** — the per-IP throttle is a durable DB-count; a fully distributed limiter remains the documented deferral. | [[ISSUE-130]] (#341) | The DB-count throttle already gives cross-instance defence-in-depth. | M |
| DF-8 | **Reporting-chain breadcrumb (TC-283)** — the *immediate* manager is now on `EmployeeDto`/`EmployeeProfileDto`; the full `Employee > Manager > VP > CEO` chain (recursive walk / dedicated endpoint) isn't. FE can build it from `/org-tree?view=reporting` meanwhile. | [[ISSUE-218]] (#338) | Larger; only needed if the breadcrumb wants a first-class endpoint. | M |
| DF-9 | **Custom-fields FE render pass** — the BE `GET /custom-fields/active` endpoint ships; the wizard *consuming* it needs a running-UI verification to close the FE half. | [[ISSUE-206]] (#339) | Env-gated (needs the running stack). | S |
| DF-10 | **Controller idempotency-key precedence test (header vs body)** — `OnboardingChecklistsController` resolves `Idempotency-Key` header-then-body with no test; a mutation swapping precedence would survive. | [[ISSUE-315]] | Cheap controller/integration arm. | S |
| DF-13 | **Magic-link email delivery** — the applicant-portal token logic works but the raw link is never delivered (FR-7 email seam has no live caller). Wiring real delivery = the US-NTF-006 delivery story, not a bug fix. **BLOCKED on US-NTF-006.** | [[ISSUE-132]] (MED-fix campaign 2026-07-17) | Delivery is a whole subsystem/story; the token/security half already works. | L (story) |
| DF-14 | **Payroll approval notification delivery** — `NotifyApprovalEventAsync` is a log-only seam; no in-app/SignalR/email on submit/approve/reject (AC-1/2/3, NFR-1). = US-NTF-006 delivery. A "Pending Approvals" queue/badge read-endpoint is a smaller independent sub-item. **BLOCKED on US-NTF-006.** | [[ISSUE-172]] (MED-fix campaign 2026-07-17) | Delivery is the tracked cross-module story; the approval state machine + audit trail already work. | L (story) |
| DF-12 | (filed as **[[ISSUE-317]]**) **FE has no `Unknown` badge for a tolerated corrupt row** — `applicant.models.ts` / `pipeline.models.ts` (`STAGE_BADGE`) / `payroll-run.models.ts` (`RUN_STATUS_BADGE`/`LABELS`) are `Record<Enum,string>` with no `Unknown` key, so a row the BE now tolerates renders a **blank** badge (undefined key lookup) rather than a visible "unknown/corrupt" affordance. Graceful degradation, no crash — but silent. | [[ISSUE-231]] (PR #348) · `@integration-enforcer` | Decision: add an `Unknown` badge (surface the bad row so someone fixes the data) vs accept blank. | S |

## Done

| # | Item | Source | Resolution |
|---|------|--------|------------|
| DF-2 | **Full end-to-end cumulative-RUN true-up on real Postgres** — the processor running the payroll + persisting withheld deltas end-to-end, previously InMemory-only. | [[ISSUE-300]] · **PR #347** (2026-07-17) | `YtdCumulativeRunPostgresTests` drives `InitiatePayrollRunCommand → PayrollRunProcessor.ProcessAsync` on `postgres:17-alpine`. 2 arms green (cumulative Apr–Aug true-up `0,0,0,60k,60k` + non-cumulative flag-gate contrast). Confirmed the run write path is date-Kind-clean (unblocked once BUG-289/290 landed). |
| DF-11 | **`RejectionReason` enum columns still strict → board/detail can still 500** (filed as [[ISSUE-316]]). | [[ISSUE-231]] · **PR #351** (2026-07-17) | Both `rejection_reason` columns now read through `TolerantEnumToStringConverter<RejectionReason>(Unknown)` (nullable-safe); `Unknown=99` added; stage-move validators guard it; 2 Postgres arms + mutation-verified; TC-REC-003-16. Every enum-string column on the recruitment board/detail + payroll read paths is now tolerant. |
