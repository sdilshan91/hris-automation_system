# M/L Backlog Plan (post arc-m, 2026-07-20)

> The deferred **medium/large** feature backlog — everything left after arc-m shipped all S/M-actionable
> items + all real-Postgres coverage arms (PRs #391–#400). These are genuine features needing a design
> pass each, **not** batch-grindable quick wins. Source of truth for status stays
> [`DEFERRED-FOLLOWUPS.md`](../DEFERRED-FOLLOWUPS.md); this file is the *sequencing* view.
>
> **Working method per item (unchanged):** recon/stale-check → one `feat/`|`fix/` branch → parallel
> backend-dev + frontend-dev on disjoint paths (neither commits) → orchestrator verifies build + Postgres +
> Karma + 2 auditors + adds material arms → bound TC → PR → auto-merge before the next.

## Tier A — quick M, near-clones of already-shipped patterns (lowest risk, do first)

| Item | What | Pattern to reuse | Est |
|------|------|------------------|-----|
| **DF-20** | ISSUE-044 tenant-configurable leave **cancellation window** (hardcoded `const=0` → per-tenant column + API + read) | **Direct clone of DF-5** (#395): new column on the tenant/settings entity + migration + a resolver read. | M (S–M) |
| **DF-6** | Dashboard **PDF: embed tenant logo image** (branded PDF has the colour band; `LogoUrl` image not embedded) | HTTP fetch of the logo → embed bytes in the QuestPDF/PDF renderer (the renderer already draws the colour band). | S–M |
| **DF-31** | ISSUE-162 **per-employee payslip retry** (only run-level regenerate exists; `RegenerateAsync` overwrites ALL slips) | New `POST payslips/{runId}/employees/{empId}/retry` scoped to one slip; mirror the existing regenerate service path but single-slip. | M |

## Tier B — M, moderate design (independent, mid-value)

| Item | What | Notes | Est |
|------|------|-------|-----|
| **DF-8** | Reporting-chain breadcrumb — full `Employee > Manager > VP > CEO` chain (only the immediate manager is on the DTO today) | Recursive CTE / walk or a dedicated endpoint; watch cycle-safety + tenant scope. | M |
| **DF-22** | ISSUE-309 per-location policy on tenant-wide sweeps (auto-clock-out + monthly summary + absenteeism ignore per-location overrides) | Batch-resolve policy per location in the sweep jobs; the resolver already exists (CAL-4). | M |
| **DF-23** | ISSUE-068 multi-location geofence (single center → allowed-locations collection) | New allowed-locations model + the clock-in geofence check iterates them. | M |
| **DF-4** | Onboarding recalc **outbox** for exactly-once (enqueue-after-commit is best-effort; a Hangfire outage between commit+enqueue silently skips the recalc) | Transactional outbox + a drain job; reliability, not correctness-under-normal-ops. | M |
| **DF-21** | ISSUE-039 my-balance **N+1** (per-leave-type entitlement-engine loop; >200ms NFR-1) | Batched resolution method on the entitlement engine — real refactor; add a real-PG perf arm. | M |
| **DF-9** | Custom-fields **FE render pass** (BE `GET /custom-fields/active` ships; the wizard consuming it needs running-UI verification) | Env-gated — needs the running UI + `@browser-debugger`/`/debug-ui`. | M |
| **ISSUE-173** | US-PAY-008 FR-3 **SLA auto-escalation to backup approver** + FR-6 **approval delegation** (neither implemented) | Two related payroll-approval features; SLA needs a scheduled check + a backup-approver config; delegation needs a delegate model + authz. | M–L |
| **DF-7** | Distributed (Redis) magic-link limiter (per-IP DB-count throttle already works; distributed is the documented deferral) | Only worth it at multi-instance scale; the DB-count is correct single-instance. | M |

## Tier C — L / structural / risky (deliberate, one at a time, design review first)

| Item | What | Risk | Est |
|------|------|------|-----|
| **DF-1** | Remodel 6 date-only `timestamptz` columns → `date` (`onboarding_checklist_instance.start_date`, `onboarding_task_instance.due_date`, `asset.issue_date`, `exit_interview.interview_date`, `offboarding_instance.last_working_day`, `offboarding_task_instance.due_date`) + props → `DateOnly` | Migration touching 6 columns across 4 modules + every read/write site; the `DateOnly`→`date` pattern is proven (#386) but the blast radius is wide. | L |
| **DF-19** | ISSUE-045 pool-aware carry-forward restoration (cancel writes one general Adjusted row, not a per-pool split) | Needs distinct carry-forward **pool** modelling — `LeaveCarryForwardTracking` has no `Pool` field yet + FIFO-aware restoration. | L |
| **DF-37** | Payroll-model pass: key salary components by **Code** not Name (ISSUE-280) + BUG-079 residual encashment clauses (ISSUE-295) | Touches the core payroll calc model; regression-sensitive (money). | M–L |
| **DF-50** | ISSUE-285(b) parallelize the ~8 sequential dashboard HR widgets via `IDbContextFactory` | **Concurrency + cross-tenant risk** — the factory contexts MUST carry the scoped tenant filter or leak on a hot path. Do deliberately. | M |
| **LocationId epic** | `LeaveEntitlementRule.LocationId` (CAL-4c) + the `TC-LV/CHR-ISO-049` cross-tenant location-tier arms | Location-tier entitlement resolution; security-adjacent (cross-tenant location resolve). | L |

## Perf / ops validation (dev-runnable now — being scheduled separately)

| Item | What | Status |
|------|------|--------|
| **DF-51** | k6 dashboard-at-scale scenario (p95<800ms @ 50k rows) | Rig exists (`perf/scripts/*` + 50k seed); **run locally now**. |
| RLS dev-enable | Enable + validate RLS in a local/throwaway DB (prod flip stays ops) | Built + proven OFF; **validate in dev now**. |
| ClamAV | Run ClamAV in Docker + confirm the virus-scan path (EICAR) locally | **Validate in dev now**. |

## Suggested order
1. **Tier A** (DF-20 → DF-6 → DF-31) — fast, low-risk, reuse shipped patterns.
2. **Tier B** by value (ISSUE-173 SLA/delegation and DF-22 per-location sweeps are the most user-visible; DF-21 the clearest perf win).
3. **Tier C** one per session, each with a `/research-story` or design pass first (DF-1 and the LocationId epic especially).
4. Perf/ops validation (RLS dev-enable, ClamAV, k6) can run in parallel with the above — they're independent of the feature work.
