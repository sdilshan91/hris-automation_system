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
| **2026-07-13 (c) — ▶ ACTIVE** | **DEC-2 + DEC-1 shipped (#276, #277).** **#276 (DEC-2, doc-only):** encryption key-rotation SOP runbook at `HRM.Infrastructure/Security/README.md` (overlap rotation + re-encrypt-backlog + verify-SQL); closed the deploy-gate; surfaced "no bulk re-encrypt job yet" → P7. **#277 (DEC-1):** dedicated `Reports.View.All`/`Reports.View.Team` taxonomy replacing the borrowed cross-module scope signal; both resolvers switched (Team now requires the explicit perm + a direct report — deliberate tightening), behavior-preserving add-only seed, Recruiter over-grant corrected, FE roles-catalog + perf-gate cleanup. BE 3685/3685 + FE 3763/3763; auditors CONNECTED+AUTHENTIC. **Auto-healed:** ISSUE-290 (perf-gate dead `Performance.Read.*` strings — pre-existing FE↔BE drift, needs-verify), ISSUE-291 (**⚠ release-note:** custom roles relying on old data-derived team scope drop to self-scope; built-ins auto-backfilled). **User decisions this session:** ClamAV=full-wire+require-daemon (caveat pending), key-rotation SOP=done, Reports perm=done. | _(this file)_ |
| **2026-07-13 (b)** | **P2-2 MED cluster started — ISSUE-226 (offer stored-XSS) shipped (#275).** Sanitize-on-write in `OfferService.GenerateAsync` for the 3 recruiter free-text fields, reusing the already-DI-registered `IHtmlSanitizer` (Ganss.Xss) — no new package; mirrors `VacancyService`. Explore corrected the ticket premise (PDF path is a QuestPDF literal-glyph sink, not HTML — never exploitable; real surface = JSON read path the FE renders; portal projection covered at source). Added 2 material auditor-recommended arms myself (integration DI+MediatR pipeline; `javascript:`/style allow-list pin). BE **3671/3671** green; Offer 50/50; integration-enforcer CONNECTED, test-authenticator AUTHENTIC. **User decisions captured** (see gate list): ClamAV = full-wire+require-daemon (with prod-fail-closed / dev-test-CI-green caveat) · key-rotation SOP = write runbook now · Reports.View.Team = build dedicated permission. Out-of-lane filed: `OfferLetterTemplate.Substitute` latent foot-gun (P7). | _(this file)_ |
| **2026-07-13 — ▶ RESUME POINT (session stop)** | **13-PR continued-execution arc merged (#262–274); prioritized queue's whole top band cleared.** After the P1 arc, ran the refreshed 🔝 queue top-down: **#262** ISSUE-245 (Karma verify-close) · **#263** BUG-243 (last P1 HIGH — Perf FE re-model + SaveGoals) · **#264** ISSUE-287 (mock) · **#265/#266/#268** US-NTF-006 Phases 6/7/8 (**notification delivery COMPLETE**) · **#267** ISSUE-288 (employee self-sign-off) · **#269** BUG-281 (write-time audit PII redaction) · **#270** P3-2 (Redis JWT denylist, fail-open) · **#271** P2-2a (ISSUE-195 HR-report manager-scope + cache-isolation; BUG-120=dup) · **#272** BUG-082 (audit opt-in→opt-OUT; ~50 entities now audited; fixed latent ActorEmployeeNo overflow) · **#273** P3-4 (PII-at-rest AES-256-GCM field encryption — ISSUE-134 + ISSUE-150-enc) · **#274** P3-3 (Redis permission cache, fail-open). BE gate green **3670/3670**; every PR passed integration-enforcer + test-authenticator. **User stopped here to review the batch + re-prioritize.** Remaining work + consolidated out-of-lane/gated items re-sorted in the 🔝 queue below. | _(this file)_ |
| **2026-07-12 (e)** | **P2-1 US-NTF-006 scope corrected + Phases 6 & 7 shipped.** Audit found delivery infra + 8 DI module seams already `Real*`-wired (NOT "13 LogOnly seams"); genuine remainder = inline deferred-notify sites. **Phase 6 (#265):** attendance/overtime/regularization (8 sites, new `IAttendanceNotificationService`). **Phase 7 (#266):** EmployeeStatus probation/reassignment + DocumentExpiry/ScheduledReport jobs (4 sites, new `ICoreHrNotificationService`, cross-tenant per-item TenantId). Both: 3575→3590 green, auditors WIRED+AUTHENTIC. **Auto-healed: Phase 8 tail (P2-1c)** = LeaveReportExportJob:70 + BulkEmployeeImportService:316 (2 sites the initial Explore missed). | _(this file)_ |
| **2026-07-12 (d)** | **P1-5c BUG-243 shipped (LAST P1 HIGH) + auto-heal.** FE re-model of 6 Performance services to the real routes (cycleId via `cycles/active`+`switchMap`) + the bulk `SaveGoals` BE endpoint (8 authentic tests). Verified FE 3759 / BE 3545 green (2 reds = pre-existing ISSUE-287). Auto-healed **ISSUE-287** (test-double staleness → new **P1-6, do next**), **ISSUE-288** (HIGH employee self-service sign-off BE gap → P5-5), **ISSUE-289** (LOW). **All 14 P1 HIGH now addressed.** Next: P1-6 (green BE gate) then P2 (US-NTF-006). | _(this file)_ |
| **2026-07-12 (c)** | **ISSUE-245 verify-closed (no code).** Full Angular Karma suite green ×2 runs (**3757/3757**, deterministic order) on `test/local-subdomains` — the ~26 red specs were cleared incidentally by the P1 merges (#254–261). FE gate now trustworthy. Next: BUG-243 (last P1). | _(this file)_ |
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

### 🔝 Active execution queue (re-prioritized 2026-07-13 resume point; execute top-down, PR+merge each; auto-heal out-of-lane into this queue)

**✅ CLEARED — the entire prior top band (13 PRs #262–274) shipped:** ISSUE-245, BUG-243 (last P1 HIGH), ISSUE-287, US-NTF-006 delivery (Phases 6/7/8), ISSUE-288, BUG-281, P3-2 JWT denylist, P2-2a RBAC-scope (ISSUE-195; BUG-120=dup), BUG-082 audit-all, P3-4 PII-at-rest encryption, P3-3 Redis permission cache. **BE gate green 3670/3670; all merged, all audited.** Per-item detail in the tracker table below.

**⚠ DEPLOY-GATES & NEEDS-DECISIONS (surface before the relevant deploy / to the human):**
- **[OPS, HIGH] Encryption key (P3-4/#273):** prod/staging MUST set `Encryption__Keys__hrm-field-key-1` (base64 32-byte) via env/secret before app start, or it FAIL-FASTS by design. Dev + tests carry a key.
- **[OPS] RLS prod flip** — the flag is committed OFF; the actual staging/prod flip is the user's ops step (README §3b runbook).
- **[DECISION ✅ DONE 2026-07-13, #276] Encryption key-rotation SOP** (P3-4) — runbook written at `src/backend/HRM.Infrastructure/Security/README.md` (DEC-2). Follow-up surfaced: no bulk re-encrypt job yet (parked P7).
- **[DECISION ✅ RESOLVED 2026-07-13] `Reports.View.Team` dedicated permission** (ISSUE-195) — user chose **build the dedicated perm** → queued as **DEC-1** (net-new perm + repoint 6 HR builders).
- **[DECISION] ISSUE-021** (no SalaryGrade entity) · **BUG-056** (no goal-finalize seam) — feature-gated.

**▶ REMAINING IMPLEMENTABLE — ranked severity × blast-radius × user-value:**
1. **P2-2 MED functional cluster** (real user/security bugs — the highest-value remaining band). **✅ SHIPPED: ISSUE-226 offer stored-XSS (#275), DEC-1 Reports.View.* perm (#277).** Remaining sub-clusters, fan out one per branch: **audit gaps** BUG-081/083/085, ISSUE-120/200 (PII-read audit, onboarding audit); **payroll semantics** ISSUE-153/154/156/157/165/166/167/170/177/178/180 + BUG-061/062/074/079; **a11y** BUG-108/109/110/112; **UTC-boundary** BUG-245/246; **Redis** BUG-115/116; **recruitment/perf gaps** ISSUE-133/137/140/141/145/232. **✅ ISSUE-290 (perf-gate dead-strings) RESOLVED #278** — was a real functional break (dashboard unreachable), fixed `Performance.Read.*`→`View.*` + non-mocked regression spec. **✅ ISSUE-291 (DEC-1 rollout) RESOLVED #279** — user chose backfill+note: idempotent custom-role `Reports.View.*` startup backfill + `docs/DEV/UPGRADE-NOTES.md`; 7 tests incl. cross-tenant arm.
2. **P2-1d — AttendanceSummaryExportJob notify** (US-ATT-010 FR-7 export-ready; MED) — auto-healed from Phase 8, same Site-B pattern (thread `requestedByUserId` + a report-ready event). Small; completes the export-notify family.
3. **P3-1 ClamAV** (security; **infra-gated**) — **user DECIDED: full wire + require daemon.** Wire `ClamAvVirusScanner` (+ nClam pkg) as the default. **⚠ Orchestrator caveat to honor at build time:** a HARD daemon requirement with no fallback breaks local dev + the xUnit gate + CI (none run a ClamAV daemon). Implement as **required/fail-closed in Production** but keep dev/test/CI green via a config gate (e.g. `VirusScanning:Mode=ClamAv|AllowWithLog`, default ClamAv only in Prod) OR a CI ClamAV service container. **Show the exact shape to the user before building** (per their request). The LIVE scan still can't be end-to-end-tested here without a daemon.
4. **P0-2 Missing TC suites** (qa-engineer, report-only) — IEEE-829 TCs for the stories shipped this arc (Training/Benefits, US-ADM-011 workflow, US-NTF-006 delivery) + ISSUE-273. Low-risk, improves traceability, ships no functional change.
5. **P7 LOW tail** (~150 LOW) — batch-triage by module. Includes the small auto-healed items: ISSUE-289 (sign-off structured-notes fields unused), the P3-3 minor test gaps (version-key TTL-refresh + malformed-JSON→miss arms), ISSUE-270/274/280/282, ChangeTracker.Clear tidy.

**Still-open partials / verify tasks (not net-new):**
- **ISSUE-150 (partial)** — the compensation-at-rest ENCRYPTION half is DONE (#273); the `currentCompensation` SNAPSHOT/join-from-Payroll seam is still unbuilt (separate feature, FR-5 comp comparison + AC-5 gate can't be exercised until then).
- **BUG-003 family** — statically LIKELY-FIXED; formal closure still wants a live `/verify-fix --iso` re-run (park as a verify task).

> **Gated/parked (not in the active queue):** ISSUE-285 (dashboard SLA — birthday-index migration decision + k6 rig), P4-1 RLS code tail (ISSUE-269 long-tx), P5 net-new stories (US-ADM-012 / US-PRF-011 / US-PLT-004 / SSO US-AUTH-012/016 + 5 [b] TCs), P6 deferred FE (ISSUE-271/272/267 workflow-viewer + eligible-plans UI).

| Item | Priority | Scope (findings/story) | Status | PR | Notes |
|------|----------|------------------------|--------|----|-------|
| P0-1 Reconcile ledgers | P0 | BA/STATUS.md + TEST-STATUS.md drift (ADM-011, TRN-001/002/003 shipped) | MERGED | #253 | done |
| P0-2 Missing TC suites | P0 | TC-TRN-001/002/003, TC-ADM-011-*, US-NTF-006 delivery + ISSUE-273 | TODO | — | qa-engineer |
| P1-0 Re-verify body-conflicts | P1 | BUG-003(CRIT)/086/002/005 | VERIFIED | — | all 4 statically LIKELY-FIXED (code-grounded); BUG-003 family formal closure needs a live `/verify-fix --iso` re-run (park as verify task); 086/002/005 fixed at code layer, stale "STILL PRESENT" wording |
| P1-1 RBAC payroll lockouts | P1 | BUG-060(Payroll)/071/077 | PR#254 | #254 | seed fix; 42/42 unit green; merging |
| P1-2 OT overpay | P1 | BUG-078 (OT base EARNINGS→BASIC) + **BUG-280** (same defect in statutory EPF/ETF, auto-healed) | PR#255 | #255 | Code-based BASIC resolution; 10 unit + 29 payroll integ green on Postgres; merging |
| P1-3 Payroll audit emitters | P1 | **BUG-080** (7 payroll audit actions) | PR#256 | #256 | 7 emitters + 8 authentic tests; BUG-084 stale→RESOLVED (BUG-241); BUG-082→P3-5; ISSUE-282(P7); merging |
| P3-5 Audit-all | P3 | **BUG-281** (write-time PII redaction) → **BUG-082** (opt-out audit-all) | MERGED | #269, #272 | BOTH DONE. BUG-281 (#269) write-time mask; **BUG-082 (#272)** interceptor flipped opt-in→opt-OUT (47 `IAuditExempt` = explicit-writer + high-volume; ~50 business entities now audited) + fixed a latent `ActorEmployeeNo` varchar(50) overflow; 3638/3638; auditors WIRED+AUTHENTIC |
| P1-4a Attendance N+1 | P1 | **BUG-125** + **BUG-283** (shift-resolution N+1, shared resolver) | PR#257 | #257 | ~15k round-trips→3; no migration; 40/40 green; merging |
| P1-4b Leave report N+1 | P1 | BUG-124 (batch entitlement resolution) | PR#258 | #258 | ~325k round-trips→2; no migration; 89/89 green; merging |
| P1-4c Dashboard scale | P1 | BUG-123 (hot-path projection) | PR#259 | #259 | attendance-today/live-board projection; folds ISSUE-284#2; 56/56 green; remainder→ISSUE-285; merging |
| P4-perf Dashboard SLA remainder | P4 | **ISSUE-285** (split from BUG-123) | GATED | — | birthday-index MIGRATION (decision) + widget parallelism (IDbContextFactory) + k6 p95/50k confirmation |
| P1-5a FE session restore | P1 | BUG-097 (silent refresh on bootstrap) | PR#260 | #260 | FE-only; chained APP_INITIALIZER; 17/17 auth spec green; merging |
| P1-5b Custom-fields crash | P1 | BUG-100 | RESOLVED | 46d7ebb2 | stale — already fixed (shape-drift); closed |
| P1-5c Perf FE routes | P1 | BUG-243 (mostly FE re-model) + saveGoals BE gap | MERGED | #263 | 6 FE services re-modeled (cycleId via cycles/active resolver) + bulk SaveGoals BE endpoint (8 tests); FE 3759 green, BE 3545 (only 2 pre-existing ISSUE-287 reds); auto-healed ISSUE-287/288/289 |
| P1-6 Restore green BE gate | P1 | **ISSUE-287** (entitlement mock not stubbed for BUG-124 batch resolver → 2 red HrLeaveAttendanceReport tests) | MERGED | #264 | stubbed the batch resolver on the mock; class 7/7 green; BE gate green again |
| P5-5 Employee self sign-off | P5→P1 | **ISSUE-288** (HIGH — caller-scoped BE self endpoint for employee acknowledge/dispute) | MERGED | #267 | 3 `reviews/cycles/active/me/*` self endpoints (Read.Self, resolve caller employee + active cycle) + FE rewire; BE 3596/3596, FE 3763 green; auditors WIRED+AUTHENTIC; unblocked US-PRF-006 AC-3 |
| P1-5d Employee↔Location | P1 | BUG-113 (full-stack) | PR#261 | #261 | BE+FE; profile-DTO prefill healed inline; count 0→1; 473 BE + 105 FE green; ISSUE-286 parked; merging |
| P2-1 Notification delivery | P2/P3 | US-NTF-006 — **CORRECTED SCOPE (2026-07-12 audit):** delivery infra + all 8 DI-level module seams are ALREADY `Real*`/dispatcher-wired (Phases 2a–5b shipped). NOT "13 LogOnly seams." The genuine remainder = 11 **inline deferred-notify sites** in modules that never got a `Real*NotificationService` | WIP | — | split into Phase 6 (attendance-family) + Phase 7 (core-hr + 2 jobs) below |
| P2-1a NTF Phase 6 attendance-family | P2 | Attendance/Overtime/Regularization notify-halves (8 sites: AttendanceService:210/504, OvertimeService:140/234/373/425, RegularizationApprovalService:193/345) → new `IAttendanceNotificationService`/`RealAttendanceNotificationService` + 8 `AttendanceAlerts` catalog events | WIP | — | delegating backend-dev |
| P2-1b NTF Phase 7 core-hr + jobs | P2 | EmployeeStatusService:368 (probation-end HR) + :457 (reassignment alert); DocumentExpiryNotificationJob:74; ScheduledReportJob:96 | MERGED | #266 | new ICoreHrNotificationService + 4 catalog events; cross-tenant per-item TenantId; 3590/3590 green; auditors WIRED+AUTHENTIC |
| P2-1c NTF Phase 8 tail | P2 | **LeaveReportExportJob:70** + **BulkEmployeeImportService:316** | MERGED | #268 | `requestedByUserId` threaded through `ILeaveReportExportJob`; BulkImport→`InitiatedBy` email; 2 catalog events (`leave_report_ready`/`bulk_import_completed`); BE 3609/3609 + new leave-side dispatch test (InternalsVisibleTo HRM.Api→HRM.Tests); auditors WIRED+AUTHENTIC |
| P2-1d NTF export-ready tail | P2 | **AttendanceSummaryExportJob** (US-ATT-010 FR-7 "download link sent via notification" still DEFERRED) — 3rd export-ready notify gap, same Site-B pattern (thread requestedByUserId + dispatch a report-ready event) | TODO | — | auto-healed from Phase 8 backend-dev OUT-OF-LANE; then all export-ready notifies done |
| P2-2a RBAC-scope | P2 | ISSUE-195 (HR-report manager-scope) + BUG-120 (Directory authz — dup of ISSUE-018, already fixed #147) | MERGED | #271 | 6 HR builders scoped via shared resolver + cache-key folds scope + cache-leak test; 3635/3635; auditors WIRED+AUTHENTIC |
| P2-2 sec ISSUE-226 XSS | P2 | ISSUE-226 (offer free-text stored-XSS defense-in-depth) | MERGED | #275 | sanitize-on-write via existing `IHtmlSanitizer` (Ganss.Xss); ticket PDF-premise corrected (QuestPDF `.Text()` = non-HTML sink); 2 auditor arms added (integration DI+MediatR + `javascript:`/style pin); 3671/3671; auditors CONNECTED+AUTHENTIC |
| P2-2 MED clusters (rest) | P2 | audit gaps; payroll semantics; a11y; UTC; Redis; recruitment/perf | TODO | — | fan-out one sub-cluster per branch; ISSUE-226 done; BUG-082 broaden-audit now unblocked |
| DEC-1 Reports.View.* perm | P2 | ISSUE-195 follow-up — dedicated `Reports.View.All`/`Reports.View.Team` (user DECIDED: build both, wire Team) | MERGED | #277 | self-describing taxonomy; both resolvers gate All→`Reports.View.All` + Team→`Reports.View.Team`+direct-report (was data-derived); behavior-preserving seed (add-only reconcile backfills built-ins), Recruiter over-grant corrected; FE catalog + perf-gate cleanup; BE 3685/3685 + FE 3763/3763; auditors CONNECTED+AUTHENTIC. Auto-healed **ISSUE-290** (perf-gate dead `Performance.Read.*` strings) + **ISSUE-291** (custom-role team-scope drop — release-note decision) |
| DEC-2 Key-rotation SOP | P3 | P3-4 follow-up — encryption key rotation/retention runbook (user DECIDED: write now) | MERGED | #276 | doc-only; runbook at `src/backend/HRM.Infrastructure/Security/README.md` (overlap rotation + re-encrypt-backlog + verify-SQL). Surfaced a follow-up: no bulk re-encrypt job exists yet (needed to fully rotate OFF a key) → P7 |
| P2-3 Red FE base | P2 | ISSUE-245 (~26 pre-existing Angular spec fails) | VERIFIED | — | RESOLVED 2026-07-12 no-code — cleared by #254–261; full Karma suite 3757/3757 green ×2 runs, deterministic order; FE gate trustworthy |
| P3-1 ClamAV | P3 | AllowWithLogVirusScanner → ClamAvVirusScanner | TODO | — | security |
| P3-2 JWT denylist | P3 | NoOpSessionRevoker → Redis denylist | MERGED | #270 | Redis "revoked-before" cutoff + OnTokenValidated fail-open + iat claim; DI-gated Redis/NoOp; 3630/3630; auditors WIRED+AUTHENTIC. Impersonation-end already covered by ImpersonationEnforcementMiddleware (not the per-user cutoff — would over-revoke) |
| P3-3 Permission cache→Redis | P3 | InMemoryPermissionCache (NFR-2) | MERGED | #274 | `RedisPermissionCache` over the shared multiplexer (P3-2 pattern); tenant-version-marker invalidation (INCR, no SCAN); 15-min TTL; FAIL-OPEN (Redis blip → cache miss → DB resolve, never denies); DI-gated Redis/InMemory; 3670/3670; auditors WIRED+AUTHENTIC. All perm-mutation paths already per-user-invalidate. Minor uncovered (LOW): version-key 30d TTL-refresh + malformed-JSON→miss (shares the tested catch). |
| P3-4 PII at rest | P3 | ISSUE-134 (PIP) + ISSUE-150 (Recommendation comp) | MERGED | #273 | **app-side AES-256-GCM** (user decision, not pgcrypto) via EF value converters; 8 fields (Pip 3 + Recommendation comp 5); config key-ring rotation-ready; `numeric→text` migration + idempotent DbInitializer back-fill; 23/23 Postgres + full suite green; auditors WIRED+AUTHENTIC. **⚠ DEPLOY-GATE (ops):** prod/staging MUST set `Encryption__Keys__hrm-field-key-1` (base64 32-byte) via env/secret or the app fail-fasts (dev+tests carry a key). Rotation SOP = a follow-up needs-decision. Budget pool amounts intentionally NOT encrypted. |
| P4-1 RLS code finalize | P4 | US-PLT-002 code parts (prod flip = ops) | TODO | — | ISSUE-269 long-tx tail |
| P5-1 US-ADM-012 | P5 | plan/module governance enforcement | TODO | — | net-new |
| P5-2 US-PRF-011 | P5 | calibration workspace | TODO | — | net-new |
| P5-3 US-PLT-004 | P5 | observability NFRs | TODO | — | net-new |
| P5-4 SSO | P5 | US-AUTH-012/016 + 5 [b] SSO TCs | TODO | — | net-new; some ops-gated |
| P6-1 Workflow viewer FE | P6 | ISSUE-272/267 (US-ADM-011 FR-12 UI) | TODO | — | BE-complete |
| P6-2 Eligible-plans UI | P6 | ISSUE-271 (US-TRN-003 AC-8) | TODO | — | BE-complete |
| P7-1 LOW tail | P7 | 82–152 LOW, batch by module | TODO | — | ISSUE-270/274/**280**/**282** (auto-healed: PayrollSlipLine Code; job-path audit Postgres arm), ChangeTracker.Clear tidy, **`OfferLetterTemplate.Substitute` raw `string.Replace`** (auto-healed from #275 — latent XSS foot-gun IF a future HTML/email offer template reuses it; harmless today since the renderer ignores it, values now sanitized on write), **bulk field-encryption re-encrypt job** (auto-healed from #276 — none exists; needed to fully rotate OFF an encryption key per the SOP; low-urgency until first real rotation), **`Performance.Read.*` docstring sweep** (auto-healed from #278 — 6 sibling perf files still name the dead `Performance.Read.*` perms in comments; comment-only, prevents the next copy-paste-into-a-live-gate that caused ISSUE-290) |
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
