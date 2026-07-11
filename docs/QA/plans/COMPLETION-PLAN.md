# HRM — Completion Plan (living)

> **This is the ONE living completion plan.** It rolls over in place — do not create a new
> dated `COMPLETION-PLAN-<date>.md`; instead add a dated entry to the **Changelog** below and
> re-sort the plan body. Full snapshots of superseded plans are archived under
> [`archive/`](archive/). Built from a read-only survey of the findings ledger, code TODO/stub
> markers, the RLS flip state, and the story/test-status ledgers. **Living document — re-sort as
> reality changes.** Base: `test/local-subdomains`. **RLS is committed OFF; flip is RE-VALIDATED GO** (see P4).

## 🗓 Changelog (rollover history)

| Date | Event | Full snapshot |
|------|-------|---------------|
| **2026-07-11** | **Current active plan.** Rolled over from 07-10 (all it carried shipped). Rebuilt from a full findings/ledger/RLS survey → P0 ledger reconcile + missing TC suites … P7 LOW tail (body below). | _(this file)_ |
| 2026-07-10 | CLOSED — shipped US-ADM-011 workflow runtime (011a/b/c), Training & Benefits (US-TRN-001/002/003), Redis command-spans, RLS flip-prep (ISSUE-268/269/277) + flake fix (ISSUE-275). | [archive/COMPLETION-PLAN-2026-07-10.md](archive/COMPLETION-PLAN-2026-07-10.md) |
| 2026-07-06 | CLOSED — shipped P1–P3 (DataProtection, 8 findings, OTel/health/cache) + the RLS build (flag OFF) + US-ADM-011a + reconciliation Part II (Themes A–M). | [archive/COMPLETION-PLAN-2026-07-06.md](archive/COMPLETION-PLAN-2026-07-06.md) |

_Other closed one-off plans (blocked-TC re-exec/remediation, blocker-verification, fix-findings) also live in [`archive/`](archive/); dated QA reports/snapshots live in [`../reports-archive/`](../reports-archive/)._

## ✅ What shipped 2026-07-10 → 2026-07-11 (closed plan)
- **US-ADM-011 workflow runtime epic** — 011a (#238) · 011b parallel+SLA+notifs (#239) · 011c delegation + Attendance/
  Overtime/Offer wiring + read API (#240).
- **Training & Benefits** — US-TRN-001 catalog/enrol (#241) · 002 benefit plans (#242) · 003 eligibility/enrol (#243).
- **Redis command-spans** shared instrumented multiplexer (#245). **agent-config-guards** (#237).
- **RLS flip-prep + validation:** ISSUE-268 notification/session GUC (#244) · ISSUE-269 payslip long-tx split (#246) ·
  local RLS-on validation NO-GO→**GO** + `roles.sql` fix + findings (#247) · **ISSUE-277** per-request-tx → session-scope
  `TenantGucConnectionInterceptor` (#248, the critical flip-blocker). **ISSUE-275** test-flake stabilized (#249).

---

## ⚠ Reality check before diving in
The ledger holds **244 OPEN findings (14 HIGH · 78 MED · 152 LOW)**. Many LOW/MED date to the June QA arc and some may
be **stale** (fixed-but-not-re-verified, or test-env/persona artifacts). **Do a triage-verify pass on the HIGH/MED band
first** (`/verify-fix` or a quick re-exec) before scheduling fixes — don't assume all 244 are live. Two ledgers are
**stale and must be reconciled** (P0). Line anchors below are into `docs/QA/TEST-FINDINGS.md`.

## ▶ Remaining work — priority order

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
- **SSO (real gaps):** US-AUTH-012 (per-tenant SSO config), US-AUTH-016 (SSO enforcement / break-glass / admin-consent)
  — also the 5 `[b]` BLOCKED SSO test cases (US-AUTH-011/012/013/014/016).
- US-PRF-011 (calibration workspace), US-ADM-012 (plan/module governance enforcement), US-PLT-004 (observability).

### P7 — LOW tail (152) + small tidies
- Batch-triage the 152 LOW by module (line anchors in the survey). Quick self-contained ones: **ISSUE-270** (Training/
  Benefits notification category), **ISSUE-274** (custom `IEFCacheServiceProvider` for EF-cache Redis spans),
  **DecideCoreAsync `ChangeTracker.Clear()`** tidy on the onApproved-failure path (`WorkflowRuntimeService.cs:309-315`).
- Deferred/feature-blocked (park at decision-gate): ISSUE-021 (no SalaryGrade entity), BUG-056 (no goal-finalize seam).
  WONTFIX for reference: ISSUE-209/250/265.

## 🔒 Standing rules (carry forward)
NEW-TENANT-TABLE RLS RULE (every new tenant_id table adds its dormant `tenant_isolation` policy in-migration) · **RLS
GUC is set by `TenantGucConnectionInterceptor` on connection open (NOT a request-wide tx)** · cache = read + auto-evict,
tenant-prefixed · RLS config-gated + reversible, committed OFF · BUG-068/252/264 retry-vs-tracked-state (manual tx under
`EnableRetryOnFailure` → wrap in `CreateExecutionStrategy().ExecuteAsync` + detach on rollback) · verify branch before
commit, no `--no-verify` · payroll approval stays on its bespoke `PayrollApprovalService` (converge later = a separate
US-PAY-XXX story) · **the full `dotnet test` gate is now reliable** (xUnit `maxParallelThreads:4`).

## Working method (unchanged)
One `feat/`|`fix/` branch per story/cluster off fresh `test/local-subdomains` → map seams (Explore) → LOCKED spec →
parallel backend-dev + frontend-dev on disjoint paths against a pinned contract → verify FULL suite on Postgres (Docker
up) + read-only auditors (integration-enforcer + test-authenticator) → commit / PR / merge each before the next.
Security/RLS-touching changes: re-run the local RLS-on validation (throwaway Postgres; method in Rls/README §runbook).
Auto-heal out-of-lane flags into this plan + `TEST-FINDINGS.md`.
