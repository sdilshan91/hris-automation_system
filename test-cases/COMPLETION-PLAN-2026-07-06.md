# HRM — Completion Plan (all pending work)  ·  2026-07-06

> Consolidates everything left after the fix campaign (PRs #119–180, all merged into `test/local-subdomains`).
> Sources: the `TEST-FINDINGS.md` open items **de-noised** (most "OPEN" MED items are actually merged-not-closed),
> plus a fresh **codebase latent-TODO scan** that surfaced work never captured as a QA finding. Phased by
> value + dependency; each phase is independently shippable.
>
> **State today:** 0 open CRIT, 0 open HIGH findings.

---

## ✅ STATUS — Phase 0 bookkeeping COMPLETE (2026-07-06)
- **Ledger close-out — DONE** (PR #182, merged): 29 Waves-C/D/E findings flipped `OPEN → RESOLVED` with PR# (#168–178). Ledger tally now **OPEN 77 / RESOLVED 110** (was 106/81). Genuinely-open kept OPEN: ISSUE-243, ISSUE-244, BUG-058.
- **Final full-suite verification — DONE**: merged `test/local-subdomains` is **2924/2924 green** (0 failed, Docker up). *(An earlier 42-fail run was purely Docker-being-down — all Testcontainers/Postgres tests — not code.)*
- **User-story reconciliation — DONE** (PR #181, merged): `STATUS.md` now carries a per-story **Deferred-AC** table for ~40 `[x]` stories, plus net-new stories — US-NTF-006 (delivery layer), US-ADM-011 (workflow runtime), stubs US-ADM-012 / US-PRF-011 / US-PLT-004 / US-PLT-005, and a new **Training & Benefits** module (US-TRN-*). Theme-K items attached to their existing stories.
- **Remaining Phase-0 item:** the code-scan **[NEW]** items (plaintext-MFA, EXIF, UTC-timezone, etc.) are catalogued in Part II + covered by the BA stories, but **not yet filed as individual `TEST-FINDINGS.md` entries** — optional, since Part II is now the system of record for them.

**⇒ Execution now starts at the "verify-first" pass, then Phase 1.**

---

## ▶ RESUME POINT — new session starts HERE (updated 2026-07-08)
- **Base:** `test/local-subdomains` @ **HEAD `e862198b`** (everything through PR #196 merged). **0 open PRs.** Working tree clean.
- **Base health:** backend build green (exit 0); the full `dotnet test` re-verify was **in-progress at handoff** — **RE-RUN `dotnet test HRM.sln` (Docker up) + `ng test` first** to confirm the merged base (last known: backend 2998 green, FE 3647 green on the pre-merge branches; #195 combined #193's migrations + timezone).
- **Working method (unchanged):** one `fix/{cluster}` branch per item off fresh `test/local-subdomains` → parallel `@backend-dev` + `@qa-engineer` (non-overlapping paths) → gate on the **FULL** suite → commit → PR → user merges → next. **Auto-heal is ACTIVE** (rule #6 / `/auto-heal`): file every `OUT-OF-LANE:` flag to `TEST-FINDINGS.md`, fold into this plan, re-prioritize. Avoid stacking branches that touch the same files/migrations as an open PR.
- **▶ NEXT, in priority order:**
  1. **Phase 1c — JWT signing-key rotation/overlap** (`JwtService`/`AuthService`; a key-ring so rotation doesn't invalidate live tokens).
  2. **Finish tenant-tz across attendance** — **BUG-245** (`AttendanceDashboardService`), **BUG-246** (`AttendancePayrollService` monthly agg), **BUG-247** (`RegularizationApprovalService` recompute) — reuse the merged **`TenantClock`** helper; same UTC-no-op discipline.
  3. **Phase 5** — Testcontainers coverage for the `TenantDataDeletionService` tx path (BUG-068 class) + the **Training & Benefits** module (zero coverage).
  4. **Phase 6** — LOW cosmetic sweep + **Theme-L dead-code** (`EmployeeFieldAuditLog` query filter, `IPipCheckpointScheduler`/`GeneratePortalLinkCommand` orphans, 2 un-`AddScoped` Hangfire jobs).
  5. **DECISION-GATE** (needs the user): Phase 3 features (**Notifications delivery** US-NTF-006, **workflow runtime** US-ADM-011, the **BUG-244** Performance endpoints incl. the `cycles/current` resolver) + Phase 4 infra (**Redis, RLS enablement, OTel**, and **ISSUE-247** DataProtection key persistence).

## 🔄 LOOP EXECUTION PROGRESS — MERGED (2026-07-07/08)
- ✅ #184 verify-first (2 ghosts STALE; Perf routes LIVE→BUG-243) · #185 BUG-243 (6 Perf FE routes) · #186 Theme-G (4 shape bugs) · #187 Ph1a (magic-byte + EXIF + ISSUE-244) · #188 Ph1b-A (auth rate-limit + cache-invalidation + refresh-guard + audit-metadata) · #189 Ph2a (dept leave-coverage + payroll-lock) · #190 US AC-B* · #191 tracking (BUG-244/ISSUE-245/246 + TC stubs) · #192 **auto-heal protocol** · #193 Ph1b-B (MFA encryption + password-history, migrations) · #194 **ISSUE-245 FE suite green (3647/0)** · #195 Ph2b (tenant timezone / ISSUE-065) · #196 absorbed tooling (design-review/retro/security-audit/guardrails/exploratory-QA).

## 🆕 LOOP-DISCOVERED items now tracked (auto-healed into the ledger)
1. **BUG-244 [MED]** — backend half of BUG-243: 7 Performance endpoints never built (360 `saveReviewers`/`getFeedbackForm`/`tracker`, self-assessment `deleteAttachment`, cycle `rating-scales`, recommendation `cycles/completed`, pip `draft`) + **HR-gated `cycles/active` resolver**. → US-PRF-*-AC-B* (#190), TC-PRF-*-B* stubs (#191). **Decision per endpoint: build vs remove dead FE control**; `cycles/current` resolver = highest-leverage. → Phase 3.
2. **ISSUE-245 [MED] — RESOLVED (#194)** — the Angular suite was red on the base (~26 stale specs); now green (3647/0).
3. **ISSUE-246 [LOW]** — EXIF strip skips WebP (ImageSharp 2.1.x). → Phase 6.
4. **ISSUE-247 [HIGH]** — DataProtection key ring is ephemeral/per-instance → MFA secrets won't decrypt across instances/redeploys. → **Phase 4 infra**.
5. **ISSUE-248 [LOW]** — no self-service change-password path; history enforced only on reset. → decision.
6. **ISSUE-249 [MED]** — AutoMapper 13.0.1 NU1903 advisory. → dependency hygiene.
7. **BUG-245/246/247 [MED×3]** — attendance dashboard / payroll-agg / regularization-approval still UTC (siblings of the Ph2b tz fix). → NEXT item #2.
8. **ISSUE-250/251 [LOW×2]** — regularization validator UTC future-check; `TenantClock.LocalToUtc` DST-gap throw. → Phase 6 / decision.

---

## Legend
- **[LEDGER]** already a tracked finding · **[NEW]** surfaced by the code scan, not yet filed · **[OPS]** deployment/config, not code
- Sev = product impact. "Decision" = needs a product/AC/infra call before coding.

---

## PHASE 0 — Bookkeeping & truth  ✅ COMPLETE (2026-07-06)
Make the ledger honest so every later count is trustworthy.

1. ✅ **Close-out** — 29 findings flipped `OPEN`→`RESOLVED` with PR# (#168–178). **PR #182 merged.** Ledger now OPEN 77 / RESOLVED 110.
2. ✅ **Final full-suite verification** — merged base **2924/2924 green** (Docker up). The earlier 42-fail run was Docker-down (Testcontainers only), not code.
3. ◻︎ **File the [NEW] items as findings** — catalogued in Part II + covered by the BA stories (PR #181 merged); individual `TEST-FINDINGS.md` entries **optional** (Part II is their system of record).
- ✅ **US/STATUS reconciliation** (PR #181 merged): Deferred-AC table for ~40 stories + net-new stories (US-NTF-006, US-ADM-011, stubs, Training & Benefits US-TRN-*).

---

## PHASE 1 — Security & privacy hardening  (highest value · mostly no decision)
1. **[NEW · HIGH] Encrypt the TOTP MFA secret at rest** — `AuthService.cs:973-974` stores `MfaSecret` plaintext (`// TODO: encrypt with pgcrypto KEK`). A DB read defeats all MFA. Encrypt with a KEK (pgcrypto or app-layer AES); migrate existing rows. **File as BUG.**
2. **[OPS] Actually enable virus scanning** — the pluggable ClamAV scanner shipped (#177/ISSUE-101) but is **config-gated OFF**; the default is still `AllowWithLogVirusScanner` (accepts every file incl. EICAR). Deploy a ClamAV daemon + set `VirusScanning:ClamAv:Host`. Until then, uploads are unscanned. Document in the deploy runbook.
3. **[LEDGER · MED] BUG-058 resume magic-byte sniffing** — trusts client `Content-Type`. Add real file-type sniffing on the **same** upload path the ClamAV scan now uses (employee docs/photos, resumes, self-assessment attachments).
4. **[NEW · MED] Strip EXIF from uploaded images** — `EmployeeService.StripExifData` (+ `EmployeeDocumentService.cs:407`) is a pass-through stub; GPS/camera PII in photos is stored + served. **File as ISSUE.**
5. **[NEW · MED] Access-token revocation on offboarding** — `NoOpSessionRevoker`: refresh tokens die but issued access tokens live to expiry. Add a JWT denylist / per-request user-active recheck. (Denylist wants Redis → coordinate with Phase 4; a short access-token TTL is an interim mitigation.) **File as ISSUE.**
6. **[LEDGER · LOW] ISSUE-244** drop `resumeStorageKey` from the wire DTO (trivial).
7. **[LEDGER · LOW] security batch** — ISSUE-050 (reuse-detection writes no security-audit row), ISSUE-049 (refresh accepted on any subdomain), ISSUE-053 (reset ignores password history), ISSUE-006/054 (audit rows omit `ip_address`/`user_agent`, actor `unknown`).

---

## PHASE 2 — Correctness & tenant-settings  (systemic · a few decisions)
1. **[NEW · MED · systemic] Tenant timezone support** — attendance day-boundary, late/early detection, and regularization all assume **UTC** (`AttendanceService.cs:449/391`, `RegularizationDtos.cs`). Wrong for every non-UTC tenant. Overlaps the LEDGER item **ISSUE-065** (late-detection tz) — do them together. **File the broader gap as ISSUE.**
2. **[NEW · MED] Hardcoded tenant-settings → real config** — leave lookback/cancellation windows (`LeaveRequestService.cs:32/39`), fiscal/leave-year boundary (`ProcessLeaveYearEndJob.cs:50`, `GetCarryForwardPreviewQueryHandler.cs:22`). Needs a tenant-settings surface (mirror the `AttendanceSettings`/`Tenant`-column pattern used for BUG-038/ISSUE-160). **File as ISSUE.** *(Decision: which settings are tenant-configurable vs global.)*
3. **[NEW · MED] "Department Leave Calendar Coverage" report is a stub** — `LeaveReportService.cs:562-569` returns empty; US-LV-012 FR-1 silently yields nothing. **File as ISSUE.**
4. **[NEW · LOW/MED] Leave approval ignores payroll lock** — `LeaveRequestService.cs:918/1014` always "not locked" (attendance side has a real lock). Inconsistency. **File as ISSUE.**
5. **[LEDGER · MED · decision] ISSUE-243** (vacancy `is_deleted` anomaly) — likely test residue, LOW-confidence root cause. **Repro first**, then fix or downgrade to WONTFIX.

---

## PHASE 3 — Feature builds  (each = a proper story via `/implement-story`; needs AC)
1. **[LEDGER] ISSUE-021 — SalaryGrade entity** (Payroll), then job-title `gradeId` validation becomes trivial. Reconcile the tests that assert an arbitrary gradeId succeeds.
2. **[LEDGER] BUG-056 — goal-set finalize/submit endpoint** enforcing `sum == 100%` at finalize (the running ≤100% cap already exists).
3. **[NEW · MED] Subscription/Plan entity + tier-limit enforcement** — custom-field cap (`CustomFieldService.cs:27/745`) and `Tenant.Max*` limits + careers/SSO opt-ins (`Tenant.cs:121-185`) are hardcoded defaults; no plan is enforced (billing risk). **File + build.** *(Distinct from ISSUE-021.)*
4. **[NEW · MED] US-ADM-007 workflow-engine RUNTIME** — the design-time evaluator exists but nothing routes live requests through it; `WorkflowInstanceId` is always null; multi-level leave routing deferred (`LeaveRequestService.cs:372/1041/1077`). Config is authorable but inert. **File the runtime gap + build.**
5. **[LEDGER] ISSUE-140 — conversion side-effects** (auto-create user account / welcome email / onboarding trigger on applicant→employee).
6. **[LEDGER] ISSUE-123 — offer-approval gate** (`Recruitment.ApproveOffer` + BR-5/FR-10).
7. **[LEDGER] ISSUE-177 — year-end tax statement** (`PayrollReportService.cs:842`).
8. **[LEDGER] US-NTF Notifications module (ISSUE-188)** — the single biggest block: ~30 `LogOnly*` seams across every module only Serilog. Deliver real SignalR + Hangfire email dispatch (one-class-swap by design, but nothing is delivered today).

---

## PHASE 4 — Infra / performance  (needs infra provisioning)
1. **Redis wiring** (currently all direct-DB with `TODO(redis-*)`): permission cache (`InMemoryPermissionCache`), leave dashboard/balance → resolves **ISSUE-039** N+1, holiday cache, **ENH-017** statutory-rule cache (30-min TTL), and the Phase-1 session-revocation denylist.
2. **[LEDGER] US-PLT-002 — Postgres RLS Phase-4** + audit-log immutability (`Persistence/Rls/README.md`, `AuditLogService.cs:22`, `ImpersonationService.cs:21`).

---

## PHASE 5 — Test-coverage hardening
1. **[NEW] Testcontainers coverage for the 4 `IsRelational()`-gated transactional paths** — `TenantTransactionBehavior.cs:57`, `AuthService.cs:2056`, `TenantDataDeletionService.cs:61`, `ApplicantConversionService.cs:161`. This is the **BUG-068 InMemory-masks-Postgres class**: correctness of these units is currently not exercised by the InMemory suite. Add real-Postgres integration tests.

---

## PHASE 6 — LOW cosmetic sweep  (optional polish · batch by module)
~70 `ISSUE-LOW` (many already fixed-not-closed). Themes: HTTP status-code correctness (404-vs-400, 200-vs-201), whitespace-trim on names, WCAG AA contrast + favicon 404, audit-metadata completeness, error-message wording, verbatim-store hardening, FE "coming soon" placeholders (`my-team` approve-leave, tenant-suspended export), PDF-report charts (`HrReportRenderer`), stale job-title `employeeCount` placeholder, directory-export Hangfire offload for >10k rows. Low value individually — sweep only if a polish pass is wanted.

---

## Recommended order & sizing
| Phase | Theme | Rough size | Blocks on |
|---|---|---|---|
| 0 | Bookkeeping + file NEW items | ½ day | nothing — **start here** |
| 1 | Security/privacy | 2–3 days | ClamAV deploy (ops) |
| 2 | Correctness/tenant-settings | 3–4 days | 2 product calls |
| 3 | Feature builds | multi-week | AC per story |
| 4 | Infra/perf | 1–2 weeks | Redis + RLS provisioning |
| 5 | Test hardening | 2–3 days | Docker |
| 6 | LOW sweep | 2–3 days | optional |

**NEW items to file in Phase 0:** plaintext-MFA-secret (BUG/HIGH), EXIF-not-stripped (ISSUE/MED), access-token-revocation-noop (ISSUE/MED), UTC-only-timezone (ISSUE/MED), hardcoded-tenant-settings (ISSUE/MED), dept-leave-coverage-report-empty (ISSUE/MED), leave-approval-no-payroll-lock (ISSUE/LOW-MED), plan-tier-limits-unenforced (ISSUE/MED), workflow-runtime-absent (ISSUE/MED), per-email-rate-limit (ISSUE/LOW), IsRelational-test-coverage (ISSUE/test), + the ClamAV-deploy OPS note.

---

# PART II — Completeness-sweep findings (2026-07-06)

> A six-lens read-only sweep (orphaned-code · US-acceptance-criteria↔code traceability across all 12 modules ·
> FE↔BE contract-drift · blocked/untested census · spec-NFR checklist) beyond the marker scan above. **Headline:**
> the app's data-layer spines are genuinely built and well-wired (integration-enforcer: 434/435 handlers dispatched,
> all 102 tenant query filters present, every component routed) — but a large share of `[x]`-**done** stories carry
> **unbuilt acceptance criteria**, almost all in *outward delivery* and *cross-module seams* that were stubbed before
> the dependency existed and never rewired. This is a **status-integrity** problem, not scattered bugs. Gaps below are
> grouped by cross-cutting theme; each theme is a candidate epic. **Story/STATUS corrections → `@business-analyst`.**

### Theme A — Tenant isolation & RLS  🔴 highest risk
- **RLS absent** — `Rls:Enabled=false`, 0 `CREATE POLICY` in any migration; the spec's "critical second isolation layer" doesn't exist. Isolation rests on the single EF `HasQueryFilter` layer → any `IgnoreQueryFilters()`/raw-SQL/untenanted-job path has no safety net. **[NEW-spec — this is the one that stands]**
- ~~Cross-tenant WRITE leaks outside `/tenant/*`~~ **→ STALE (verify-first 2026-07-06, PR #184).** `TenantAccessGuardMiddleware` (#119) is **global**, not path-scoped; live probes on payroll/onboarding/recruitment reads+writes all `403 cross_tenant_denied`. BUG-087/089/090/091/092 + ISSUE-181/187 closed.
- **`EmployeeFieldAuditLog`** — tenant table with **no** query filter + write-only (no read endpoint): latent BUG-003 trap + orphaned US-CHR-002 field-audit feature. **[NEW — still open]** (Note: harmless today precisely because the app-guard above is global and there's no read path; add a `HasQueryFilter` before any read endpoint is added.)
- ~~BUG-040 password-reset takeover~~ **→ STALE.** Already RESOLVED (#118); reset is single-use + constant-time token-validated; live-confirmed no takeover.

### Theme B — Outbound delivery: Notifications / Email / SignalR  ← the single dominant gap
Every email + in-app/SignalR path is a `LogOnly*` stub (ISSUE-188/221/228). Nothing is ever delivered. Contradicts an AC in **~25 `[x]`-done stories**: password-reset/lockout/break-glass (Auth); doc-expiry/probation/import/manager-reassignment (Core-HR); leave queue/approval; **payslip email PAY-011** (entire story purpose); payroll+performance notifications (PAY-003/008, PRF-001/002/003/005/008/009); all recruitment emails (confirmation/interview/offer-with-PDF/scorecard/magic-link/stage — REC-002/004/005/006/007/008); attendance alerts/escalations/scheduled-reports (ATT-004/008/010); impersonation; export-ready. **→ build the US-NTF delivery layer (SMTP sender + SignalR hub) + rewire the ~30 seams.**

### Theme C — Approval-workflow RUNTIME engine
Design-time (`WorkflowEvaluator`, versioned definitions, editor UI) is fully built; **runtime is inert** — `WorkflowInstanceId` always null, `inFlightCount` hardcoded 0, no instance table. Blocks multi-level leave approval (**LV-005 AC-4**), attendance regularization multi-level (**ATT-004 AC-4**), offer approval (**REC-007 FR-10**), SLA-escalation (ADM-007), delegation (ADM-007). **[NEW runtime gap]**

### Theme D — Security hardening (defense-in-depth, mostly aspirational vs spec)
Plaintext MFA secret · JWT single static key (no rotation/overlap, AUTH-002 AC-7) · password-history configured-but-unenforced (AUTH-004) · login/MFA-challenge not rate-limited (AUTH-001/005) · **subdomain cache not invalidated on tenant status change** (AUTH-007 FR-9 → suspended tenant resolves Active for TTL) · **download URLs not actually signed** (`LocalFileStorage` returns plain paths — data-export + resume + attachments) · EXIF not stripped · audit DB-immutability code-only · no encryption-at-rest · no HSTS/HTTPS-redirect · no Key Vault.

### Theme E — Cross-module integration seams stubbed
REC-010 convert: **no user-account creation** (AC-3), **no salary persistence** (AC-2), no "Converted" badge (AC-4/ISSUE-232) · attendance auto-LOP behind `NoOpAttendanceProvider` (LV-011 AC-2 inert) · payroll-lock checks hardcoded `false` (LV-005 BR-4 / LV-010 AC-4) · recommendation downstream provisioning log-only · **Calibration DEAD-END TRAP** — enabling the toggle permanently blocks recommendation generation (`calibration_incomplete`, nothing can mark it complete → US-PRF-010 lockout).

### Theme F — PDF export seams (QuestPDF is in the repo, just unused)
PRF-005 (360 report), PRF-006 (review), PRF-007 (dashboard), PRF-008 (PIP), PRF-010 (recommendation), REC-009 (dashboard — with a **dead FE button** that always toasts "Export failed"), PAY-009 year-end tax (ISSUE-177). CSV/XLSX exist; the AC-named PDF does not.

### Theme G — FE↔BE contract drift
**3 NEW shape bugs:** payroll adjustments list renders empty (`{items,totalCount}` as `{data}`), custom-fields list broken (array-as-object), custom-fields `id` read as `customFieldId` → `.../undefined` requests. Plus **Performance module route mismatch → CONFIRMED LIVE (verify-first 2026-07-06) = BUG-243 (HIGH), PR #184**: ~9 of 10 Angular Performance services call path segments the backend doesn't expose → 404 end-to-end (5 services 100% dead, 5 partial). Backend routes are correct/tested; **fix the FE to match** (~10 services, FE-only). **This is the top open *functional* defect** — a whole priced module is unusable via its UI while reading "done."

### Theme H — Plan / tenant governance not enforced
Module-gating deferred (ADM-009 #17: a disabled module's API isn't 403'd, no FE route guard) · usage limits storage/API/email config-only (ADM-009 #16; storage quota BUG-114) · plan-gated enterprise-only settings absent (ADM-006 #17).

### Theme I — Observability / monitoring pipeline absent
No OTel/tracing/metrics store; monitoring error-rate/latency/SLA hardcoded null; per-tenant usage counters absent; no `/health/live|ready`; perf SLOs uninstrumented (ties to ISSUE-203 login p95 3.86s).

### Theme J — Tenant-settings / localization / timezone
Hardcoded leave lookback/cancellation windows + fiscal/leave-year boundary · **UTC-only attendance** (day-boundary/late/early wrong for non-UTC tenants) · localization saved-but-not-applied (no `LOCALE_ID`/`registerLocaleData`) · i18n ships only `en.json`.

### Theme K — Discrete unbuilt features (each a story)
Part-time **FTE proration** (LV-002 BR-2, no `Employee.Fte`) · **accrual-frequency scheduling** (LV-002 FR-5, always upfront) · **custom-field columns in bulk import** (CHR-010/012 FR-11) · **dept leave-coverage report** (LV-012) · **scorecard versioning** (REC-006) · interview-guide attachment (REC-005 FR-8) · **SalaryGrade entity** (ISSUE-021) · **goal-set finalize** (BUG-056) · **calibration workspace** (PRF-010 dep) · **SSO config + enforcement** (AUTH-012/016 — correctly `[ ]`, net-new).

### Theme L — Orphaned / dead code (integration-enforcer)
`IPipCheckpointScheduler` dead seam (no impl/registration, false "wired" comment — likely covered by `PipReminderJob`) · `GeneratePortalLinkCommand` orphaned handler · 2 Hangfire jobs (`DocumentExpiryNotificationJob`, `Feedback360ReminderJob`) scheduled but not `AddScoped` (works via fallback) · `EmployeeFieldAuditLog` (→ Theme A).

### Theme M — Test-coverage blind spots
**Training & Benefits — ZERO coverage** (no stories, no test-cases, never executed) · Platform Admin Console UI never UI-tested (admin-subdomain 404) · the 4 `IsRelational()`-gated transactional paths not Postgres-tested (BUG-068 class) · k6/perf harness out-of-tree.

## How Part II maps onto the phases
- **Verify-first pass — ✅ DONE (2026-07-06, PR #184):** of the 3 ambiguous items, **2 were ghosts** (cross-tenant-outside-`/tenant/*` and BUG-040 both STALE/already-fixed) and **1 was real** (Performance routes = BUG-243 HIGH, now the top functional item). Net: don't spend on Theme-A cross-tenant or BUG-040; **do** prioritize the BUG-243 FE route fix.
- **Phase 1 (security):** Themes A (RLS + EmployeeFieldAuditLog filter), D.
- **Phase 2 (correctness):** Themes E, J, and the Theme-K quick ones.
- **Phase 3 (features):** Themes B (Notifications — biggest), C (workflow runtime), F (PDF), H, and the Theme-K stories.
- **Phase 4 (infra):** Theme I (observability) + RLS enablement.
- **Phase 5 (tests):** Theme M.
- **Phase 6:** Theme L dead-code cleanup + LOW tail.

## Business-analyst actions (US / STATUS changes)
1. **Correct `STATUS.md`** — the ~25 `[x]` stories with unbuilt ACs should not read fully done; mark the specific ACs deferred.
2. **Create net-new stories/epics** for capabilities that have no story: US-NTF delivery layer, workflow-runtime engine, calibration workspace, plan/module-gating enforcement, observability/OTel, RLS enablement, encryption-at-rest — and a **Training & Benefits** story set (currently absent entirely).
3. **Annotate** each affected story's ledger/AC with the sweep finding so future agents don't re-mark it done.
