# HRM — Continuation Plan  ·  2026-07-10   — ⛔ CLOSED (2026-07-11), superseded by `COMPLETION-PLAN-2026-07-11.md`

> **CLOSED 2026-07-11.** Everything this plan carried shipped: US-ADM-011 workflow runtime (011a/b/c, #238/#239/#240),
> Training & Benefits (US-TRN-001/002/003, #241/#242/#243), Redis command-spans (#245), and the RLS flip-prep —
> ISSUE-268 (#244), ISSUE-269 (#246), the critical ISSUE-277 GUC-interceptor rewrite + roles.sql fix (#247/#248, RLS
> flip re-validated **GO**), and the ISSUE-275 test-flake stabilization (#249). Remaining/deferred items (the RLS ops
> flip, the long-tx tail, deferred FE, and the broader findings backlog) carry forward to the new plan.
> **→ Active plan: [`COMPLETION-PLAN-2026-07-11.md`](COMPLETION-PLAN-2026-07-11.md).**

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
Story: `docs/BA/admin-console/US-ADM-011.md` (§13 decisions, §14 phasing). Runtime core is in
`src/backend/HRM.Infrastructure/Services/WorkflowRuntimeService.cs` + `WorkflowInstance`/`WorkflowStepInstance`.
- **011b — Parallel steps + SLA escalation. ✅ SHIPPED → PR #239 (2026-07-10, base `test/local-subdomains`,
  awaiting merge).** Q1 resolved with the `WorkflowStepApprover` child table (approvers = additional NamedUser
  approvers beyond the step's own approver #1; FE already emitted `parallelApproverIdentifiers` so **no FE change**).
  Delivered: child entity+config+migration (dormant RLS policy in-migration), `WorkflowService` author/round-trip,
  group-aware `DecideCoreAsync` (all-approve advances / any-reject short-circuits / new `StepRecorded` partial
  outcome), idempotent per-tenant `WorkflowSlaEscalationJob` (`*/5`; atomic `ExecuteUpdateAsync` CAS, no nested tx;
  breached→Escalated+fresh Pending target row, no-target→stays Pending+notify admin, Q2 SlaDueAt=activation),
  3 US-NTF-006 events dispatched post-commit. 3374/3374 on Postgres; integration-enforcer WIRED + test-authenticator
  AUTHENTIC. **Auto-healed ISSUE-266** (pre-existing US-ADM-007 validation ErrorCode drop). **⚠ 011c must NOT
  re-collide the migration** — 011c's new-entity migration stacks on `20260710165359_Admin_WorkflowStepApprovers`.
- **011c — Delegation + remaining entity wiring. ✅ SHIPPED → PR #240 (merged 2026-07-10).** Delegation
  snapshot-at-activation (primary approver → backup when on approved leave; no-backup → notify admin; recorded via
  audit + `workflow_step_delegated` notif). Attendance/Overtime/Offer wired through the engine (per-service
  `TryWorkflowDecisionAsync` adapters mirroring Leave; AC-11 legacy fallback each). `WorkflowEntityType.Overtime`
  added (BE+FE). Offer got a `WorkflowInstanceId` column (`Recruitment_OfferWorkflowInstance`, column-only) + a
  `SendAsync` gate (409 `offer_approval_pending` until Approved, US-REC-007 BR-5). `DecideWorkflowInstanceCommandHandler`
  routes all 4 types. Read API (`GET /workflow-instances/{id}` step-chain + `GET /workflows/{lineageId}/instances`
  paged) + real `InFlightCount`. 3385/3385 Postgres + FE green; both auditors clean. **Auto-healed ISSUE-267**
  (open instance-read endpoint) + strengthened a delegation test. **⚠ DEFERRED:** full FE workflow-instance
  detail/step-chain viewer (net-new, cross-module) — BE read API delivered for a follow-up FE story.
  **✅ US-ADM-011 (workflow runtime) epic COMPLETE (011a #238 + 011b #239 + 011c #240).**
- ~~**011c — Delegation + remaining entity wiring.**~~ (superseded by the shipped entry above) Delegation at step-activation via approver approved-leave
  lookup (AC-6/FR-9, resolve **Q3**: snapshot-at-activation). Wire **Attendance regularization** + **Overtime**
  (resolve **Q6**: add a `WorkflowEntityType.Overtime` member + migration/seeded default — recommended) + **Offer**
  approval through the engine (FR-11 remainder); no-definition legacy fallback for each (AC-11); request-detail
  step-chain read API + admin instance list (FR-12).
- Payroll keeps its bespoke `PayrollApprovalService` maker-checker AS-IS (scope decision b).
- LOW follow-up from 011a: `WorkflowRuntimeService.DecideCoreAsync` should `ChangeTracker.Clear()`/detach on the
  onApproved-callback-failure path (harmless in Phase 1; tidy under RLS/reuse).

### 3. P4 — Training & Benefits (greenfield module) — ✅ COMPLETE (merged 2026-07-11)
Stories: `docs/BA/training-benefits/US-TRN-{EPIC,001,002,003}.md`. **US-TRN-001 → PR #241** (TrainingCourse +
CourseEnrollment; race-safe waitlist via FOR-UPDATE, FIFO promotion; guard=permissionGuard so employees self-enrol).
**US-TRN-002 → PR #242** (BenefitPlan CRUD + status machine; currency default from Tenant.Currency; archive-only).
**US-TRN-003 → PR #243** (BenefitEligibilityRule + BenefitEnrollment; pure `BenefitEligibilityEvaluator` AND-of-rules;
enroll gate plan-active→window→eligibility→duplicate; my-benefits self-service). All new tenant tables carry their
dormant RLS policy in-migration (RLS RULE). Both auditors clean per story (WIRED + AUTHENTIC). Suite 3361→3502.
Deferred/flagged: FE workflow-instance viewer (011c), manager-view eligible-plans UI (003 endpoint is API-complete),
benefit notifications reuse OnboardingOffboarding category (shared-enum decision).

### 4. RLS 3b — pre-prod-flip hardening (only before actually enabling RLS in prod)
- CI RLS postgres-service-container job (the RLS tests are Testcontainers-only → need a harness param to run
  against a CI service container).
- **[MED]** long-running by-id jobs (GeneratePayslips/SendPayslipEmails/DataExportGeneration/ProcessPayrollRun/
  HrReportExport) hold ONE `RunForTenantAsync` tx for the whole batch under RLS → long locks; consider a
  set-GUC-per-short-unit variant.
- **[LOW]** audit the wrapped service bodies for internal DI-scope/DbContext creation escaping the runner tx.

### 5. Misc / deferred
- **Redis command-spans** [LOW] — ✅ SHIPPED → PR #245 (merged 2026-07-11). Shared instrumented
  `IConnectionMultiplexer` for `IDistributedCache` + SignalR backplane + `AddRedisInstrumentation` (also
  consolidates connection pools). The EF second-level cache keeps its private multiplexer (library 5.3.13
  can't share it) → **ISSUE-274** (deferred; observed via cache metrics + DB spans instead).
- **`chore/agent-config-guards`** branch — ✅ already merged as **PR #237** (moot; config-protection +
  no-verify guards live on base).

### 6. RLS flip-prep (from the 2026-07-11 readiness audit — do before `Rls:Enabled=true`)
- **ISSUE-268** [MED, flip-BLOCKER] — ✅ RESOLVED → PR #244. Notification/session fresh-scope writes now
  routed through `ITenantJobRunner` so they carry the tenant GUC under RLS-on (proven by
  `NotificationRlsPostgresTests`).
- **ISSUE-269** [MED] — still OPEN: payslip render/email jobs hold one idle-in-tx GUC tx per batch;
  restructure `GeneratePayslips`/`SendPayslipEmails` before a high-volume flip (keep `ProcessPayrollRun`
  atomic). Deferred to the actual flip.

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
