# M/L Backlog Plan (post arc-n, 2026-07-21)

> The deferred **medium/large** feature backlog. arc-m shipped all S/M-actionable items + all real-Postgres
> coverage arms (PRs #391–#400); the **arc-n wave (#401–#415)** then shipped **all of Tier A (DF-20/6/31),
> all of Tier B (ISSUE-173, DF-8/22/4/21/9/7), and DF-50** — see status markers below. What remains is
> **Tier C** (DF-19, DF-1, DF-37, DF-23, LocationId epic) — genuine features needing a design pass each,
> **not** batch-grindable quick wins. Source of truth for status stays
> [`DEFERRED-FOLLOWUPS.md`](../DEFERRED-FOLLOWUPS.md); this file is the *sequencing* view.
>
> **Working method per item (unchanged):** recon/stale-check → one `feat/`|`fix/` branch → parallel
> backend-dev + frontend-dev on disjoint paths (neither commits) → orchestrator verifies build + Postgres +
> Karma + 2 auditors + adds material arms → bound TC → PR → auto-merge before the next.

## Tier A — quick M, near-clones of already-shipped patterns — ✅ ALL DONE (arc-n)

| Item | What | Pattern to reuse | Est |
|------|------|------------------|-----|
| **DF-20** ✅ | ✅ **DONE #403** — ISSUE-044 tenant-configurable leave **cancellation window** (hardcoded `const=0` → per-tenant column + API + read) | **Direct clone of DF-5** (#395): new column on the tenant/settings entity + migration + a resolver read. | M (S–M) |
| **DF-6** ✅ | ✅ **DONE #404** — Dashboard **PDF: embed tenant logo image** (branded PDF has the colour band; `LogoUrl` image not embedded) | HTTP fetch of the logo → embed bytes in the QuestPDF/PDF renderer (the renderer already draws the colour band). | S–M |
| **DF-31** ✅ | ✅ **DONE #405** — ISSUE-162 **per-employee payslip retry** (only run-level regenerate exists; `RegenerateAsync` overwrites ALL slips) | New `POST payslips/{runId}/employees/{empId}/retry` scoped to one slip; mirror the existing regenerate service path but single-slip. | M |

## Tier B — M, moderate design (independent, mid-value) — ✅ ALL DONE (arc-n) except DF-23 (→ Tier C)

| Item | What | Notes | Est |
|------|------|-------|-----|
| **DF-8** ✅ | ✅ **DONE #410** — Reporting-chain breadcrumb — full `Employee > Manager > VP > CEO` chain (only the immediate manager is on the DTO today) | Recursive CTE / walk or a dedicated endpoint; watch cycle-safety + tenant scope. | M |
| **DF-22** ✅ | ✅ **DONE #409** — ISSUE-309 per-location policy on tenant-wide sweeps (auto-clock-out + monthly summary + absenteeism ignore per-location overrides) | Batch-resolve policy per location in the sweep jobs; the resolver already exists (CAL-4). | M |
| **DF-23** | ISSUE-068 multi-location geofence (single center → allowed-locations collection) — **still open, deferred to Tier C** | New allowed-locations model + the clock-in geofence check iterates them. | M |
| **DF-4** ✅ | ✅ **DONE #411** — **leave-entitlement** recalc durability (recon-corrected: `LeaveEntitlementService.UpdateRuleAsync`, NOT onboarding). Enqueue-after-commit is best-effort; a Hangfire outage between commit+enqueue silently skips the recalc. Shipped as an **idempotent reconciliation SWEEP** (daily `leave-entitlement-reconcile` cron), not an outbox — the consumer is idempotent so exactly-once was unneeded (systemic Payroll/Onboarding siblings filed DF-58). | reconciliation sweep backstop; reliability, not correctness-under-normal-ops. | M |
| **DF-21** ✅ | ✅ **DONE #408** — ISSUE-039 my-balance **N+1** (per-leave-type entitlement-engine loop; >200ms NFR-1) | Batched resolution method on the entitlement engine — real refactor; add a real-PG perf arm. | M |
| **DF-9** ✅ | ✅ **DONE #412** — Custom-fields **FE render pass** (BE `GET /custom-fields/active` ships; the wizard consuming it needs running-UI verification) | Env-gated — needs the running UI + `@browser-debugger`/`/debug-ui`. | M |
| **ISSUE-173** ✅ | ✅ **DONE — FR-3 #406, FR-6 #407** — US-PAY-008 FR-3 **SLA auto-escalation to backup approver** + FR-6 **approval delegation** | Two related payroll-approval features; SLA needs a scheduled check + a backup-approver config; delegation needs a delegate model + authz. | M–L |
| **DF-7** ✅ | ✅ **DONE #413** (regression fix **#414**) — Distributed (Redis) magic-link limiter (per-IP DB-count throttle already works; distributed is the documented deferral) | Only worth it at multi-instance scale; the DB-count is correct single-instance. | M |

## Tier C — L / structural / risky (deliberate, one at a time, design review first)

| Item | What | Risk | Est |
|------|------|------|-----|
| **DF-1** | Remodel 6 date-only `timestamptz` columns → `date` (`onboarding_checklist_instance.start_date`, `onboarding_task_instance.due_date`, `asset.issue_date`, `exit_interview.interview_date`, `offboarding_instance.last_working_day`, `offboarding_task_instance.due_date`) + props → `DateOnly` | Migration touching 6 columns across 4 modules + every read/write site; the `DateOnly`→`date` pattern is proven (#386) but the blast radius is wide. | L |
| **DF-19** | ISSUE-045 pool-aware carry-forward restoration (cancel writes one general Adjusted row, not a per-pool split) | Needs distinct carry-forward **pool** modelling — `LeaveCarryForwardTracking` has no `Pool` field yet + FIFO-aware restoration. | L |
| **DF-37** | Payroll-model pass: key salary components by **Code** not Name (ISSUE-280) + BUG-079 residual encashment clauses (ISSUE-295) | Touches the core payroll calc model; regression-sensitive (money). | M–L |
| **DF-50** ✅ | ✅ **DONE #415** — ISSUE-285(b) parallelize the ~8 sequential dashboard widgets via a CHILD-SCOPE-per-widget runner (NOT IDbContextFactory) + cold-start warmup | **Concurrency + cross-tenant risk** — the factory contexts MUST carry the scoped tenant filter or leak on a hot path. Do deliberately. | M |
| **LocationId epic** | `LeaveEntitlementRule.LocationId` (CAL-4c) + the `TC-LV/CHR-ISO-049` cross-tenant location-tier arms | Location-tier entitlement resolution; security-adjacent (cross-tenant location resolve). | L |

## Perf / ops validation (dev-runnable now — being scheduled separately)

| Item | What | Status |
|------|------|--------|
| **DF-51** | k6 dashboard-at-scale scenario (p95<800ms @ 50k rows) | ✅ **VALIDATED 2026-07-20** — 50k / 50-VU / 5-min: widgets **p95 192.9ms** (<800 ✅), 0% errors. Cold-start outliers (max ~25s) → DF-50. |
| RLS dev-enable | Enable + validate RLS in a local/throwaway DB (prod flip stays ops) | ✅ **VALIDATED 2026-07-20** — 20/20 across all 5 RLS Testcontainers suites (isolation, fail-closed GUC, reconciler ENABLE→isolate→DISABLE reversibility). Prod flip stays the ops runbook. |
| ClamAV | Run ClamAV in Docker + confirm the virus-scan path (EICAR) locally | ✅ **VALIDATED 2026-07-20 (#401)** — `ClamAvVirusScanner` detects EICAR against a real clamd; clean passes. Durable opt-in test `Category=ClamAv`. |
| **DF-53** (new) | Perf seed not self-contained on a fresh DB (sources hash + roles from `acme`) | Rig-hardening: COALESCE the role/hash source across acme→e2e→any built-in-role tenant. Workaround documented in `perf/README.md`. |

## Suggested order
1. ~~**Tier A** (DF-20 → DF-6 → DF-31)~~ ✅ **DONE arc-n** (#403/#404/#405).
2. ~~**Tier B** by value~~ ✅ **DONE arc-n** (ISSUE-173 #406/#407, DF-22 #409, DF-21 #408, DF-8 #410, DF-4 #411, DF-9 #412, DF-7 #413/#414).
3. ~~DF-50~~ ✅ **DONE #415** (dashboard widget warmup + parallelize).
4. Perf/ops validation (RLS dev-enable, ClamAV, k6@50k) ✅ **VALIDATED** (see table above).
5. **▶ REMAINING = Tier C only** — do **one at a time**, each with a `/research-story` or design pass **first**: **DF-19** (pool-aware carry-forward), **DF-1** (date-only remodel), **DF-37** (payroll-model by Code), **DF-23** (multi-location geofence), and the **LocationId epic** (CAL-4c). No batch-grinding — these are structural/regression-sensitive.
