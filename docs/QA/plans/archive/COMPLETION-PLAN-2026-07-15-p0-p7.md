# ARCHIVED SNAPSHOT — P0–P7 priority list, 2026-07-15 snapshot.

> Split out of [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md) on **2026-09-01**, when the plan was
> audited and rebuilt. It carried five overlapping sections that each claimed to be 'the queue';
> this is one of them, preserved verbatim as history.
>
> **Not current. Do not execute from this file.** The live execution lane is
> [`../GAP-CLOSURE-QUEUE.md`](../GAP-CLOSURE-QUEUE.md); the current backlog is
> [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md).

---

## ▶ Remaining work — priority order

> ⚠ **STALE (2026-07-15 snapshot — predates the #352–#382 campaign).** The P0–P7 breakdown below was groomed BEFORE the arc-k/arc-l "fix all bugs+issues+decisions" campaign, which cleared the MED/LOW bug/issue backlog + every product/BA decision (see the two 2026-07-18/19 changelog entries above). **The LIVE queue is now `docs/QA/DEFERRED-FOLLOWUPS.md`** (per-item, with recommendations); `docs/QA/TEST-FINDINGS.md` holds current finding statuses. Treat the sections below as historical context, not the active plan.

### P0 — Ledger reconciliation + missing test suites (cheap, unblocks accurate planning)
- **Reconcile stale trackers:** `docs/BA/STATUS.md` still marks US-ADM-011, US-TRN-EPIC/001/002/003, US-NTF-006 as
  `[ ]` though they shipped this arc; the `TEST-STATUS.md` tally block (ln 149-157) is stale vs its own per-module rows.
  Flip them to reflect merged state.
- **Author the missing test-case suites (shipped-but-untested-in-the-ledger — ZERO coverage today):**
  `docs/QA/training-benefits/TC-TRN-001/002/003`, `TC-ADM-011-*` (workflow runtime), and US-NTF-006 delivery. Add
  `TEST-STATUS`/`TRACEABILITY` rows. Also **ISSUE-273** (additive test-hardening arms across US-ADM-011 + T&B).

### P1 — HIGH findings (14) — the real defects (triage-verify, then fix by cluster)
- **RBAC persona lockouts (Payroll):** BUG-060 (ln 2552, HR Officer can't configure salary components), BUG-071
  (ln 3315, HR Officer lacks `Payroll.Run`), BUG-077 (ln 3746, HR Officer 403 on all Payroll reports). One role-bundle
  fix likely clears the cluster.
- **Payroll correctness:** BUG-078 (ln 3810) — overtime hourly base uses total EARNINGS not BASIC → OT **overpaid ~2.5×**.
- **Audit completeness:** BUG-080 (ln 3938, 8 payroll audit actions never emitted), BUG-082 (ln 4176, "audit ALL changes"
  false — interceptor covers 3 entity types), BUG-084 (ln 4241, audit keyword search 500s on jsonb `~~`).
- **Scale / SLA:** BUG-123 (ln 5388, dashboard p95 829ms + 60s timeouts @50k), BUG-124 (ln 5427, Leave reports N+1 timeout
  @5k), BUG-125 (ln 5440, Attendance `payroll-data` P95 ~28s @5k vs ≤5s SLA).
- **FE contract / session:** BUG-097 (ln 4778, reload/deep-link logs user out — no silent refresh-cookie restore),
  BUG-100 (ln 4838, Custom Fields page render crash + Add modal throws), BUG-113 (ln 5133, Employee↔Location FK
  unreachable → counts always 0), **BUG-243 (ln 5660, ~9/10 Performance Angular services hit 404 — broad FE↔BE contract
  break)**.

### P2 — MED findings (78) — themed clusters (representative IDs; full list at line anchors)
- **Notification DELIVERY (the biggest theme, ties to P3):** ISSUE-221 (ln 5226, only the template "test email" uses real
  SMTP — all module transactional email is LogOnly), ISSUE-228 (ln 5401, SignalR live but every in-app producer is
  log-only/unreachable), ISSUE-214 (ln 4952, NTF-002/003 pages orphaned from nav). **US-NTF-001/002 delivery.**
- **RBAC / team-scope:** ISSUE-195 (ln 4341, manager `Reports.View.Team` unimplemented → sees full tenant), BUG-120
  (ln 5303, Directory gated ViewOwn only → Manager/HR/Admin 403).
- **Audit gaps:** BUG-081 (4085), BUG-083 (4189, PII-read audit unimplemented), BUG-085 (4254), ISSUE-120 (2854),
  ISSUE-200 (4432, zero onboarding audit), ISSUE-108-PAY (2590).
- **Payroll semantics:** ISSUE-153/154 (3324/3333, idempotency-replay + no cancel/re-run), 156/157 (pro-ration),
  165/166/167/170 (point-in-time payslip, std-deduction/80C, YTD tax, finalized-retro-edit guard), 177/178 (year-end
  statement + report filters), 180 + BUG-079 (encashment), BUG-061/062/074.
- **Scale / N+1:** BUG-095 (4682, export file collision @concurrency), ISSUE-203 (4695, login p95 4.8× SLA — BCrypt),
  ISSUE-201 (4539, negative page → 500), ISSUE-230 (5453).
- **Security:** ISSUE-226 (5344, offer stored-XSS), ISSUE-134 (3060, PIP fields plaintext), ISSUE-165-class PII.
- **a11y:** BUG-108/109/110/112 (5063-5121). **UTC-boundary:** BUG-245/246 (5703/5710). **Redis:** BUG-115 (5174,
  outage ~11s stall), BUG-116 (5187, my-tenants cache no invalidation).
- **Recruitment/Perf gaps:** ISSUE-133/137/140-PRF/141/145, ISSUE-140-REC (3185, REC-010 auto-user/welcome/onboarding
  stubs), BUG-064/065/066 (scorecard/sign-off/offer BR bypasses), ISSUE-232 (3193).
- **⚠ ISSUE-245 (ln 5685) — the Angular unit-test suite is RED on base (~26 pre-existing spec failures).** Fix this
  early: a red FE baseline undermines every FE gate.

### P3 — Architectural / infra debt (shipped-but-incomplete live paths — from the code survey)
- **★ Notification delivery rewire (biggest surface):** `RealNotificationDispatcher` infra landed but **12 `LogOnly*`
  module seams are not switched onto it** (Leave/Payroll/Performance/Recruitment/Payslip-email/TenantWelcome/
  Lifecycle/UserMgmt/F&F/Recommendation), plus deferred notify-halves in Attendance/Overtime/Regularization and ~13
  jobs that only log. This IS ISSUE-221/228. Scope as **US-NTF-001/002 delivery**.
- **★ ClamAV virus scanner** — `AllowWithLogVirusScanner` accepts uploads with only a log on live asset/onboarding/
  employee-document paths. Wire real ClamAV (`TODO(prod)`).
- **★ JWT denylist / session revocation** — `NoOpSessionRevoker`; access tokens can't be revoked (Redis denylist TODO).
- **★ IPermissionCache → Redis** (`InMemoryPermissionCache`, NFR-2) — prod-scale gap.
- **★ Year-End Tax Statement** — `PayrollReportService` live `deferred:true` stub returns a Note (compliance) — US-PAY-009.
- **PII encryption at rest (US-PLT-005, plan-HIGH)** — compensation (Recommendation/Pip/Budget) + PIP reason stored
  plaintext; no pgcrypto.
- **PDF export** deferred across Performance/Recruitment dashboards (CSV/XLSX shipped). **Observability** per-tenant
  enrichment/custom meters (US-PLT-004). Redis eviction seams (TenantSettings/SubscriptionPlan).

### P4 — RLS actual prod flip (GO — ops-gated) + the deferred long-tx tail
- **Ops gates (per environment, user's deploy step)** — README §3b: run `roles.sql`, `hrm_owner` owns schema, repoint
  `DefaultConnection`→`hrm_app` / `PrivilegedConnection`→`hrm_owner` + Hangfire→privileged, coverage-guard green on the
  target Postgres, rollback rehearsed. Greenfield only: `GRANT CREATE ON DATABASE … TO hrm_owner` (ISSUE-278).
- **Deferred code tail (before a HIGH-VOLUME flip):** restructure `DataExportGeneration` + `HrReportExport` to
  GUC-per-short-unit (ISSUE-269 class; low-freq → safe to defer past the initial flip). Finalize **US-PLT-002**.
- Cosmetic: ISSUE-279 (reconciler bypass-warning false-fire), ISSUE-266 (WorkflowService drops ErrorCode), ISSUE-276
  (Redis future-host coupling).

### P5 — Deferred FE (BE-complete this arc)
- **ISSUE-272** [MED] FE workflow-instance detail / step-chain viewer (US-ADM-011 FR-12 UI) + admin instance list;
  tighten **ISSUE-267** (instance-read endpoint is `[Authorize]`-only).
- **ISSUE-271** manager "eligible plans for employee X" UI (US-TRN-003 AC-8; BE API-complete).
- Nav-orphan cleanups: ISSUE-208 (Attendance sub-pages), ISSUE-214 (NTF-002/003).

### P6 — Pending / net-new stories
- **SSO (real gaps):** ~~US-AUTH-012 (per-tenant SSO config)~~ **BUILT — PR #444 (unmerged)**; US-AUTH-016 (SSO enforcement / break-glass / admin-consent) remains
  — also the 5 `[b]` BLOCKED SSO test cases (US-AUTH-011/012/013/014/016).
- US-PRF-011 (calibration workspace), US-ADM-012 (plan/module governance enforcement), US-PLT-004 (observability).

### P7 — LOW tail (152) + small tidies
- Batch-triage the 152 LOW by module (line anchors in the survey). Quick self-contained ones: **ISSUE-270** (Training/
  Benefits notification category), **ISSUE-274** (custom `IEFCacheServiceProvider` for EF-cache Redis spans),
  **DecideCoreAsync `ChangeTracker.Clear()`** tidy on the onApproved-failure path (`WorkflowRuntimeService.cs:309-315`).
- Deferred/feature-blocked (park at decision-gate): ~~ISSUE-021 (no SalaryGrade entity)~~ **DONE #389**, ~~BUG-056 (no goal-finalize seam)~~ **DONE #387**.
  WONTFIX for reference: ISSUE-209/250/265.
