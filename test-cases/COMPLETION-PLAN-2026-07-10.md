# HRM — Continuation Plan  ·  2026-07-10

> Supersedes `COMPLETION-PLAN-2026-07-06.md` (now CLOSED — P1–P3 + P4-011a shipped). This plan carries the
> remaining work: the rest of the workflow runtime, Training & Benefits, and the deferred infra hardening.
> Base at close: `test/local-subdomains` (P1/P2/P3 + RLS merged; #235/#236/#238 open for review).

## ✅ What shipped (2026-07-06 → 2026-07-10) — closed plan
- **P1** ISSUE-247 DataProtection key persistence (#224). **P2** 8 findings in 3 clusters (#225/#226/#227). **P3**
  OTel + `/health/live|ready` + tenant-safe EF second-level cache (#229) + cache-whitelist broadened 8→28 (#236).
- **RLS full-enable BUILT & MERGED, flag OFF** (#230 ambient tenant/cache-prefix · #231 dormant policies + isolation
  tests · #232 connection routing · #233 per-job GUC · #234 reconciler + e2e + SendEmailJob). Runbook +
  design in `src/backend/HRM.Infrastructure/Persistence/Rls/{README.md,IMPLEMENTATION-DESIGN.md}`; ADR
  `docs/vault/decisions/ADR-2026-07-10-tenant-isolation-model.md` (decision: shared-DB + RLS, hybrid seam open).
- **BUG-264** [MED] ApplicantConversion retry idempotency (#235). **ISSUE-265** → WONTFIX.
- **P4 US-ADM-011a** workflow runtime core + Leave (#238).

## ▶ RESUME — remaining work (priority order)

### 1. Open PRs (awaiting user merge) — merge before stacking dependent work
- **#235** BUG-264 · **#236** cache-whitelist · **#238** US-ADM-011a. (011b builds on #238.)

### 2. P4 — Workflow runtime, remaining phases (US-ADM-011)
Story: `user-stories/admin-console/US-ADM-011.md` (§13 decisions, §14 phasing). Runtime core is in
`src/backend/HRM.Infrastructure/Services/WorkflowRuntimeService.cs` + `WorkflowInstance`/`WorkflowStepInstance`.
- **011b — Parallel steps + SLA escalation.** **DECISION (Q1): new `WorkflowStepApprover` child table** (a
  `WorkflowStep` has many approvers) — NOT sibling-rows. Build: the child entity + EF config + migration (add its
  dormant RLS policy in-migration — see the RLS RULE below); refactor design-time `WorkflowStep`/`WorkflowService`
  + the US-ADM-007 **editor UI** to author multiple approvers per step; runtime groups a step's approvers for
  **all-approve / any-reject** join (AC-4); idempotent recurring **Hangfire SLA-timer job** scanning
  `WorkflowStepInstance` where `Decision=Pending` past `SlaDueAt` → escalate (AC-5/FR-8, resolve **Q2**:
  SlaDueAt = step-activation time); US-NTF-006 notifications on assignment/escalation/decision.
- **011c — Delegation + remaining entity wiring.** Delegation at step-activation via approver approved-leave
  lookup (AC-6/FR-9, resolve **Q3**: snapshot-at-activation). Wire **Attendance regularization** + **Overtime**
  (resolve **Q6**: add a `WorkflowEntityType.Overtime` member + migration/seeded default — recommended) + **Offer**
  approval through the engine (FR-11 remainder); no-definition legacy fallback for each (AC-11); request-detail
  step-chain read API + admin instance list (FR-12).
- Payroll keeps its bespoke `PayrollApprovalService` maker-checker AS-IS (scope decision b).
- LOW follow-up from 011a: `WorkflowRuntimeService.DecideCoreAsync` should `ChangeTracker.Clear()`/detach on the
  onApproved-callback-failure path (harmless in Phase 1; tidy under RLS/reuse).

### 3. P4 — Training & Benefits (greenfield module)
Stories authored: `user-stories/training-benefits/US-TRN-{EPIC,001,002,003}.md`. Permission constants exist
(`PermissionCatalog` `Training.*`/`Benefits.*`). Entities: `TrainingCourse` + `CourseEnrollment` (001);
`BenefitPlan` (002); `BenefitEligibilityRule` + `BenefitEnrollment` (003). Sequence: 001 ∥ 002, then **003 after
002**. Full BE+FE per module. All new tenant tables → apply the RLS RULE.

### 4. RLS 3b — pre-prod-flip hardening (only before actually enabling RLS in prod)
- CI RLS postgres-service-container job (the RLS tests are Testcontainers-only → need a harness param to run
  against a CI service container).
- **[MED]** long-running by-id jobs (GeneratePayslips/SendPayslipEmails/DataExportGeneration/ProcessPayrollRun/
  HrReportExport) hold ONE `RunForTenantAsync` tx for the whole batch under RLS → long locks; consider a
  set-GUC-per-short-unit variant.
- **[LOW]** audit the wrapped service bodies for internal DI-scope/DbContext creation escaping the runner tx.

### 5. Misc / deferred
- **Redis command-spans** [LOW] — `OpenTelemetry.Instrumentation.StackExchangeRedis` (needs the cache provider's
  `IConnectionMultiplexer` shared for instrumentation).
- **`chore/agent-config-guards`** branch (pushed, not merged) — commit `3c4c9dda` adds config-protection +
  no-verify git-hook-bypass guards (`.claude/hooks/scripts/*`). User decides whether to merge to base.

## 🔒 Standing rules learned this arc (apply going forward)
- **NEW-TENANT-TABLE RLS RULE:** every new table with a `tenant_id` column MUST add its own dormant
  `tenant_isolation` policy **in its migration** (strict `USING`+`WITH CHECK`, `NULLIF(current_setting(
  'app.current_tenant',true),'')::uuid`; nullable-tenant tables get permissive `USING` + strict `WITH CHECK`).
  The `RlsIsolationPostgresTests` coverage-guard fails CI otherwise. See 011a's migration for the pattern.
- **Cache pattern** = read + auto-evict-on-`SaveChanges` (NOT write-through), tenant-prefixed keys via
  `AmbientTenant`. New reference/read-heavy tables can be added to `Cache:SecondLevelCache:CachedTables` +
  `DefaultCachedTables`; NEVER cache authz/identity/secrets, high-write transactional, or pay-affecting tables.
- **RLS enablement is config-gated + reversible** (`Rls:Enabled` + repoint `DefaultConnection`→`hrm_app` /
  `PrivilegedConnection`→`hrm_owner` after `roles.sql`, `hrm_owner` OWNS the schema). Committed default stays OFF.
- **Retry-vs-tracked-state (BUG-068/252/264):** any manual tx / row-lock under `EnableRetryOnFailure` must wrap in
  `CreateExecutionStrategy().ExecuteAsync` AND detach entities added/mutated in a failed attempt on rollback.
- **Git:** verify the current branch before every commit (background agents can move HEAD); recover a mis-landed
  commit via `git cherry-pick` onto the intended branch. Do NOT `--no-verify` (the no-verify-guard blocks it).

## Working method (unchanged)
One `feat/`|`fix/` branch per story/cluster off fresh `test/local-subdomains` → sub-agents on non-overlapping
paths → gate on the FULL suite (`dotnet build` + `dotnet test`, Docker up for Testcontainers) → code-only PR.
Big builds: dispatch a backend-dev; if it hits the tool-use limit, continue it via SendMessage. Auto-heal
out-of-lane flags into this plan + `TEST-FINDINGS.md`.
