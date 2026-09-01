# HRM — Completion Plan (living backlog)

> **Rebuilt 2026-09-01 from a full code-verified audit.** Every item below was checked against `src/`
> by a `@requirements-auditor` pass with `file:line` evidence. The ledgers were treated as **claims**,
> never evidence — which is why a third of them turned out to be wrong.
>
> **This file is the broad backlog. It is NOT the execution lane.**
> The loop executes [`GAP-CLOSURE-QUEUE.md`](GAP-CLOSURE-QUEUE.md), top-down, one item per iteration,
> and that is also where `/auto-heal` folds new findings. This file is refreshed at `/retro` and
> `/gap-analysis` cadence. Findings live in [`../TEST-FINDINGS.md`](../TEST-FINDINGS.md) (live) and
> [`../TEST-FINDINGS-RESOLVED.md`](../TEST-FINDINGS-RESOLVED.md) (terminal).
>
> **Superseded generations are in [`archive/`](archive/).** Before this rebuild this file carried
> **five** overlapping sections that each claimed to be "the queue" (2026-07-15 resume point, 2026-08-03
> live queue, 2026-08-04 waves, 2026-08-08 GAP-PLAN, P0–P7). A reader got five answers to "what is next".
> They are preserved verbatim as history and stamped *do not execute*. 256 KB moved; this file is what is true.

---

## 📐 What the audit measured, and how far to trust it

| Population | Method | Result |
|---|---|---|
| 38 findings the ledger called **live** | all code-verified | **11 already fixed (29%)** |
| 188 findings the ledger called **terminal** | random sample of 30 | **0 false-resolved** (26 confirmed, 1 untested-but-fixed, 3 need a live stack) |
| 40 GAP register rows | all code-verified | **17 CLOSED · 18 PARTIAL · 5 OPEN** |

**Stale-pessimistic 29%, stale-optimistic ≤10%** (0/29, rule-of-three 95% bound → at most ~19 of the 188).
The asymmetry is exactly what [`ledgers.md`](../../../.claude/rules/ledgers.md) predicts, and the
pessimistic direction is the expensive one: it makes shipped work look outstanding, and someone
eventually rebuilds it.

**Every PARTIAL gap failed leg 2 (wiring) or leg 3 (test-bound) — not one failed leg 1.** The backend
capability existed everywhere it was claimed to. This codebase's defect class is **things not being
connected**, not things not being built. Plan accordingly.

**Confidence caveat (stated because it changes how you use this):** every verdict is a *static* contract
inference. Nothing was run. The FE↔BE breaks below are falsifiable in about a minute against a running
stack, and three items are explicitly `CANNOT-VERIFY` because they need one.

---

## 🔴 P0 — live defects with money, compliance or access consequences

| # | Item | Evidence | Why it is P0 |
|---|---|---|---|
| 1 | **Part-time overtime is paid on a full-time base** | `PayrollRunProcessor.cs:977-978` calls `PayrollOvertimeCalculator.Compute` with **4 of 7** args, so `fte=1.0`/`fteScaledBase=false` always. `AttendanceSettings.FteScaledOvertimeBase` is persisted, exposed and settable — and inert. `OvertimeFteBaseTests.cs:10-13` concedes it "proves the MATH, not the plumbing". | Money, silently wrong, for every part-time employee of every tenant that enabled the flag. (GAP-022) |
| 2 | **No DSAR / right-to-erasure path exists** | `IAuditAnonymizationService` is DI-registered (`DependencyInjection.cs:781`) and called by **nothing** outside tests. `DataExportController` is tenant-wide bulk export, not subject access. Searched SubjectAccessRequest / RightToErasure / ExportMyData — empty. | A data-subject request can today only be served by manual DB intervention. (GAP-036) |
| 3 | **Public careers page: every vacancy click 404s** | `careers-page.component.ts:141` links `['/careers', v.id]` (a GUID); `CareersController.cs:53` matches `{slug}` on `x.Slug`. A GUID never equals a slug. The FE model even documents the requirement at `applicant.models.ts:81` — nothing was changed to use `v.slug`. | Visitors can list vacancies but cannot open one, so cannot reach the apply form. Register says GAP-011 is closed. |
| 4 | **Manager team-goals dashboard is non-functional** | `performance-goal.service.ts:53-56` expects `ITeamGoalStatus[] \| {data:[…]}`; BE returns `{cycleId, members}` (`GoalDtos.cs:62-66`). `.members` is never read → `toArray()` returns `[]` → "No team members yet" on every load. | US-PRF-001 AC-4 dead in production. Register says GAP-012's adapter work is the gap; the adapter shipped and this is a different, unlisted break. |
| 5 | **Onboarding: dead route + two 405 buttons** | `onboarding-checklist.service.ts:68` GETs a `/preview` route that does not exist on `OnboardingChecklistsController`; `onboarding-template.service.ts:94,103` use `PATCH` against `[HttpPost]` activate/deactivate (`OnboardingTemplatesController.cs:155,174`). All three reachable from routed components. | HR cannot preview a checklist before assigning, nor activate/deactivate a template. Register says GAP-013 is closed. |
| 6 | **Blank `Jwt:PrivateKey` in Production yields an ephemeral per-process key** | `JwtKeyRingOptions` has no `IValidateOptions`/`ValidateOnStart`; `JwtService.cs:41-47` falls back to a generated RSA key. `TokenValidationParameters` is snapshotted once at `Program.cs:162`. | Every restart invalidates all tokens; multi-instance deploys reject each other's. Belongs to no GAP row — found out-of-lane. |

> **Items 3, 4 and 5 share a root cause and a lesson: their test suites are green because the specs mock
> the broken shape.** `vacancy-detail.component.spec.ts:77` feeds `'vac-1'`; `onboarding-checklist.service.spec.ts:110`
> flushes `/preview`; `performance-goal.service.spec.ts:135` flushes `{data: …}` — a shape the endpoint never
> returns. Three live user-facing breaks behind a green suite. Fixing the code without fixing the specs
> leaves the detector broken.

---

## 🟠 P1 — HIGH: isolation invariants held by convention

| # | Item | Evidence | Note |
|---|---|---|---|
| 7 | **The tenant-isolation off-switch is fail-open by construction** | Layers 1/2/4 unchanged: `TenantResolutionMiddleware.cs:90-93` passes through, `AppDbContext.cs:272-289` filters read `!IsResolved \|\| …` (tautology), `TenantAccessGuardMiddleware.cs:40` skips. Layer 3's fix is inert in shipped config (`PrivilegedConnection` blank; `Rls:Enabled=false` in Dev). | Not exploitable today **only** because ~494 hand-written `!IsResolved` guards across 104 service files fail-close first. That is a convention holding Critical Rule #1. One omitted guard on a new read path = full cross-tenant leak. **The missing HTTP-level negative test is the cheapest real mitigation.** (GAP-001) |
| 8 | **Audit append-only is a runbook checkbox, not a control** | `roles.sql:70-71` has the REVOKE, but nothing runs `roles.sql` — not the app, not EF, not `ops/`, `scripts/` or CI; the file itself says so at `:3-4`. The test at `RlsIsolationPostgresTests.cs:431` **hand-mirrors** the revoke in its fixture, so it validates the intended privilege set, not that `roles.sql` produces it. | `AuditLogController.cs:21-23` claims append-only is "ENFORCED rather than merely conventional". That is false until RLS increment 3 ships and `roles.sql` is applied per environment. Correct the comment or apply the file. (GAP-005) |
| 9 | **354 `IgnoreQueryFilters()` sites, none machine-reviewable** | Measured 354 in non-test `src/backend` (register said 270) + 472 in tests. **Zero** carry `// nosemgrep`. `.semgrep/tenant-isolation.yml:55` is still `WARNING` and its CI step is `continue-on-error: true`. | The written rationale is "RLS backstops them" — but Development runs `Rls:Enabled=false`, so in dev/CI the backstop that argument rests on is switched off. (GAP-007) |
| 10 | **`ISSUE-379` — 9 dashboard/sign-off rows still unbacked** | Full list with `file:line` in the finding. | Re-audited untruncated; rows 9 and 11 had never been audited at all. |

---

## 🟡 P2 — MED: wiring, contracts and coverage

| # | Item | Evidence |
|---|---|---|
| 11 | **3 `ISSUE-379` rows have shifted BE→FE and the comments say otherwise** | `availableExportFormats`, `ratingScaleMax`/`finalScore`/`cycleName`, `scoreScaleMax`/`cycleLabel` all ship on the wire; `dashboard.models.ts:336,369,404,406` and `review-signoff.models.ts:376,387,389` hardcode `[]`/`0`/`''`/`null` **under comments asserting the wire lacks them**. Row 5 is entirely FE-side and the cheapest fix on the list. |
| 12 | `ISSUE-372` — `/salary-components/reorder` and `/validate-formula` are live 404s | Two UI controls (drag-reorder, Test-formula) call routes no controller serves. Specs mock both. |
| 13 | `ISSUE-374` — onboarding `/checklists/preview` 404 + modify shape mismatch silently drops edits | FE `{tasks}` vs BE `{AddTasks, TaskChanges}`. |
| 14 | `ISSUE-365` — `nginx.conf` has no `/api` proxy; compose still publishes `4200:80` | A login page that cannot log in. **Do not** implement the shared `storageState` the finding proposes — `playwright.config.ts:15` forbids it (2026-08-11 decision). |
| 15 | `ISSUE-381` — codegen emits `content?: never`; split the JSON and file routes | Typed annotation alone did not fix it. |
| 16 | `ISSUE-272` — orphaned BE workflow-instances API, no FE | Net-new FE story. |
| 17 | `ISSUE-378` — no `my-results` employee route; `byCategory` chips never render | |
| 18 | `ISSUE-382#1` — `>500`-row imports never poll | Wire always sends `total`, so the async branch is unreachable. |
| 19 | `ISSUE-380` — 3 decision-gated fields + 1 dead control | Item 1 (salary-grade `isActive`) is fixed. |
| 20 | **GAP-024** — ~19 sweep jobs get no `tenant_id` in logs | `TenantJobRunner.cs:31-40` sets `ITenantContext` but never `LogContext.PushProperty`. `JobLogContextFilterTests.cs:72-77` pins this as intended, conflating "cross-tenant" with "iterates every tenant". Isolation forensics blind on the higher-risk job class. |
| 21 | **GAP-018** — rate-limit store is in-process | Effective ceiling = 300/min × instance count. No behavioural 429 test (integration host disables the limiter). |
| 22 | **GAP-015** — Production email guard has no test | `EmailSenderDiRegistrationTests` never sets an environment name, so the Production branch is never entered. Non-Production still delivers no password-reset or lockout mail. |
| 23 | **GAP-031** — coverage measured, never gated | Deliberate; revisit once the number has been looked at. |
| 24 | **GAP-035** — per-tenant sender only on payslips | Password reset and lockout — which BR-1 designates non-suppressible — still use the global From. |
| 25 | **GAP-021** — calibration has no FE, no `CyclePhase.CompletedOn`, no `Performance.Calibrate` permission | BE shipped and tested. |

---

## 🟢 P3 — LOW / cleanup

`ENH-005` (FR-8 Redis cache only) · `ISSUE-276` (latent; no non-API host exists) · `ISSUE-289`, `ISSUE-301` (needs-decision) ·
`ISSUE-298` · `ISSUE-299` · `ISSUE-302` (test-health) · `ISSUE-303` · `ISSUE-373` (7 hardcoded fields; **downgraded from HIGH** — all 6 adapters exist) ·
`ISSUE-376` (client disconnects logged as 500s) · `ISSUE-382#2/#3` · `ISSUE-150`, `ISSUE-271` · `GAP-032` (`PastDue` is an enum state no code can reach) ·
`GAP-038` (reserved `admin` host is a dead login; docs still say `admin.yourhrm.com`) · `GAP-039` (caching deferral has no ADR) · `GAP-040` (tech-doc §10 documents folders that do not exist).

---

## 🚧 Decision gate — do not schedule until decided

| Item | The decision |
|---|---|
| **GAP-019** — 11 platform capabilities absent | Revenue/MRR, billing ops, platform-staff management, maintenance mode, broadcasts, growth/churn reports, platform report definitions + scheduling, Google and Apple sign-in. **No story exists for any of them.** Author stories or record the deferral. |
| **GAP-037** — outbound webhooks | Documented Phase-2 deferral, but tech-doc `:639`'s NFR table still reads as if built. |
| **GAP-020** — no *online* JWT rotation lever | Config rotation works and is tested; containment of a leaked signing key needs a rolling restart, and there is no global revocation cutoff. Reword the gap, don't close it. |
| `ISSUE-289`, `ISSUE-301`, `ISSUE-400` | Product calls, not build work. |
| **GAP-030** | Two BA stories are stubs. *(Register claim "zero test cases" is false — both are test-covered.)* |

---

## 🧾 Ledger flips owed — verified fixed, still recorded open

These carry `file:line` proof in the audit. They are **not** work; they are corrections. Route each
through `/verify-fix` — do not hand-edit a status.

`BUG-003` (systemic cross-tenant — `TenantAccessGuardMiddleware.cs:36-56`, 6 arms) · `BUG-056` · `BUG-298` ·
`BUG-301` · `BUG-307` (all 10 sites migrated; entry still claims nine remain) · `ISSUE-021` · `ISSUE-232` ·
`ISSUE-280` · `ISSUE-362` · `ISSUE-364` (**header says OPEN, its own body says RESOLVED 2026-08-10**) ·
`BUG-003 (US-ATT-004 ext)`.

**`ISSUE-377` is QA-execution debt, not code.** The release endpoint shipped. The remaining action is
`/test-us US-PRF-005` — three TCs are still `draft`. **Do not close it by editing TC frontmatter.**

### Register rows the audit contradicts
- **GAP-006** — only 3 of 6 named entities were real holes; the other 3 are deliberately allow-listed with RLS policies (`TenantQueryFilterCoverageTests.cs:67-79`). The register's "add 6 lines" framing **would have caused a regression**.
- **GAP-030** — "zero test cases" is false; both stories are test-covered.
- **GAP-034** — "assertions exist but never execute" understates it: there are **zero** axe assertions in the codebase.
- **GAP-020** — headline premise false; rotation exists and is tested.
- **GAP-040 / queue item E2** — references `HRM.ArchitectureTests`, a project that does not exist.

---

---

## ⚠ Reality check before diving in

**▶ CURRENT (re-surveyed 2026-07-15 — supersedes the 07-11 figures below):** the ledger holds **218 OPEN findings —
0 CRITICAL · 2 HIGH · 40 MED · 156 LOW · 20 ENH** (of 453 unique findings; 224 RESOLVED). The **2 HIGH are BUG-284 +
BUG-285** (work-week hardcode → wrong leave balances / wrong OT pay) — both live, both money, both in the CAL queue
above. The original 14-HIGH band was fully cleared across #262–299. ⚠ The file's own summary table (TEST-FINDINGS.md
lines 15–20) is **stale — do not use it**; four status encodings coexist in that ledger and 20 ENH entries carry no
`Status:` field at all (open by construction per its preamble).

_(historical, 2026-07-11)_ The ledger holds **244 OPEN findings (14 HIGH · 78 MED · 152 LOW)**. Many LOW/MED date to the June QA arc and some may
be **stale** (fixed-but-not-re-verified, or test-env/persona artifacts). **Do a triage-verify pass on the HIGH/MED band
first** (`/verify-fix` or a quick re-exec) before scheduling fixes — don't assume all 244 are live. Two ledgers are
**stale and must be reconciled** (P0). Line anchors below are into `docs/QA/TEST-FINDINGS.md`.

**Triage-verify result (2026-07-11b):** heading-by-heading reconciliation **confirms 14 genuinely-OPEN HIGH** (BUG-060-Payroll,
071, 077, 078, 080, 082, 084, 097, 100, 113, 123, 124, 125, 243) — the P1 list stands. **Re-verify-before-trusting (RESOLVED
token but body says "STILL PRESENT"):** **BUG-003 (CRIT** cross-tenant settings write — but memory's 2026-07-03 note says
CLOSED via #119 `TenantAccessGuardMiddleware`, so body is likely stale), **BUG-086** (HIGH, Leave 'Accrued' enum 500 — dup of
BUG-037), **BUG-002/BUG-005** (MED, graceDays default + localization). These 4 lead P1.

---

## 🔁 [NEW] Loop-discovered items — auto-healed 2026-08-21

> **Filed under CLAUDE.md rule #6**, which names *this* file as the fold target. My first heal folded into
> `GAP-CLOSURE-QUEUE.md` and skipped the COMPLETION-PLAN entirely; corrected here.
>
> Source: the D-migration slices (performance ×3, leave ×2, core-hr ×2) plus P1. ~28 out-of-lane discoveries,
> grouped into six findings rather than 28 IDs.

| finding | sev | disposition | rank |
|---|---|---|---|
| **[[BUG-306]]** — 7 FE calls to routes absent from the contract | HIGH | 3 of 9 **fixed** (P1); #4-#7 **decision-gated**; #8 delete; #9 = B6 build | gated / B6 at #5 |
| **[[BUG-307]]** — tenant plan limits **fail open** (`plan_id` matching no plan → unlimited) | MED↑ | **build** a guard + fix the data. *Re-rated up from LOW: the original described the cause (seed data), not the effect (a paid cap that silently does not exist).* | verify blast-radius first |
| **[[ISSUE-379]]** — 11 backend DTO gaps; 4 blank a whole feature surface | HIGH | **decision-gated** — add the fields, or remove the UI rendering them | gated |
| **[[ISSUE-380]]** — 5 dead FE items incl. a no-op Active toggle | MED | remove-dead-control; `isActive` is **decision-gated** | #7 |
| **[[ISSUE-381]]** — accrual-exposure emits no response schema | MED | fix-in-backend (Swashbuckle annotation) — *a hole in the contract gate itself* | #8 |
| **[[ISSUE-382]]** — 3 late-filed items (uploadImport union, ReleasedBy fallback, stale docstring) | MED/LOW | 1 decision, 2 cleanup | #7 |

**Re-sorted order** (severity × blast-radius × unblocks-others − gated): **E2 ArchitectureTests → E1 security
headers → C1 workflow seed → B6 lookups → remaining migration → cleanup**. Note E2 and E1 rank *above* the
half-finished migration: a guard's value compounds across the six modules still to come, and zero of the six
required §23.4 headers exist anywhere.

**★ The measurement that reframes the remaining work:** roughly **one field in five** of the hand-written FE
interfaces describes an endpoint that was never built. These were not an accurate API description that
drifted — they were written from what the UI wanted and never reconciled. **"Finish the migration" is not a
mechanical task and should not be estimated as one**; the remaining ~570 interfaces should be expected to
surface their own DTO gaps and absent routes.

**Two process failures recorded, because both will recur:**
1. **A heal must enumerate its sources, not recall them.** My first pass reconciled the recent slices and
   remembered the rest; four earlier findings were missed until the user asked.
2. **Auto-heal is a reflex, not a request.** Rule #6 and [[auto-heal-session-todo]] both say to do it every run,
   proactively, and to mirror items into the **live session TODO** — a ledger row the user has to dig for is
   not "tracked". The user has now had to point this out three times.

---

## 🔒 Standing rules (carry forward)
NEW-TENANT-TABLE RLS RULE (every new tenant_id table adds its dormant `tenant_isolation` policy in-migration) · **RLS
GUC is set by `TenantGucConnectionInterceptor` on connection open (NOT a request-wide tx)** · cache = read + auto-evict,
tenant-prefixed · RLS config-gated + reversible, committed OFF · BUG-068/252/264 retry-vs-tracked-state (manual tx under
`EnableRetryOnFailure` → wrap in `CreateExecutionStrategy().ExecuteAsync` + detach on rollback) · verify branch before
commit, no `--no-verify` · payroll approval stays on its bespoke `PayrollApprovalService` (converge later = a separate
US-PAY-XXX story) · **the full `dotnet test` gate is now reliable** (xUnit `maxParallelThreads:4`).

---

## Working method (unchanged)
One `feat/`|`fix/` branch per story/cluster off fresh `test/local-subdomains` → map seams (Explore) → LOCKED spec →
parallel backend-dev + frontend-dev on disjoint paths against a pinned contract → verify FULL suite on Postgres (Docker
up) + read-only auditors (integration-enforcer + test-authenticator) → commit / PR / merge each before the next.
Security/RLS-touching changes: re-run the local RLS-on validation (throwaway Postgres; method in Rls/README §runbook).
Auto-heal out-of-lane flags into this plan + `TEST-FINDINGS.md`.

---

## 🗓 Changelog

> Rollover history through 2026-08-22 (103 KB) moved to
> [`archive/COMPLETION-PLAN-changelog-to-2026-08-22.md`](archive/COMPLETION-PLAN-changelog-to-2026-08-22.md).
> Add new entries here; archive again when this section outgrows a screen.

- **2026-09-01 — full code-verified audit + rebuild.** 10 parallel `@requirements-auditor` passes over
  38 live findings, a 30-item random sample of the 188 terminal ones, and all 40 GAP rows. Findings:
  29% of the "live" backlog was already fixed; 0/29 false-resolved; 17/18/5 CLOSED/PARTIAL/OPEN on GAPs;
  every PARTIAL failed wiring or test-binding, never capability. Five superseded queue generations
  (256 KB) moved to `archive/`. 30 out-of-lane discoveries folded into `GAP-CLOSURE-QUEUE.md`.
  Two P0s found that belonged to no existing row: part-time overtime paid on a full-time base, and a
  blank `Jwt:PrivateKey` silently producing an ephemeral per-process signing key.

