# HRM — Gap Register (implemented vs documented)

> ## ⚠ RE-VERIFICATION 2026-08-12 — this register is a 2026-08-08 snapshot; 24 PRs have merged since
>
> **The contradiction count in this document does not survive measurement.** It headlines **36**; a grep of the
> pass files returns **51**; classifying those 51 gives **23 summary lines** (not rows), **22 doc-vs-code
> contradictions**, and **6 rows where the ledger claims an open bug that is actually fixed** — so **28
> actionable**.
>
> **Verified STALE (the code has since changed; treat these rows as closed):**
> - **AUTH-013 AC-1, AUTH-013 AC-2, US-AUTH-012, US-AUTH-014** — all four rested on `EntraSsoService` reading
>   `TenantAllowList` from appsettings. `SsoIsolationGuard.Evaluate(settings, tid, email, emailVerified)` has
>   been wired at `EntraSsoService.cs:396` since **#483**, taking per-tenant settings **and** the verified-email
>   flag — precisely what those ACs demanded.
> - **ONB-002 AC-1** — the FE now calls `applicable-templates` (`onboarding-checklist.service.ts:54`), fixed in
>   **#491**. Its own docstring still documented the dead `/applicable` route until 2026-08-12; corrected.
>
> **Verified STILL LIVE:**
> - **ATT-011 AC-5** (GAP-022) — `PayrollOvertimeCalculator` contains no `.Fte` reference and
>   `PayrollRunProcessor.cs:518` still calls `ComputeOvertime` with **4 args**. The dead-wiring claim holds.
> - **US-PRF-011** (GAP-021) — neither `CyclePhase.CompletedOn` nor a `Performance.Calibrate` permission exists.
>
> **Still to re-verify: ~11 doc-vs-code rows.** All cite code that still exists, so each needs reading against
> current `src/` — none can be dismissed on a moved-file basis.
>
> **Also still live, and the target of step 3 of the conflict-resolution programme:** the
> `CONTRACT (assumed — reconcile with backend)` comment this register called out as never reconciled is still
> present at `onboarding-template.service.ts:22`. Migrating the FE onto the generated contract types removes the
> ability to write that comment at all.
>
> **New guard:** `LedgerTraceabilityTests` now fails CI when the ledgers drift — see the COMPLETION-PLAN entry
> for 2026-08-12.


> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains` · **Mode:** REPORT-ONLY
> **Method:** every requirement traced to `src/` with `file:line` evidence. A ledger line was never
> allowed to promote a verdict. **AC-level for Must Have, story-level for Should/Could.**
> **Evidence bar:** code exists **+** wired/reachable **+** a bound test exists. Failing any leg → `PARTIAL`.
> **17 passes · ~674 verdicts · every headline claim independently re-verified by the orchestrator.**

---

## 1. Headline — claimed vs verified

`docs/BA/STATUS.md` marks **124 of 125 stories done (99%)**.

| Scope | Rows | Implemented | Partial | Missing | Contradicted* |
|---|---:|---:|---:|---:|---:|
| **13 BA modules** | 492 | **297 (60%)** | 156 (32%) | 2 | 36 |
| Pass B — doc→story coverage | 62 | 45 covered | — | **15 uncovered** | — |
| Pass C — NFR §6 | 44 | 11 | 22 | 5 | 1 |
| Pass D — reverse (code→doc) | 19 | 5 documented | — | — | 2 |
| Pass E — architecture §8/§9/§10 | 57 | 30 | 20 | 3 | 2 |
| **Total** | **674** | — | — | — | — |

\* `CONTRADICTED` rows overlap `PARTIAL`/`MISSING`; they are not additive.

**The one-line answer: a product the ledger calls 99% done is ~60% done at acceptance-criterion level — and almost none of the shortfall is unbuilt backend.**

### By MoSCoW

| Tier | Stories | Verified health | Character of the gap |
|---|---:|---|---|
| **Must Have** | 82 (448 ACs) | **~62% clean** | Overwhelmingly **frontend contract + wiring**. Two genuine MISSING ACs in 448. |
| **Should Have** | 41 | **~45% clean** | Same pattern, plus more documented deferrals |
| **Could Have** | 2 | 1 partial, 1 n/a | Immaterial |
| **Undocumented (§6 NFR)** | — | **11 of 44 clean** | **The weakest tier by far — and no ledger tracks any of it** |

**The MoSCoW tiers are not where the risk concentrates.** The Must-Have backend is strong. The risk concentrates in **two axes the tiers don't capture**: the FE↔BE contract, and the non-functional requirements no story owns.

---

## 2. The four systemic findings

Everything in §3 is an instance of one of these. **Fixing the four structurally is worth more than the ~40 individual items.**

### S-1 · Two hand-written descriptions of one contract, with nothing checking they agree
**Hit 9 of 13 modules.** The Angular layer is coded against a response shape the API cannot emit, and the Karma specs mock the invented shape, so they stay green over a dead feature.

The team **knew**: `onboarding-checklist.service.ts:33` and `onboarding-template.service.ts:22` carry the literal comment `CONTRACT (assumed — reconcile with backend)`. It was never reconciled. Elsewhere it was fixed *pointwise* (`document.service.ts:47-49`, `custom-field.service.ts:56`) and never generalized.

**Counter-examples prove it is solvable:** notifications, training-benefits, reports and US-PRF-004 are all clean — and **US-PRF-004 is clean precisely because it got an explicit reconciliation pass.**

> **Structural fix: generate TypeScript models from the OpenAPI schema.** Kills the entire class.

### S-2 · Hand-maintained parallel lists drift; guarded ones don't
The **RLS** layer has a reflection-driven coverage guard asserting exact set equality → **zero holes**. The **EF query-filter** layer has no guard → **six holes**.

> **Structural fix: one `[Fact]` over `db.Model.GetEntityTypes()` asserting every `TenantId`-bearing type has a filter.** Mirror the RLS guard. Closes the class permanently.

### S-3 · Build the mechanism, ship it switched off
Seven instances in the NFR pass alone: OTel wired-but-dormant · `@axe-core/playwright` installed with zero usages · coverlet never invoked · semgrep non-blocking · Playwright not in CI · `AnonymizeUserAsync` registered with zero callers · `TenantStatus.PastDue` defined but never enforced. Plus `FteScaledOvertimeBase` (flag never threaded) and `includeCharts` (accepted, never read).

> **Not a capability gap — a last-mile activation gap. Most items are S-sized.**

### S-4 · The ledgers are wrong in both directions, and the pessimistic direction costs real time
**36 CONTRADICTED verdicts.** Recruitment alone narrates **twelve closed defects as live**, including BUG-068 (*"100% BROKEN, HTTP 500 on EVERY conversion"*, 9 TCs blocked behind it) and BUG-003 (CRIT). `TEST-STATUS.md` calls training-benefits *"zero test coverage"* when 6 Testcontainers-Postgres suites exist, and calls US-NTF-006 *"not-yet-built"* when ~1,000 lines and 34 test files ship.

**Two test cases are marked passing against code paths that do not exist** — `TC-ADM-010-13` (a `system_audit_log` table the codebase says it deliberately doesn't have) and `TC-ATT-152` (`automated`, asserting *stored* earnings its pure-function test cannot produce; **its own header concedes it "proves the MATH, not the plumbing"**).

> **Anyone triaging from these ledgers re-opens fixed tickets and skips real ones.**

---

## 3. The ranked register

Ranked by **severity × blast radius**, tenant isolation first per Critical Rule #1.
IDs are stable — never renumber; append only.

### P0 — act before the next production deploy

> **Filed to [`docs/QA/TEST-FINDINGS.md`](../../QA/TEST-FINDINGS.md) 2026-08-08 so `/fix-finding` can act on them:**
> `GAP-001 → BUG-297` · `GAP-002 (+GAP-017) → BUG-298` · `GAP-003 → BUG-299` · `GAP-004 → BUG-300` · `GAP-005 → BUG-301`.
> The remaining 35 gaps are **not** filed — they live here and in the COMPLETION-PLAN's §GAP-PLAN.

| ID | Gap | Evidence | Size |
|---|---|---|---:|
| **GAP-001** | **An unresolved tenant context disengages ALL FOUR isolation layers.** Resolution passes the request through (`TenantResolutionMiddleware.cs:90-93,105-110`); the EF filter becomes a tautology (`AppDbContext.cs:269-270`); the connection routes to the **BYPASSRLS** `hrm_owner` role (`ConnectionRoutingInterceptor.cs:92-93`); the cross-tenant guard skips (`TenantAccessGuardMiddleware.cs:38-42`). **`api` and `app` are both in the shipped reserved-subdomain list.** Latent today; live the moment the API is served at `api.<domain>`. **No US-PLT AC covers this case** — the nearest (US-PLT-002 AC-4) is unmet and never negatively tested. *Mitigation found: `SystemEndpointHostGuardMiddleware` protects `/api/v1/system/*`; `/api/v1/tenant/*` is unprotected.* | 6 links, all verified | **M** |
| **GAP-002** | **SSO tenant isolation is appsettings-backed, not DB-backed.** `EntraSsoService.CheckIsolation` reads `_options.TenantAllowList`; the five DB fields have **zero read sites on any login path**. Editing the allow-list in the UI changes nothing; **admin-consent onboarding cannot enable anyone**; `SsoEnabled = false` does **not** block SSO. `STATUS.md:40` declares the BR-5 production gate satisfied. **It is not.** | `EntraSsoOptions.cs:9-13` admits it is "the dev-POC home" | **M** |
| **GAP-003** | **LOP is under-deducted for every mid-month joiner and leaver.** `PayrollSlipCalculator.cs:142` divides the **already pro-rated** basic by full working days — the pro-ration lands twice, and **the code contradicts its own comment at `:138`**. 22 days, BASIC 22,000, joins mid-month, 2 LOP days → deducts 1,000; correct is 2,000. **No test combines proration with LOP.** | Money path | **S** |
| **GAP-004** | **A daily job deletes audit logs at 90 days** against a documented **7-year** retention (tech doc `:369`). `AuditLogPurgeService.cs:41` hard-deletes; the "Enterprise = 2555" promise exists only as a comment. No archival path. | Compliance | **S** |
| **GAP-005** | **Audit log is append-only by convention only.** `roles.sql:43,47` grants `UPDATE, DELETE` on all tables to the runtime role. **The app's own credentials can rewrite audit history.** | `AuditLogController.cs:19-21` concedes it | **S** |

### P1 — high, close before the next release

| ID | Gap | Size |
|---|---|---:|
| **GAP-006** | **CORRECTED 2026-09-02 — THREE, not six, were real holes** — `AuditLog`, `PayrollReportExport`, `PlanLimitOverride`, `TenantLatencyBucket`, `TenantLifecycleEvent`, `TenantScheduledJob`. RLS covers them when enabled; **`Rls:Enabled=false` in Development, so dev and CI run on one layer.** Fix + **add the coverage guard (S-2)**. | S+M |  ⚠ **Do NOT "add the 6 HasQueryFilter lines" — that prescription would have caused a regression.** Only `AuditLog`, `PayrollReportExport` and `TenantLatencyBucket` were genuine holes (each carried a doc comment claiming a filter it did not have) and all three are now filtered. `PlanLimitOverride`, `TenantLifecycleEvent` and `TenantScheduledJob` are **deliberately unfiltered** system/cross-tenant tables, each carrying a `tenant_isolation` RLS policy verified against a live database. `TenantQueryFilterCoverageTests.cs:60-79` records this and allow-lists them. Status: **CLOSED** (the coverage guard the register asked for also shipped).
| **GAP-007** | **270 `IgnoreQueryFilters()` calls, none carrying the project's own `// nosemgrep:` justification marker**, with the detecting rule running `continue-on-error`. Many are legitimate — **nobody can tell which.** | M |
| **GAP-008** | **`PayrollReportExport.GenerateAsync:194` reads by id with neither tenant nor owner check**, and two code comments assert a filter that does not exist. | S |
| **GAP-009** | **Admin user-management screen is 0-for-6.** Seven of twelve service calls target routes that do not exist (`assignable-roles`, `invite/csv` — **zero backend hits each**). **The backend is correct and well tested; the fix is eight URL strings.** | S |
| **GAP-010** | **Four payroll write paths dead at the contract** — bulk assign, tax slabs, adjustments, payslip distribution. All marked `[x]` with PR numbers. **Income-tax slabs cannot be saved from the UI.** | S each |
| **GAP-011** | **Public careers page unreachable from its own UI** — the public DTOs expose `Slug`, no `Id`; all three FE surfaces route on `v.id`. Compounded by `PublicCareersEnabled` having **no writer outside test fixtures**. | S |
| **GAP-012** | **Performance module: response-shape drift across 8 of 11 stories.** No adapter layer. **US-PRF-002/009/010 hard-crash rather than degrade.** `'PromotionConsideration'` 400s every promotion-flagged submit. | L |
| **GAP-013** | **Onboarding: assignment and clearance unusable** — 2 dead routes, 1 wrong verb, 5 field mismatches. **`canComplete()` is permanently false, so "Complete Offboarding" can never be enabled.** | M |
| **GAP-014** | **`{entity}Id` vs `id` kills deactivate on departments, job titles and locations** — plus `PATCH` against `[HttpPost]` on two of them. **Corroborated independently by core-hr and attendance.** | S |
| **GAP-015** | **Default deployment sends no email at all.** `Smtp:Host = ""` wires `LogOnlyEmailSender` — **including password reset and account lockout, which BR-1 designates non-suppressible.** | S (ops) |
| **GAP-016** | **RBAC roles UI dead at the wire** (`RoleDto.Id` vs FE `roleId`), compounded by a permission string absent from the catalog. **Specs green because they mock both defects.** | S |
| **GAP-017** | **Unverified email accepted for SSO domain allow-listing** — no `xms_edov`/`email_verified` check anywhere. | S |
| **GAP-018** | **Rate limiting has no tenant or user dimension** — IP only, anonymous endpoints only. **Every authenticated tenant endpoint is unthrottled**, and the limiter is in-process so limits multiply by instance count. | M |

### P2 — medium

| ID | Gap | Size |
|---|---|---:|
| **GAP-019** | 15 documented capabilities have **no story and no code** — **8 in the §11.1 operator surface** (revenue, billing ops, staff management, **JWT signing-key rotation**, maintenance mode, broadcasts, GDPR intake), 4 in §33.3 platform reporting, **2 in auth breadth (Google + Apple sign-in, both In Scope at §3.1 with named sample stories)** | varies |
| **GAP-020** | **No way to rotate a compromised JWT signing key.** `RevokeAllSessions` is per-user. §3.4 declares cross-tenant leakage zero-tolerance; the containment lever doesn't exist | M |
| **GAP-021** | **US-PRF-011 marked DONE**: data model shipped, **workspace and phase-completion state machine did not**, and `Performance.Calibrate` was never created — **the exact trap the story warned against** | M |
| **GAP-022** | **`FteScaledOvertimeBase` is dead wiring** — `Compute` called with 4 of 7 args, `.Fte` appears 0 times in `PayrollRunProcessor` | S |
| **GAP-023** | **US-CHR-013 claims a shipped "FE employee-form"** for FTE and work arrangement. **Zero frontend files reference either.** HR cannot set them through the product | M |
| **GAP-024** | **Three Hangfire jobs run with no tenant context**, and **no background-job log line carries `tenant_id`** — exactly where you'd need it to diagnose an isolation incident | M |
| **GAP-025** | **Employee changes produce a degraded audit row invisible to the audit viewer** (`Employee` is `IAuditExempt`) | M |
| **GAP-026** | **Terminated employees are enrollable into any rules-free benefit plan** — `EmployeeStatus` never checked on the enrollment path | S |
| **GAP-027** | **Document download is dead twice over** — `GetSignedUrl` returns a path no route serves, and the FE reads `downloadUrl` while the BE emits `signedUrl` | M |
| **GAP-028** | **Export bundle missing 2 of 5 artifacts and the emailed link 404s.** GDPR Art. 20 portability is the story's stated purpose | M–L |
| **GAP-029** | **New tenants get no default workflow**, so the entire US-ADM-011 runtime engine lies dormant until an admin hand-authors one. **Highest-leverage small fix in admin-console** | S |
| **GAP-030** | **CORRECTED 2026-09-02 — the "zero test cases" half is FALSE.** US-ADM-012 and US-PLT-004 are `[EPIC STUB]`s with ~50 production files and 2 migrations built against them, **but both are well test-covered**: US-ADM-012 by `ModuleEntitlementApiTests.cs` (`[Trait("TC","TC-ADM-012")]`), `ModuleEntitlementMiddlewareTests.cs`, `ModuleVocabularyContractTests.cs`, `SubscriptionPlanModuleSweepPostgresTests.cs` and `module.guard.spec.ts`; US-PLT-004 by `PlatformMonitoringIntegrationTests.cs`, `TenantApiCallUsagePostgresTests.cs`, `PlatformMonitoringUsageGaugesPostgresTests.cs`, `PlatformMonitoringRedisHealthTests.cs` and `SlaUptimeTests.cs`. **The real gap is documentation-only: the BA stories are stubs.** Acting on the original wording would have meant writing tests that already exist. | M |
| **GAP-031** | Coverage requirement (≥70%/≥85%) **has no measurement path** — coverlet never invoked, Karma coverage off. **Can be neither met nor missed** | S |
| **GAP-032** | `terminated` tenants are **not blocked at the API layer**; `past_due` is **defined, unreachable and unenforced** | S |

### P3 — low / decisions, not defects

`GAP-033` no security headers or in-repo production TLS artefact · `GAP-034` **CORRECTED 2026-09-02 — it understated the a11y half.** The responsive half is CLOSED (`e2e/cross-browser.spec.ts:20-21,65-76` runs in CI at `ci-gate.yml:303-438`). The a11y half is worse than "never executes": **there are ZERO axe assertions in the repo** — `@axe-core/playwright` is a devDependency but a repo-wide grep for `AxeBuilder|axe-core|checkA11y|toHaveNoViolations` returns nothing. There is no assertion to execute (highest coverage-gained-per-effort in the list) · `GAP-035` per-tenant sender identity absent — all mail from one global From · `GAP-036` no per-user GDPR access/erasure (tenant scope is well built) · `GAP-037` outbound webhooks documented but unbuilt · `GAP-038` `admin.yourhrm.com` documented as the system-admin entry point **is one nobody can authenticate on** · `GAP-039` Redis balance-cache invalidation absent module-wide (**a deliberate vault decision — no correctness bug today**) · `GAP-040` documented folder layouts stale in both projects

---

## 4. What is genuinely strong

Stated plainly, because a register that only lists faults misrepresents the codebase.

- **Zero orphaned code.** 0 of 359 MediatR handlers undispatched, 0 of 62 Hangfire jobs unreferenced, 0 unrouted components. **There is no dead weight to delete.**
- **Only 2 MISSING acceptance criteria in 448 Must-Have ACs.** Almost nothing is unbuilt.
- **The isolation architecture is better than average** — three independent layers, a fail-closed RLS startup guard, a spoof-proof cross-tenant JWT guard, a `tenantId`-mandatory storage interface that cannot be forgotten at a call site, and a cache-key provider that derives the tenant from `ITenantContext` rather than SQL text **so it stays correct after the RLS flip**.
- **Documented pipeline order and dependency direction hold exactly**, with a genuinely framework-free Domain.
- **Real Postgres where it matters** — money paths, isolation, concurrency all use Testcontainers, not InMemory.
- **The BA corpus is unusually rigorous** — acceptance criteria name controller classes and route templates.
- **Four modules have clean FE/BE contracts.** It is solvable, and it has been solved here.

---

## 5. Hand-off

**This register closes nothing.** Per the skill's boundary, no `src/` file and no ledger line was edited.

1. **File `GAP-001`–`GAP-005` to `docs/QA/TEST-FINDINGS.md`** with the standard schema. GAP-001 belongs under Critical Rule #1.
2. **Settle `GAP-001`'s reachability** with one probe: authenticate as tenant A, `GET /api/v1/tenant/employees` with `Host: api.<basedomain>`, count rows. *That single test converts a 60%-confidence risk into a yes or no.*
3. **Do the two structural fixes (S-1, S-2) before the ~40 individual items** — generated TS models, and the EF-filter coverage guard.
4. **Run `/verify-fix` over the stale ledger lines** — recruitment (12), performance (3), attendance (8), reports (5), notifications (1). **The regression tests already exist, so the re-runs are cheap.**
5. **Route the FE spec suites to `@test-authenticator`** — mocking-the-invented-shape is the mechanism behind most of §3.
6. **Take three items to the decision gate, not the backlog:** ~~goal cascading org/department tier (build vs amend §11.9)~~ **✅ DECIDED 2026-08-11 — AMEND, do not build** ([[ADR-2026-08-11-goal-ownership-stays-individual]]; propagated through the doc tree 2026-08-17), ~~billing-ops epic (does Phase 1 stay in a spreadsheet?)~~ **✅ DECIDED 2026-08-11 — PARKED** until automated billing goes live, and whether the D2-b attendance-provider deferral should be re-opened now that attendance has shipped — **still open, the only one of the three left at the gate.**

---

## 6. Confidence and limits

**Every headline claim was re-verified by the orchestrator against the cited files before it landed here.** Twelve auditors ran; **eight corrected my briefs** — on story prefixes, ISSUE attribution, Asset Management coverage, the `IgnoreQueryFilters` unit of work, stub-swap suspicions, the NoOp attendance provider's blast radius, test coverage, and the EF-hole count (7 → 6). **I got one wrong and committed it** before catching it: `TC-LV-031` does say what the pilot reported, and I accepted an auditor's denial without checking (retracted in `66c78be3`).

**Limits that apply to everything above:**
- **Static reading only.** Nothing was executed. Leg 3 records that a test *exists*, never that it *passes*.
- **Every "the UI is broken" verdict is a static contract inference** — route string vs `[Http*]` attribute, TS field vs DTO property. **Each is falsifiable in about a minute with the app running**, and they are consistent with the QA ledger's own record that only the API layer was ever tested.
- **All numeric NFRs are UNVERIFIABLE by this method** — p95 latencies, 10k concurrent users, payroll-5000, TTI, WCAG AA. What is reported is whether the *mechanism* and a *measurement path* exist.
- **Four passes were recovered after turn-budget exhaustion** (notifications, attendance, reports, performance, platform). Each states per-story coverage in its own `## CONFIDENCE` section — **read that before trusting any single row from them.**
- **Should/Could rows are story-level by design**, so an AC that is 80% implemented inside a passing story can be under-reported.
