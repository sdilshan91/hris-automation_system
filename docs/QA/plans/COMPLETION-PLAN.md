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
| **2026-07-12 (b)** | **P1-3 shipped + triage/auto-heal.** BUG-080 fixed (7 payroll audit emitters, 8 authentic tests). BUG-084 found stale→RESOLVED (already fixed under BUG-241). BUG-082 re-scoped + gated: filed **BUG-281** (write-time PII redaction) as its blocker → moved BUG-082 to P3-5. Auto-healed **ISSUE-282** (job-path audit Postgres arm) + a PayslipJobRls test-fidelity fix. | _(this file)_ |
| **2026-07-12** | **P1-2 shipped + auto-heal.** Fixed BUG-078 (OT rate off gross not basic). Auto-healed 2 out-of-lane discoveries: **BUG-280** (HIGH — identical defect over-deducted statutory EPF/ETF; fixed same PR #255) and **ISSUE-280** (LOW — carry `Code` on `PayrollSlipLine`; parked P7). Re-sorted plan. | _(this file)_ |
| **2026-07-11 (b)** | **Dev-plan execution refresh.** Ran the P0 triage-verify pass — reconciliation confirmed the **14 open HIGH** figure (heading-by-heading ledger parse); flagged BUG-003/086/002/005 as RESOLVED-token-but-body-conflict (re-verify). Added the **📊 Item-wise Status Tracker** (below) as the execution ledger; began executing P0→P7 one branch+PR per item. | _(this file)_ |
| **2026-07-11 (a)** | **Current active plan.** Rolled over from 07-10 (all it carried shipped). Rebuilt from a full findings/ledger/RLS survey → P0 ledger reconcile + missing TC suites … P7 LOW tail (body below). | _(this file)_ |
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

**Triage-verify result (2026-07-11b):** heading-by-heading reconciliation **confirms 14 genuinely-OPEN HIGH** (BUG-060-Payroll,
071, 077, 078, 080, 082, 084, 097, 100, 113, 123, 124, 125, 243) — the P1 list stands. **Re-verify-before-trusting (RESOLVED
token but body says "STILL PRESENT"):** **BUG-003 (CRIT** cross-tenant settings write — but memory's 2026-07-03 note says
CLOSED via #119 `TenantAccessGuardMiddleware`, so body is likely stale), **BUG-086** (HIGH, Leave 'Accrued' enum 500 — dup of
BUG-037), **BUG-002/BUG-005** (MED, graceDays default + localization). These 4 lead P1.

## 📊 Item-wise Status Tracker (execution ledger — update per branch+PR+merge)

> Legend: `TODO` · `WIP` (branch cut) · `PR#nnn` (PR open) · `MERGED` · `VERIFIED` (post-merge re-test green) · `BLOCKED` ·
> `PARKED` (decision/ops-gated). One row = one branch. Findings/IDs in parentheses. Keep this table authoritative;
> re-sort as reality changes.

| Item | Priority | Scope (findings/story) | Status | PR | Notes |
|------|----------|------------------------|--------|----|-------|
| P0-1 Reconcile ledgers | P0 | BA/STATUS.md + TEST-STATUS.md drift (ADM-011, TRN-001/002/003 shipped) | MERGED | #253 | done |
| P0-2 Missing TC suites | P0 | TC-TRN-001/002/003, TC-ADM-011-*, US-NTF-006 delivery + ISSUE-273 | TODO | — | qa-engineer |
| P1-0 Re-verify body-conflicts | P1 | BUG-003(CRIT)/086/002/005 | VERIFIED | — | all 4 statically LIKELY-FIXED (code-grounded); BUG-003 family formal closure needs a live `/verify-fix --iso` re-run (park as verify task); 086/002/005 fixed at code layer, stale "STILL PRESENT" wording |
| P1-1 RBAC payroll lockouts | P1 | BUG-060(Payroll)/071/077 | PR#254 | #254 | seed fix; 42/42 unit green; merging |
| P1-2 OT overpay | P1 | BUG-078 (OT base EARNINGS→BASIC) + **BUG-280** (same defect in statutory EPF/ETF, auto-healed) | PR#255 | #255 | Code-based BASIC resolution; 10 unit + 29 payroll integ green on Postgres; merging |
| P1-3 Payroll audit emitters | P1 | **BUG-080** (7 payroll audit actions) | PR#256 | #256 | 7 emitters + 8 authentic tests; BUG-084 stale→RESOLVED (BUG-241); BUG-082→P3-5; ISSUE-282(P7); merging |
| P3-5 Audit-all (gated) | P3 | **BUG-281** (write-time PII redaction) → then **BUG-082** (opt-out audit-all) | TODO | — | auto-healed; BUG-082 BLOCKED-BY BUG-281 (else cleartext-PII leak) |
| P1-4 Scale / SLA | P1 | BUG-123/124/125 | TODO | — | N+1 + query perf |
| P1-5 FE session/contract | P1 | BUG-097/100/113/243 | TODO | — | Angular; 243=Perf routes |
| P2-1 Notification delivery | P2/P3 | US-NTF-006: 13 LogOnly* → RealNotificationDispatcher (ISSUE-221/228/214) | TODO | — | biggest surface |
| P2-2 MED clusters | P2 | ISSUE-195/BUG-120 RBAC-scope; audit gaps; payroll semantics; a11y; UTC; Redis | TODO | — | fan-out after P2-1 |
| P2-3 Red FE base | P2 | ISSUE-245 (~26 pre-existing Angular spec fails) | TODO | — | do early — gates FE |
| P3-1 ClamAV | P3 | AllowWithLogVirusScanner → ClamAvVirusScanner | TODO | — | security |
| P3-2 JWT denylist | P3 | NoOpSessionRevoker → Redis denylist | TODO | — | security |
| P3-3 Permission cache→Redis | P3 | InMemoryPermissionCache (NFR-2) | TODO | — | scale |
| P3-4 PII at rest | P3 | US-PLT-005 pgcrypto (Recommendation/Pip/Budget) | TODO | — | plan-HIGH |
| P4-1 RLS code finalize | P4 | US-PLT-002 code parts (prod flip = ops) | TODO | — | ISSUE-269 long-tx tail |
| P5-1 US-ADM-012 | P5 | plan/module governance enforcement | TODO | — | net-new |
| P5-2 US-PRF-011 | P5 | calibration workspace | TODO | — | net-new |
| P5-3 US-PLT-004 | P5 | observability NFRs | TODO | — | net-new |
| P5-4 SSO | P5 | US-AUTH-012/016 + 5 [b] SSO TCs | TODO | — | net-new; some ops-gated |
| P6-1 Workflow viewer FE | P6 | ISSUE-272/267 (US-ADM-011 FR-12 UI) | TODO | — | BE-complete |
| P6-2 Eligible-plans UI | P6 | ISSUE-271 (US-TRN-003 AC-8) | TODO | — | BE-complete |
| P7-1 LOW tail | P7 | 82–152 LOW, batch by module | TODO | — | ISSUE-270/274/**280**/**282** (auto-healed: PayrollSlipLine Code; job-path audit Postgres arm), ChangeTracker.Clear tidy |
| — ISSUE-021 / BUG-056 | park | SalaryGrade entity / goal-finalize seam | PARKED | — | decision-gated |

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
