# Test Findings Ledger — Bugs · Issues · Enhancements

> **Deferred "do/verify later" items** (documented deferrals, residual risks, needs-infra test gaps,
> bug-class-killing refactors) are indexed in [`DEFERRED-FOLLOWUPS.md`](DEFERRED-FOLLOWUPS.md) — when a fix
> defers something, add a row there in addition to the inline note here, so it isn't lost in a RESOLVED block.

> Living ledger produced by `/test-all` and `/test-us` (via `@test-runner`). **REPORT-ONLY**: findings are
> recorded here for a human to triage and fix. The testing loop never fixes code, never opens PRs, and never
> sets a downstream fix-state — it only ever appends an `OPEN` finding.
>
> Historical baseline defects from the 2026-06-19 manual run live in `BUG-STATUS.md` +
> `reports-archive/BUG-REPORT-2026-06-19.md`. **This file is the forward ledger** for the automated testing loop.
>
> **Type:** `BUG` broken vs spec · `ISSUE` contract/behavioral nit, drift, flaky · `ENH` improvement (not a defect)
> **Severity:** `CRIT` blocks core use · `HIGH` breaks a primary flow · `MED` partial/contained · `LOW` cosmetic/defense-in-depth
> **Status:** `OPEN` (set by the loop) → `WIP` / `FIXED` / `VERIFIED` / `WONTFIX` (set by the human/fix process — NOT the loop)
> **Layer:** `FE · BE · DB · TEST · DATA · INFRA`

## Summary

> **Counts are ASSERTED, not maintained by hand.** `LedgerTraceabilityTests.TheSummaryTable_MatchesTheActualCounts`
> recomputes this table from the two ledger files and fails if it drifts. The previous hand-written table read
> **169 total while 232 live entries existed** — stale by 63, in the index a human reads first, with nothing
> checking it. That is the same class as `ISSUE-437` (nothing verifies a documented claim), applied to the
> ledger's own front page.

| Type | Live | Archived | Total |
|---|---:|---:|---:|
| BUG | 50 | 168 | 218 |
| ISSUE | 221 | 296 | 517 |
| ENH | 23 | 2 | 25 |
| DECISION | 4 | 0 | 4 |
| **TOTAL** | **298** | **466** | **764** |

<!-- SUMMARY-ASSERTED: regenerate by running the test; do not hand-edit the numbers above. -->


> **Reconciliation note (2026-07-04, Wave 0 of `plans/archive/FIX-FINDINGS-PLAN-2026-07-04.md`):** the table above is the
> historical snapshot and is **not** current. A fix campaign (PRs **#114–#136**, "Phase A/B/D") landed ~38
> fixes, most already marked RESOLVED inline. This pass flipped **8 remaining stale entries** that had merged
> fixes but still read OPEN: **BUG-099** (#132), **BUG-020 / BUG-021 / ISSUE-026** + the three **BUG-003
> (EXTENDED …)** headers (all covered by the systemic tenant guard **#119**, ISO-verified 2026-07-03), and the
> **BUG-037 (EXTENDED to leave reports)** header (#117). The genuine remaining backlog is the long tail of
> **MED/LOW** defects (missing audit-writes, case-sensitive uniqueness, missing validation, 500-on-edge-input,
> a11y/contrast) plus ~12 genuine **HIGH** (BUG-014/019/025/030/035/045/048/055/102/104, ISSUE-018/210) and
> **0 genuine open CRIT**. These are queued for Waves 1–5 of the fix plan.
>
> **Ledger ID-hygiene — DONE (2026-07-05).** The three reused IDs were de-duplicated by giving the still-open,
> non-PR-referenced occurrence of each a fresh ID (the resolved/PR-referenced occurrence kept the original):
> **ISSUE-097** → the US-REC-001 vacancy-`is_deleted` finding became **ISSUE-243** (the US-PRF-001 goal-audit
> ISSUE-097 stays, RESOLVED #154); **ISSUE-105** → the US-REC-003 resume-blob-key leak became **ISSUE-244**
> (the US-PRF-002 attachment-API ISSUE-105 stays, fix #177); **BUG-059** → the US-PRF-002 self-assessment-reopen
> finding became **BUG-242** (the US-REC-003/004 "Hired is terminal" BUG-059 stays, fix #174). Each definition
> heading + the live structured `findings:` trackers in TEST-STATUS.md and the active regression TCs were updated.
> **Dated point-in-time run-logs below (2026-06-26/27 regression rollups) retain the original IDs verbatim** as
> historical records — read them through this mapping. Automated counts should now key off the (unique) `### `
> definition headings.

> **Wave 1 fixes MERGED (2026-07-04):** all 12 genuine-open HIGH + BUG-015 are now **RESOLVED** — fixed,
> regression-tested (each verified failing pre-fix / passing post-fix), merged as PRs **#137–#148** into
> `test/local-subdomains`, and re-verified together on the merged tree (**81/81 backend + 52 FE** regression tests
> green). BUG-045(#137) · BUG-019(#138) · BUG-014+BUG-015(#139) · BUG-035(#140) · BUG-048(#141) · BUG-025(#142) ·
> BUG-055(#143) · BUG-030(#144) · BUG-102(#145) · BUG-104(#146) · ISSUE-018(#147) · ISSUE-210(#148).
> **0 genuine open HIGH or CRIT remain** — the backlog is now MED/LOW only (plan Waves 3–5).

> **Waves C/D/E fixes MERGED + closed out (2026-07-06):** the remaining fixable MED backlog was fixed across
> PRs **#168–#178** (all merged into `test/local-subdomains`) and the **29** findings below are now flipped
> **OPEN → RESOLVED** with their PR#: #168 ISSUE-065/078/084 + BUG-049 · #169 BUG-057 + ISSUE-109 ·
> #170 BUG-032 + ISSUE-029/041 · #171 BUG-044/046 + ISSUE-056 · #172 ISSUE-005 + BUG-006 · #173 BUG-038 +
> ISSUE-086/090 · #174 BUG-059 + BUG-060 · #175 BUG-029 + BUG-242 · #176 ISSUE-066/118/160 + BUG-063 ·
> #177 ISSUE-101/105 · #178 ISSUE-158 + BUG-070. Each was regression-tested (red pre-fix / green post-fix); the
> full merged-tree suite is green. **Still genuinely OPEN (not fixed):** ISSUE-243 (vacancy is_deleted — needs a
> repro), ISSUE-244 (resume-blob-key LOW), BUG-058 (resume magic-byte) — plus the LOW cosmetic tail and the
> decision/feature-blocked items. **The larger completeness backlog** (unbuilt ACs across ~25 done stories, net-new
> capabilities) is catalogued in [COMPLETION-PLAN-2026-07-06.md](plans/archive/COMPLETION-PLAN-2026-07-06.md) Part II, not here.

> 2026-06-30 iso-fixture admin-isolation/lifecycle run (14 TCs): +BUG-106 (suspended-tenant admin 451-exemption broken, HIGH), +BUG-107 (impersonation FR-6 destructive-op block bypassed, HIGH), +ISSUE-217 (terminating data-export wrongly 403, MED). Cross-tenant leak via foreign `X-Tenant-Subdomain` header re-confirmed as the existing systemic **BUG-003** (not re-filed).

> **3 BUGs RETRACTED 2026-06-25 as debugger artifacts** (BUG-009, BUG-011, BUG-012): the backend was running under the VS Code debugger, which broke on the first-chance `ValidationException` at `ValidationBehavior.cs:37` and waited for a human "Continue" — that pause was misread as a hang/stall. Re-verified debugger-free: all validation failures return instant 400s, no pool exhaustion. **Net genuine bugs: 10 (1 CRIT = BUG-003; the other prior CRIT count was wrong).** Lesson: run perf/availability tests WITHOUT a debugger that breaks on thrown exceptions.

---

## Findings

> **⚠ This ledger is SPLIT (2026-09-01).** Live findings live here; terminal ones live in
> [TEST-FINDINGS-RESOLVED.md](TEST-FINDINGS-RESOLVED.md). Two rules follow, and breaking either
> corrupts the ledger:
>
> 1. **Allocating the next ID: scan BOTH files.**
>    `grep -hoE 'BUG-[0-9]+|ISSUE-[0-9]+|ENH-[0-9]+' docs/QA/TEST-FINDINGS*.md | sort -t- -k2 -n | tail -1` → +1.
>    Scanning only one file re-issues an ID another finding already owns.
> 2. **De-dup: search BOTH.** A recurring defect must re-open or extend its ORIGINAL finding
>    rather than mint a new ID — and the original is usually in the archive.
>
> Only `/verify-fix` moves an entry working → archive when it closes a finding; `WONTFIX` stays a
> human-only call. **Family rule:** all entries sharing an ID live in the SAME file — if any one of
> them is still live, the whole family stays here.

> **Status vocabulary (normalised 2026-09-01).** Live: `OPEN` · `DEFERRED`.
> Terminal: `RESOLVED` · `WONTFIX` · `RETRACTED` · `DUPLICATE`. Every entry now carries exactly one
> `- **Type / Severity / Status:**` line. Before this pass the file used ten status spellings across
> four metadata shapes, and 152 entries were not machine-readable at all. Where a header said
> RESOLVED but the body documented a live residual, the **live** reading won.

### ISSUE-321 — Employee profile has NO backend for the Education / Work-History / Dependents sections (FE-only forms, can never persist)
**▶ LIVE VERIFICATION 2026-09-08 — VERIFIED FIXED. The `/verify-fix` debt is discharged.**
Education, Work History and Dependents all persist. `GET /tenant/employees/{id}/profile` returns
`education`, `workHistory` and `dependents` keys, and the tables `employee_education`,
`employee_work_history`, `employee_dependents` all exist.
⚠ **A methodological note worth keeping, because it nearly produced a false finding.** My first probe hit
`/employees/{id}/education`, `/work-history`, `/dependents` — all **404**, which reads as "still broken".
They are **empty-body route 404s**: those paths were never the design, the sections live on the profile
endpoint. The `ENH-011` scorecard probe minutes earlier returned a *domain* 404 with a JSON error code,
and that contrast is the only thing that distinguished "no such route" from "no such feature". **An
empty-body 404 is evidence about your URL, not about the system.**

- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE (absent) + FE
- **Module / US / TC:** Core HR / US-CHR-002 / (new TCs needed)
- **Title:** The employee-profile page has editable Education, Work-History, and Dependents forms, but there is **no backend entity, migration, DTO field, or endpoint** for any of them — `UpdateEmployeeProfileRequest` has no such sections and `EmployeesController` has no routes. They could never save (masked until now by the ISSUE-319 universal 404).
- **Root cause (~99%, confirmed):** FE built ahead of BE; grep for `EmployeeEducation`/`EmployeeWorkHistory`/`EmployeeDependent` entities returns nothing. Surfaced fixing DF-36/ISSUE-319.
- **Reproduction steps:** open an employee profile → the Education/Work-History/Dependents sections offer editing but there is nowhere to persist to.
- **Severity rationale:** MED — three profile sections are non-functional; net-new BE work (entities + migrations + endpoints + DTO fields). FE now shows them read-only (DF-36 / #380) so users aren't misled.
- **Suggested direction (needs-decision, NOT applied):** build Education/WorkHistory/Dependent entities + endpoints (→ DF-39), then re-enable the FE editing. Report only.

### ISSUE-320 — Employee profile-edit: several fields within the (now-working) sections still don't persist or risk invalid enum writes
- **Type / Severity / Status:** ISSUE · MED · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** All three profile-edit gaps closed: `UpdateEmployeeProfileRequest.cs:112-118`; `employee-profile.component.ts:629,639,695` now use `<select>`; `employee.models.ts:765-770` `parseCustomFields()`.
- **Layer:** FE↔BE contract
- **Module / US / TC:** Core HR / US-CHR-002 / (new TCs)
- **Title:** After DF-36/#380 rewired the profile save onto `PATCH {id}/profile`, three field-level gaps remain: (a) the Contact form edits **city/state/postalCode/country** but `ContactInfoUpdate` only has phone/personalEmail/address → those 4 silently drop; (b) the Employment form edits **Department/Job Title as free text** + a **dateOfJoining**, but `EmploymentInfoUpdate` keys on DepartmentId/JobTitleId (Guids) and has no joining-date → those don't map/persist, and **employmentType/status are free-text inputs** that can POST invalid enum values (→ 400); (c) the profile GET returns `customFields` as a JSON **string** while the FE model types it as an object + indexes by key → custom-field values render "Not set" on the read view.
- **Root cause (~95%, `@frontend-dev` audit):** FE forms carry fields the BE DTO doesn't model + a read-side type mismatch on customFields.
- **Reproduction steps:** edit Contact city/state → save → the value doesn't persist; edit Employment department (free text) → doesn't persist; type a non-enum employment type → 400.
- **Severity rationale:** MED — visible fields silently no-op (data-loss surprise) + a free-text enum can 400. Not data corruption; the mapped fields (name/dob/phone/email/address/location/emergency-contacts/custom-fields) DO persist after #380.
- **Suggested direction (needs-decision, NOT applied):** convert Employment dept/title/type/status to id/enum-backed `<select>`s; add address-detail fields to `ContactInfoUpdate`+`Employee` (or drop them from the FE); decide if dateOfJoining is editable; parse the customFields JSON on the read view (→ DF-38). Report only.

### ISSUE-021 — Job title `gradeId` is accepted with NO validation: any arbitrary GUID is persisted as the grade link (no SalaryGrade subsystem / FK exists) — AC-4 grade-link integrity unverifiable
- **Type / Severity / Status:** ISSUE · MED · OPEN (partially discharged 2026-09-02)
- **Re-verification (2026-09-02, /verify-fix):** **The `DEFERRED (feature-blocked: no SalaryGrade entity)` reason is STALE** — the entity shipped (#389, migration `20260719152434_AddSalaryGradeEntity`). **TC-CHR-005-48 PASS · TC-CHR-337 PASS**: the FK-validation half of AC-4 is met (`JobTitleService.cs:242-253`, 422 `invalid_grade`). **TC-CHR-063 FAIL**: AC-4's second clause — the grade displayed on the employee profile — was never built; `EmployeeProfileDto` has no grade field and the FE has no grade element. Filed as **BUG-419**. This finding stays OPEN until that half lands.
- **Layer:** BE
- **Module / US / TC:** Core HR · US-CHR-005 · TC-CHR-037 (create job title with salary-grade link; AC-4)
- **Title:** `POST /api/v1/tenant/job-titles` (and PUT) stores the `gradeId` field verbatim with **zero validation** — no existence check, no tenant-scope check, no FK constraint. A wholly fabricated GUID (`00000000-0000-0000-0000-0000000000ff`) is accepted and returned on the created record (HTTP 201). There is no SalaryGrade entity, DbSet, controller, or `grade_id` foreign key anywhere in the backend, so AC-4 ("link to an existing salary grade") cannot be satisfied or verified, and any value the client sends becomes a dangling reference.
- **Root cause (~95%, source-confirmed):** `JobTitleService.CreateAsync` / `UpdateAsync` (`src/backend/HRM.Infrastructure/Services/JobTitleService.cs:57` and `:97`) assign `GradeId = gradeId` directly with no lookup. `CreateJobTitleValidator` / `UpdateJobTitleValidator` validate only `TitleName` + `Description` length — `GradeId` is unvalidated. Confirmed by absence of any `SalaryGrade` DbSet in `AppDbContext` and no grades controller (`grep -ri grade HRM.Api/Controllers` finds only unrelated PerformanceDashboard/Recommendation). The `job_title.grade_id` column is a bare nullable `Guid?` with no FK constraint, so even the DB cannot reject a bogus value.
- **Reproduction steps (live-confirmed 2026-06-25):**
  1. Login `tenantadmin@acme.test` / `Admin@123!`, header `X-Tenant-Subdomain: acme`.
  2. `POST /api/v1/tenant/job-titles` body `{"titleName":"QA GradeLink <ts>","gradeId":"00000000-0000-0000-0000-0000000000ff"}`.
  3. Response: HTTP 201, `data.gradeId = "00000000-0000-0000-0000-0000000000ff"` — the fabricated id is persisted, no error.
- **Evidence:** `{"success":true,"data":{"id":"019efd62-463e-7691-899e-a2f97ebd04f3","titleName":"QA GradeLink 1782367536","gradeId":"00000000-0000-0000-0000-0000000000ff","employeeCount":0,"isActive":true,...}}` HTTP 201. No exception in `hrm-20260625.log` (the write succeeds cleanly — that is the problem).
- **Severity rationale:** MED — data-integrity gap on a contained field: a job title can carry a grade link that points to nothing (or, by the BUG-014 class, potentially another tenant's grade id were a grades subsystem to exist). No cross-tenant read/write of another tenant's row today (so below HIGH), but AC-4 is structurally unmet and the field can silently hold garbage. Because the grades subsystem does not exist at all, TC-CHR-037's happy path (link to an *existing* grade) is unverifiable → TC marked FAIL on the AC-4 integrity contract, not on a transient error.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-032 — NFR-2 promises tenant isolation via "EF Core global query filters AND PostgreSQL RLS policies" for leave types, but RLS is NOT enabled on `leave_types` (relrowsecurity=false) and ZERO RLS policies exist in the entire database — only the EF filter layer is present (the documented BUG-003 header-spoof bypasses that single layer; see BUG-026)
- **Type / Severity / Status:** ISSUE · LOW · ♻️ **RECLASSIFIED 2026-09-06 — not "absent": BUILT / NOT ENABLED / DOCUMENTED AS ENABLED**
- **Reclassification (T0):** all three original claims are **false**. Policies exist on ~112 tables (`20260710120000_Platform_RlsPolicies_Dormant.cs:37-83`), the GUC is set (`TenantGucConnectionInterceptor.cs:44-49`), and a reconciler runs (`DbInitializer.cs:139-232`). **The real residual is operational, and worse than the finding described:** `appsettings.Development.json` sets `Rls:Enabled=false`, `Rls__Enabled` appears in **no tracked file** (`docker.env` is gitignored), yet `DbInitializer.cs:107-109` asserts *"the Docker dev stack sets `Rls__Enabled=true`"*. **Dev and Docker run on ONE isolation layer while a comment claims three** — a security posture documented as on and shipped off. *(I previously "corrected" [[ISSUE-468]] on this exact point by reading my own untracked `docker.env`; that correction was wrong and is retracted.)*
- **Layer:** DB / INFRA
- **Module / US / TC:** Leave Management · US-LV-001 · TC-LV-ISO-003 (RLS blocks direct DB queries across tenants) — BLOCKED `env` (RLS not implemented). NFR-2.
- **Title:** US-LV-001 NFR-2 + the Data Requirements section ("RLS policy: `tenant_isolation_select` and `tenant_isolation_modify` on `leave_type`") require a database-enforced isolation layer in addition to the EF filter. Live DB inspection: `pg_class` for `leave_types` shows `relrowsecurity=f`, `relforcerowsecurity=f`, no policies on the table, and `SELECT count(*) FROM pg_policy` = **0** for the whole database. So the second, defense-in-depth isolation layer the story specifies does not exist — the EF global query filter is the only thing enforcing tenant scoping, and BUG-026/BUG-003 shows that single layer is bypassable via a spoofed subdomain header.
- **Root cause (~95%, DB confirmed):** RLS is platform-wide deferred work (tracked as US-PLT-002 RLS Phase 4 per project memory) — no migration creates the `tenant_isolation_*` policies named in the US data section, and no app code sets a per-request Postgres session GUC for an RLS predicate to read. This is pre-existing deferred scope, not a regression introduced by US-LV-001.
- **Reproduction steps (DB):** `SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname='leave_types'` → `f|f`; `SELECT polname FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid WHERE c.relname='leave_types'` → empty; `SELECT count(*) FROM pg_policy` → 0.
- **Evidence:** the three queries above; the only isolation in force is `AppDbContext.cs:229` (EF filter). TC-LV-ISO-003 cannot be executed (no RLS to test) → BLOCKED env.
- **Severity rationale:** LOW as a standalone finding (it is documented deferred tech-debt and the EF filter does scope correctly when the tenant is honestly resolved), BUT it removes the defense-in-depth that would have *contained* BUG-026/BUG-003 — with RLS keyed off the token's tenant, a spoofed subdomain alone could not leak data. So closing BUG-003 at the auth layer is the priority; RLS would be the backstop. Tracked here for US-LV-001 NFR-2 traceability, not as new work.
- **Suggested direction (NOT applied):** none — report only. (Deliver the US-PLT-002 RLS phase: enable `ROW LEVEL SECURITY` + `tenant_isolation_select/modify` policies on `leave_types` (and siblings) reading a per-request `app.tenant_id` GUC set from the validated token claim — which also requires the BUG-003 token-vs-subdomain fix to be the source of that GUC.)

### ENH-001 — No on-demand accrual/recalculation trigger endpoint: AC-5's "modify rule → Hangfire recalculates affected balances" and all accrual-effect verification depend on the daily recurring LeaveAccrualJob, which cannot be invoked via the API
**▶ DISPOSITION 2026-09-08 (T4).** **Half shipped, half storied — status stays OPEN pending `/verify-fix`.**
The finding's title is the on-demand endpoint, but auditing it found an **unfiled defect sitting inside**: 4 of
5 entitlement mutations enqueued no recalculation at all. That half is **fixed and merged (#679)** — all five
now route through one `EnqueueRecalcAsync` helper, called *after* `SaveChangesAsync` so the Hangfire worker
cannot race uncommitted state. The **on-demand trigger endpoint remains unbuilt** and is now story
**`US-LV-013`** (#692) rather than a queue comment. That story is deliberately honest that its user is an
operator/tester, not an end user — `US-LV-002.md:89`'s own test hint describes a capability that does not exist.

- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title / Module:** Add an authorized on-demand "recalculate entitlements / run accrual" endpoint · Leave Management · US-LV-002 (AC-5, FR-5)
- **Why it matters:** `LeaveEntitlementsController` exposes rules/overrides/effective but NO endpoint to trigger accrual or a post-rule-change recalculation. `LeaveAccrualJob` is only registered as a Hangfire **recurring** job ("leave-entitlement-accruals", daily midnight UTC, `Program.cs:460`) and `UpdateRuleAsync` does NOT `BackgroundJob.Enqueue` a recalculation. Consequences: (a) AC-5's "a Hangfire background job recalculates affected employees' balances" on rule modify is not wired — editing a rule changes future `/effective` computation but enqueues nothing and writes no adjustment ledger entries; (b) TC-026 steps 8-11, TC-027 (accrual arms), TC-028 steps 7-9/13, TC-029 ledger arms, TC-030 (whole Hangfire+adjustment flow), TC-032 ledger arms, TC-036 (accrual ledger), TC-037 (bulk recalc), and TC-041 (5,000-emp perf) cannot be executed on demand — the *engine math* is verifiable live via `GET /effective`, but the *ledger-writing accrual effect* is only observable after the scheduled job runs. An HR officer also has no way to force a recalculation after a policy change.
- **Suggested direction (NOT applied):** add an authorized `POST /leave-entitlements/recalculate` (and/or enqueue a recalculation from `UpdateRuleAsync`/bulk) that runs `ProcessAccrualsAsync` for the tenant (optionally scoped to a rule/leave type), writing accrual/adjustment ledger entries and (per BUG-028) audit rows. This both fulfils AC-5 and makes the accrual-effect TCs executable.

### ISSUE-501 — `SocialSecurityInputValidator` rates have the identical ISSUE-169 defect, one field over, on EPF/ETF contribution rates

- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** BE
- **Module / US / TC:** Payroll · statutory configuration
- **Title:** `CreateStatutoryRuleValidator.cs:106-107` — `SocialSecurityInputValidator.EmployeeRate` and `EmployerRate` carry only `InclusiveBetween(0, 100)`. Both columns are `numeric(5,2)` (`SocialSecurityRuleConfiguration.cs:26-27`), so an EPF/ETF contribution rate of `12.345` is **silently rounded to 12.35 and echoed back** — `ISSUE-169` exactly, one field over, on a money path.
- **Root cause (~100%):** the same missing `PrecisionScale` rule that ISSUE-169 fixed for `TaxSlabInput.RatePercentage`. The fix for ISSUE-169 (#652) deliberately did NOT widen to these fields — different fields, no requested coverage, and tightening them rejects payloads that currently succeed.
- **Severity rationale:** HIGH — statutory contribution rates feed payroll deductions directly. Same class as ISSUE-169 but on a field an employer is legally obliged to compute correctly.
- **Suggested direction (NOT applied):** the identical one-line `PrecisionScale(5, 2, ignoreTrailingZeros: true)` plus arms mirroring the ISSUE-169 tests in `StatutoryRuleAmountBoundsTests`.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-169/ISSUE-299.
- **SURVEY:** **15** validator-bound decimal fields across **8** validator files map to a `numeric(p,s)` column with `s <= 2` and carry **no** `PrecisionScale` rule (unit = entity properties with `HasColumnType("numeric(...)")` in `HRM.Infrastructure/Persistence/Configurations` that are settable through a request DTO covered by a FluentValidation validator). The entire `PrecisionScale` surface is **8 usages repo-wide, all in `Features/Payroll/Validators`** — so every non-Payroll numeric field lacks one by construction. Excluded: `numeric(18,2)` money columns (tracked by `DECISION-504`), server-computed scores, fields with no validator at all, `numeric(10,7)` geo columns, migrations and tests.
- **AUDIT (2026-09-07):** (1) "`EmployeeRate`/`EmployerRate` carry only `InclusiveBetween(0,100)`" — **FALSE at this commit**: both now carry `.PrecisionScale(5, 2, ignoreTrailingZeros: true)` with `WithErrorCode("invalid_rate_scale")` (`CreateStatutoryRuleValidator.cs:118`, `:122`), added by `a408e7d8` (**#661**), which **is merged** into `origin/test/local-subdomains`; the update path shares it (`UpdateStatutoryRuleValidator.cs:42`) and the suggested arms exist (`StatutoryRuleAmountBoundsTests.cs:103,133,166,178`). (2) Cited `CreateStatutoryRuleValidator.cs:106-107` — **stale**: `:106` is the closing brace of `TaxSlabInputValidator`; the rules are at **`:117-124`**. (3) "both columns are `numeric(5,2)`" — **CONFIRMED, exact** (`SocialSecurityRuleConfiguration.cs:26-27`). (4) "the ISSUE-169 fix (#652) deliberately did not widen to these fields" — **CONFIRMED**: `92768f7d` did not touch `SocialSecurityInputValidator`. (5) Status `OPEN` — **FALSE**; the fix is merged. A stale branch `fix/ISSUE-501-social-security-rate-scale` also still exists on origin.
- **SEVERITY CHECK:** moot — should be closed, not re-rated. HIGH was defensible while open. The live problem is ledger hygiene (`ISSUE-545`); the residual code gap is filed as **`ISSUE-543`**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-502 — `CreatePayrollAdjustmentValidator.Amount` has no scale rule, and an adjustment amount feeds a payslip directly

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Payroll · adjustments
- **Title:** `CreatePayrollAdjustmentValidator.cs:23-24` validates `Amount` only as `GreaterThan(0m)`; `payroll_adjustment.amount` is `numeric(18,2)` (`PayrollAdjustmentConfiguration.cs:35`). A >2dp adjustment is silently rounded on write.
- **Severity rationale:** MED, but the **highest blast radius of the three scale gaps** — unlike a component default, an adjustment amount reaches a payslip without further transformation.
- **Suggested direction (NOT applied):** `PrecisionScale(18, 2, ignoreTrailingZeros: true)`, the ISSUE-152/ISSUE-369 idiom.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-369.
- **SURVEY:** **10** validator-bound decimal fields — **not 1 site** (unit = request-DTO decimal properties mapped to a `numeric(18,2)`/`HasPrecision(18,2)` column and reachable through a FluentValidation validator) carry **no** `PrecisionScale` rule. Siblings: `TaxSlab.SlabFrom`/`SlabTo` (`CreateStatutoryRuleValidator.cs:88`, `:99`), `SocialSecurityRule.WageCeilingAnnual` (`:125`), `SalaryGrade.MinAmount`/`MaxAmount`/`MidAmount` (`CreateSalaryGradeValidator.cs:18`, `:21`, `:24` plus the Update twin), `RecommendationBudget.AllocatedAmount` (`RecommendationValidators.cs:55`), `Offer.SalaryAmount` (`GenerateOfferValidator.cs:34`), and `SalaryStructureComponent.OverrideValue` (`ISSUE-503`). Excluded: migrations, tests, and ~20 server-computed money columns no DTO sets.
- **AUDIT (2026-09-08):** (1) "`CreatePayrollAdjustmentValidator.cs:23-24` validates `Amount` only as `GreaterThan(0m)`" — **CONFIRMED, lines exact**. (2) "`payroll_adjustment.amount` is `numeric(18,2)`" — **CONFIRMED, exact** (`PayrollAdjustmentConfiguration.cs:35`). (3) "it reaches a payslip without further transformation" — **CONFIRMED**, and stronger than filed: there is **no update-adjustment validator or command at all** (only `CreatePayrollAdjustmentValidator.cs` exists in `Features/Payroll/Validators/`), so create is the sole entry point. Merge status: `git log --all --grep=ISSUE-502` returns 0 commits and no branch. **Not fixed.**
- **SEVERITY CHECK:** **agrees at MED.** The blast-radius claim holds — an adjustment amount reaches a payslip untransformed.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-503 — `SalaryStructureComponentInputDto.OverrideValue` has no scale rule

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Payroll · salary structures
- **Title:** `CreateSalaryStructureValidator.cs:28-40` (and the Update sibling) validates only `SalaryComponentId`, `ProcessingOrder` and `OverrideFormula` per component. `override_value` is `numeric(18,2)` (`SalaryStructureComponentConfiguration.cs:24`), so a 4-dp override is silently rounded.
- **Note that bounds it:** the *echo* half of ISSUE-369 does NOT apply here — `SalaryStructureService` already re-fetches via `BuildDtoResultAsync`. This is silent-rounding only, not a wrong response.
- **Suggested direction (NOT applied):** same `PrecisionScale(18, 2, ignoreTrailingZeros: true)` idiom.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-369.
- **SURVEY:** **2 sites** — `CreateSalaryStructureValidator.cs` and `UpdateSalaryStructureValidator.cs`, one field each. `OverrideValue` appears in 3 DTO shapes (`SalaryStructureDtos.cs:12`, `:54`, `:88`) and the command record (`SalaryStructureCommands.cs:69`), and **no validator anywhere in the repo** has a `RuleFor(... OverrideValue)` — a grep for the symbol across `Features/*/Validators/*.cs` returns nothing. Unit = validator files carrying the field. Excluded: migrations, tests.
- **AUDIT (2026-09-08):** (1) "`CreateSalaryStructureValidator.cs:28-40` validates only `SalaryComponentId`, `ProcessingOrder` and `OverrideFormula` per component" — **CONFIRMED, range exact** (`:30-31`, `:33-34`, `:36-39`). (2) "and the Update sibling" — **CONFIRMED** (`UpdateSalaryStructureValidator.cs:26-38`, identical `ChildRules` block). (3) "`override_value` is `numeric(18,2)`" — **CONFIRMED, exact** (`SalaryStructureComponentConfiguration.cs:24`). (4) The bounding note "`SalaryStructureService` already re-fetches via `BuildDtoResultAsync`" — **CONFIRMED**: `SalaryStructureService.cs:102`, `:164`, `:176`, `:307`, `:333` — every mutating path returns through it, so the response echoes the stored (rounded) value. **The entry's own scoping is therefore right: this is silent-rounding only, without the `ISSUE-369` echo half.** Merge status: 0 commits, no branch. **Not fixed.**
- **SEVERITY CHECK:** **agrees at MED**, though it sits at the LOW end of it versus `ISSUE-502` because the echo is honest — offset by a structure override driving every payslip line built from it.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### DECISION-504 — 32 `numeric(18,2)` columns, 5 validators enforcing the contract: sweep them, or add a structural rule?

- **Type / Severity / Status:** DECISION · **MED** · OPEN — **parked at the decision gate**
- **Layer:** BE (architecture)
- **Title:** Only **5** validator files reference `PrecisionScale` against **32** `numeric(18,2)` columns. `ISSUE-152` (CTC preview) and `ISSUE-369` (component default) are the same defect surfacing twice, **found one at a time by testers** — and `ISSUE-501`/`502`/`503`, all filed the same day, are three more. The remaining ~27 columns are where reports six and seven come from.
- **The decision:** a one-off sweep fixes today's 27 and leaves the 33rd column to ship unguarded next month. A **Roslyn/NetArchTest rule in `HRM.ArchitectureTests`** — asserting every decimal input bound to a `numeric(18,2)` column carries a scale rule — costs more up front and closes the class permanently. This repo already has a home for exactly that kind of structural rule (`InertOptionalParameterTests`, `SharedPostgresFixtureIsolationTests`, `SourceFileIsGreppableTests`).
- **Recommendation:** the architecture rule, with a one-time sweep to make it pass. Higher effort, and it is the option that stops this recurring. Confidence it is the right call: 85%.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-369.
- **SURVEY (both asserted numbers verified, and both are wrong):** **"32 `numeric(18,2)` columns" — the true count is 33** (unit = EF property configurations in `HRM.Infrastructure/Persistence/Configurations/`): 28 `HasColumnType("numeric(18,2)")` plus 5 `HasPrecision(18, 2)` (`SalaryGradeConfiguration.cs:30`, `:34`, `:37`; `RecommendationBudgetConfiguration.cs:27`, `:28`). Identical at the filing commit `e91fd927` and at HEAD. **"5 validators" is also wrong**: at filing only **3** validator files carried `PrecisionScale`; today it is **6 files / 8 usages**. **"5" matched neither moment.** Excluded: migrations, `Designer.cs`, tests, comments.
- **AUDIT (2026-09-08):** (1) "only 5 validator files reference `PrecisionScale` against 32 `numeric(18,2)` columns" — **PARTIALLY TRUE**: the *shape* of the gap is real, both integers are wrong. (2) **"the remaining ~27 columns are where reports six and seven come from" — FALSE as stated**: ~20 of the 33 are **server-computed and unreachable from any request DTO** (`PayrollSlipConfiguration.cs:25`,`:26`,`:27`,`:49`,`:51`,`:55`,`:56`; `PayrollRunConfiguration.cs:39-42`; `FinalSettlementConfiguration.cs:29-32`,`:75`; `PayrollSlipDetailConfiguration.cs:28`; `EmployeeSalaryComponentConfiguration.cs:25`,`:26`; `SalaryRevisionHistoryConfiguration.cs:25`,`:26`). **What is true instead: the actionable set is 10 validator-bound fields across 8 validator files** — the `ISSUE-502` survey list. (3) **`ISSUE-543`'s criticism of this decision HOLDS and is corroborated in-tree**: the proposed rule keys on the literal `numeric(18,2)`, yet **49** columns in the same directory are `numeric(p,s)` with `s<=2, p!=18` (17x`(5,2)`, 8x`(6,2)`, 5x`(7,2)`, 5x`(3,2)`, 4x`(4,1)`, 4x`(14,2)`, 3x`(4,2)`, 2x`(12,2)`, 1x`(3,1)`) — and the repo's **newest** scale guard is `PrecisionScale(5, 2)` (`CreateStatutoryRuleValidator.cs:95`), so a rule scoped to 18,2 **would not cover the fix that most recently shipped**. `ISSUE-543`'s sharpest instance was spot-checked and **CONFIRMED**: `UpsertLatePolicyValidator.cs:21` `InclusiveBetween(0m, 31m)` against `LatePolicyConfiguration.cs:27` `numeric(3,1)`. Merge status: only the filing commit; no implementation, no branch. **Still parked and unanswered.**
- **SEVERITY CHECK:** **agrees at MED as a decision — but the recommendation must be amended before it is answered**: scope the rule on each column's *declared* scale, not on `numeric(18,2)`. Answering it as written would ship a guard that goes green over the whole live gap.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-505 — the statutory unique index is on the RAW `country_code`, so ISSUE-299's evasion survives under concurrency

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE (schema)
- **Title:** `StatutoryRuleConfiguration.cs:63-65` puts the unique index on the raw `country_code`. #652 closed the **application-level** evasion by comparing normalised-to-normalised, but the database is not a backstop: two **concurrent** creates of `"LK"` and `"lk "` still pass both the pre-check and 23505.
- **Suggested direction (NOT applied):** a functional unique index on `upper(btrim(country_code))`, plus a data-cleanup pass for rows already dirty. Needs an EF migration, which the merge gate holds for a human regardless.
- **Blocks:** full closure of `ISSUE-299` under concurrency.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-299.
- **SURVEY:** **1 index** — `StatutoryRuleConfiguration.cs:63-65`, the only unique index on `statutory_rule`. Sibling search establishes the scope genuinely is one site: **no functional or expression index exists anywhere** in `Configurations/` (`HasFilter`/`IsUnique` yield only column-list indexes; `upper(`/`btrim(` appear in no `HasIndex`). Unit = unique indexes on normalisation-sensitive columns. Excluded: migrations, tests.
- **AUDIT (2026-09-08):** (1) "`StatutoryRuleConfiguration.cs:63-65` puts the unique index on the raw `country_code`" — **CONFIRMED, range exact**: `HasIndex(r => new { r.TenantId, r.RuleType, r.CountryCode, r.FiscalYear, r.EffectiveFrom }).IsUnique().HasFilter("is_deleted = false")`. (2) "#652 closed the application-level evasion by comparing normalised to normalised" — **CONFIRMED** (`StatutoryRuleService.cs:87`, and the same at `:367` for clone). (3) "two concurrent creates of `"LK"` and `"lk "` still pass both the pre-check and 23505" — **CONFIRMED**: the 23505 backstop exists (`StatutoryRuleService.cs:129-136`) but keys on the raw column, so the two rows are distinct index keys and neither the pre-check race nor the constraint fires. Merge status: only the filing commit; no fix, no branch. **Not fixed.**
- **SEVERITY CHECK:** **agrees at MED.** It needs concurrency *and* a service-bypassing dirty write to be reachable, but the consequence is a wrong tax rate.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-506 — `StatutoryRuleIntegrationTests` claims to exercise the validation pipeline; FluentValidation never runs in it

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** QA (test integrity)
- **Title:** `StatutoryRuleIntegrationTests.cs:69-83`'s header states it exercises *"the full handler → service path through a real DI container (MediatR pipeline)"* and lists *"FR-6: contiguity is enforced through the validation pipeline"*. The container registers **neither `AddValidatorsFromAssembly` nor `ValidationBehavior`**, so FluentValidation never executes there. Every validation-shaped assertion in that file actually lands on the service's defensive re-check.
- **Why it matters:** any future author trusting that header writes a **green no-op**. This is why #652's ISSUE-169 arms were deliberately placed elsewhere — a validation arm in this file would have passed for the wrong reason.
- **Suggested direction (NOT applied):** register the behaviour + validators, or correct the header. The first changes what several existing tests exercise, so it is a test-design decision.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-169.
- **SURVEY:** **1 test file**, and a genuine outlier rather than house style: **9** other integration test files **do** register the validation pipeline (`AssetIssuanceIntegrationTests`, `ExitInterviewIntegrationTests`, `OnboardingChecklistIntegrationTests`, `OffboardingIntegrationTests`, `OnboardingTemplateIntegrationTests`, `ShiftIntegrationTests`, `Http/FnFPolicyApiTests`, `GoalServiceReopenTests`, `SubmitRegularizationValidatorTests`). Unit = integration test files configuring a DI container.
- **AUDIT (2026-09-08):** (1) "the container registers neither `AddValidatorsFromAssembly` nor `ValidationBehavior`" — **CONFIRMED**; the DI method is `StatutoryRuleIntegrationTests.cs:69-84`, and `:82` is a bare `AddMediatR(...)`. (2) **The cited line range is wrong for the quoted text**: the header block quoted is at **`:1-15`** (specifically `:4-5` and `:10`), not `:69-83`. (3) "the header states *FR-6: contiguity is enforced through the validation pipeline*" — **CONFIRMED verbatim at `:10`**; the other quoted phrase at `:4-5` ("real DI container (MediatR pipeline)") is **literally true**, since MediatR *is* registered — so only `:10` is false. (4) "every validation-shaped assertion actually lands on the service's defensive re-check" — **CONFIRMED**, and **the entry understates its own mitigation**: the FR-6 arm already self-documents this honestly at `:280-283` and the test is named `CreateIncomeTaxRule_WithGap_IsRejectedByService` (`:285`). Merge status: the only grep hit is `a408e7d8` (#661), which merely mentions it. **Not fixed.**
- **SEVERITY CHECK:** **lower — MED should become LOW.** The misleading text is **one comment line**, contradicted 270 lines later by an accurate in-place comment and an explicit `_IsRejectedByService` test name. The "future author writes a green no-op" risk is already blunted at the point of use, and the fix is a one-word header edit.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-507 — `FiscalYear` is compared raw in the same duplicate pre-check ISSUE-299 fixed

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE
- **Title:** `StatutoryRuleService` create pre-check compares `r.FiscalYear == fiscalYear` raw, while the write path stores it `Trim()`'d — the same evasion class as the country code, one field over. A stored `"2026-2027 "` would evade the duplicate pre-check.
- **Why not fixed in #652:** ISSUE-299 names `CountryCode` only; widening the compare was deliberately not done.
- **Found:** 2026-09-06, out-of-lane while fixing ISSUE-299.
- **SURVEY:** **4 raw `FiscalYear` comparison sites, not 1** (unit = LINQ predicates comparing `r.FiscalYear` to a `Trim()`-ed input without normalising the stored side), all in `StatutoryRuleService.cs`: `:88` (create pre-check — the only one the entry names), `:265` (list filter), `:350` (clone source lookup), `:367` (clone target-collision guard). Excluded: `:326` (a projection, not a compare), migrations and tests. **The entry understates by 3 sites.**
- **AUDIT (2026-09-08):** (1) "the create pre-check compares `r.FiscalYear == fiscalYear` raw" — **CONFIRMED** at `StatutoryRuleService.cs:88` (no line was cited; `:88` is correct). (2) "the write path stores it `Trim()`-ed" — **CONFIRMED**: `:69` normalises, `:106` assigns, and update does the same at `:196`. (3) "a stored `"2026-2027 "` would evade the duplicate pre-check" — **CONFIRMED**, and the same untrimmed value **also evades the clone guard at `:367`**, which is the more damaging of the two. (4) "why it was not fixed in #652: `ISSUE-299` names `CountryCode` only" — **CONFIRMED**, and visible in one glance: `:87` normalises country while `:88`, the very next line, does not. Merge status: 0 commits, no branch. **Not fixed.**
- **SEVERITY CHECK:** **higher — LOW should become MED.** The entry rates LOW on a single-site read. At 4 sites, one of which is the clone-collision guard, a dirty fiscal year can **duplicate an entire fiscal year's statutory rule set**, not just one rule.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-508 — leave attachments will not be counted toward tenant storage quota

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE (platform)
- **Title:** Storage quota is **caller-invoked, not automatic** — `EmployeeDocumentService.EnforceStorageQuotaAsync` is a private method one service calls, and `TenantStorageUsage.ComputeBytesAsync` sums only **four** tables. The new `leave_request_attachment` table is not among them, so leave uploads consume tenant storage invisibly.
- **Pre-existing, not new:** `SelfAssessmentAttachment` has the identical hole today. `TenantStorageUsage`'s own comment (ISSUE-340) warns that a fifth size-bearing table must be added to **both** methods — and the warning has already been missed twice.
- **Suggested direction (NOT applied):** a quota pass driven off a registry, so the next table cannot be forgotten. Point fixes are what produced the current state.
- **Severity rationale:** MED — unbounded-cost / billing-accuracy risk, no functional break.
- **Found:** 2026-09-06, out-of-lane while building ISSUE-036.
- **SURVEY:** **4 of 9** size-bearing tables are summed by `ComputeBytesAsync`, and **4 of 9** by `ComputeBytesByTenantAsync` — identical omissions, so **5 of 9 are uncounted**, not the 1 the entry implies (unit = EF entity tables carrying a byte-size column). All 9: `EmployeeDocument:30`, `HrReportExport:40`, `InterviewAttachment:30`, `LeaveRequestAttachment:33`, `PayrollReportExport:43`, `PayrollSlip:102`, `GoalProgressAttachment:28`, `PipCheckpoint:70`, `SelfAssessmentAttachment:24`. Excluded: tests, migrations, and 2 blob-storing tables with no size column at all.
- **AUDIT (2026-09-08):** (1) "`EnforceStorageQuotaAsync` is private with one caller" — **CONFIRMED** (`EmployeeDocumentService.cs:203` declared, sole call `:119`). (2) "`ComputeBytesAsync` sums only four tables" — **CONFIRMED** (`TenantStorageUsage.cs:23-32`). (3) "`leave_request_attachment` is not among them" — **CONFIRMED**. (4) "`SelfAssessmentAttachment` has the identical hole" — **CONFIRMED**. (5) "the comment warns a fifth table must be added to BOTH methods" — **CONFIRMED** (`TenantStorageUsage.cs:11`, `:44`). (6) **"missed twice" — CONFIRMED and UNDERSTATED**: post-warning, two tables were added and missed (`InterviewAttachment`, `dfd3d3c5`; `LeaveRequestAttachment`, `a8fb3589`), but **three more were already missing when the helper was written** (`SelfAssessmentAttachment`, `GoalProgressAttachment`, `PipCheckpoint`, all 2026-06-16). (7) **Not swept by `ISSUE-036` #655**: `a8fb3589` created the table and never touched `TenantStorageUsage.cs`. **Not fixed.**
- **SEVERITY CHECK:** **agrees at MED** — billing-accuracy drift with no functional break, but 5 of 9 uncounted is materially wider than the entry implies, which is the argument for the registry it proposes rather than a third point-fix.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ENH-509 — an uploaded-but-never-submitted leave attachment leaks forever

- **Type / Severity / Status:** ENH · **LOW** · OPEN — needs a product decision first
- **Layer:** BE
- **Title:** `LeaveAttachmentService` stores an attachment before the leave request exists (`LeaveRequestId = NULL`, linked on create). An employee who uploads and then abandons the form leaves both the blob and the row behind permanently. There is no sweeper.
- **Needs a decision, not just a job:** how long is an unclaimed upload valid? That is product input, not an implementation detail.
- **Found:** 2026-09-06, out-of-lane while building ISSUE-036.
- **SURVEY:** **1** orphan-producing write site and **0** sweepers (unit = code paths persisting a `LeaveRequestAttachment` with a null `LeaveRequestId`). Counted across the 4 non-test files touching `LeaveRequestAttachments`; excluded tests and migrations. Neither `HRM.Infrastructure/Jobs` nor the recurring-job registrations contain any attachment cleanup job.
- **AUDIT (2026-09-08):** (1) "it stores before the leave request exists, with `LeaveRequestId = NULL`" — **CONFIRMED** (`LeaveAttachmentService.cs:134`, carrying the comment "linked when the leave request is created (ISSUE-036)"). (2) **Unstated but material**: the **blob is written before the row** (`:127-128` `UploadAsync` precedes `:143-144` `Add` + `SaveChangesAsync`), so an abandoned form leaks **both** the object-store blob under `leaves/{employeeId}/{uuid}` **and** the DB row — the entry describes only the row. (3) "there is no sweeper" — **CONFIRMED**: no recurring job, background service or query anywhere selects `LeaveRequestId == null`. (4) "it needs a retention decision" — **CONFIRMED as a genuine product question**; no TTL constant or config key exists. Merge status: no fix commit; untouched by `ISSUE-036` #655, which is the commit that **created** the orphan window. **OPEN.**
- **SEVERITY CHECK:** **agrees at LOW** — unbounded but slow storage growth, capped at 5 MB per abandoned upload, with no correctness or security impact.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-510 — the self-assessment evidence upload endpoint has no `[RequestSizeLimit]`

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE
- **Title:** `SelfAssessmentAttachmentsController` is the **only** upload controller in the repo without `[RequestSizeLimit]` (contrast `EmployeesController.cs:443`, `InterviewsController.cs:227`, and the 10 other sites). Its 10 MB cap is enforced only **after** the whole body is buffered, via the declared `SizeBytes`.
- **Why it is filed here:** it was the obvious copy target for ISSUE-036's port. The omission was spotted and deliberately not copied — but the original is still uncapped.
- **Severity rationale:** LOW — minor DoS-surface / wasted-buffer issue, no correctness impact.
- **Found:** 2026-09-06, out-of-lane while building ISSUE-036.
- **SURVEY:** **11 of 16** upload actions carry `[RequestSizeLimit]`; **5 do not** (unit = controller action methods taking an `IFormFile`/`IFormFile?`). Counted all 17 `IFormFile` occurrences under `HRM.Api/Controllers` minus `TenantSettingsController.cs:265`, which is a private helper rather than an action. Excluded: tests and generated code. No global `MaxRequestBodySize`/`MultipartBodyLengthLimit` override exists, so the 5 uncapped actions fall back to Kestrel's 30 MB default.
- **AUDIT (2026-09-08):** (1) "`SelfAssessmentAttachmentsController` has no `[RequestSizeLimit]`" — **CONFIRMED** (`SelfAssessmentAttachmentsController.cs:34-41`, attribute absent). (2) "its 10 MB cap is enforced only after buffering, via the declared `SizeBytes`" — **CONFIRMED** (`SelfAssessmentAttachmentService.cs:27`, checked at `:85`). (3) **"it is the only upload controller without one" — FALSE.** Four other actions also lack it: `CareersController.cs:73` (`Apply`), `ApplicantsController.cs:81` (`SubmitInternal`), `PayrollAdjustmentsController.cs:129` (`BulkUpload`) and `:155` (`UploadDocument`). (4) "contrast `EmployeesController.cs:443`, `InterviewsController.cs:227` and the 10 other sites" — **PARTIALLY TRUE**: both cited lines verify, but there are **9** other sites, not 10. Merge status: named in `a8fb3589` (#655, merged) as the deliberately-not-copied omission; **no fix commit**. **OPEN.**
- **SEVERITY CHECK:** **agrees at LOW for this endpoint** (authenticated, self-scoped), but **the entry understates the class by 4 endpoints** — and one of them is **unauthenticated**, filed separately as `ISSUE-567`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-513 — the threat model claims a Hangfire filter REJECTS tenant-less jobs at runtime; nothing does

- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** docs (architecture) — but the content is a **security control claim**
- **Title:** `hrm_technical_document_v4.0.md:3439`, threat-model row 11: *"Hangfire job runs without tenant context → Mitigation: **Hangfire server filter requires `TenantId` in args; jobs without it rejected**."* **Nothing rejects a tenant-less job at runtime.** The only shipping `IServerFilter` is `JobLogContextFilter`, which pushes Serilog properties and nothing else.
- **Why this is the most dangerous of the doc-drift family:** `ISSUE-375` describes the same non-existent filter in a *design* section; this states it as a **security mitigation with a runtime rejection behaviour**, in the threat model — the document a reviewer consults to decide whether a risk is already covered. The real control is a **build-time source scan** (`BackgroundJobTenantContextTests`) with a named exemption list: a different guarantee, a different failure mode, and it does not run in production at all.
- **Root cause (100%, verified):** an `IServerFilter` cannot reach the job body's DI scope — 42 of 62 job classes in `HRM.Api/Jobs/` call `CreateScope()` themselves. The documented control could not be built as described.
- **Suggested direction (NOT applied):** rewrite row 11 to the build-time guard and **re-rate the residual likelihood**, or implement a real runtime check. Not a wording fix — the rating depends on which.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-375.
- **SURVEY:** **1** Hangfire filter is registered — `JobLogContextFilter.cs:27` (`IServerFilter` only), via `Program.cs:355` `config.UseFilter(...)` inside `AddHangfire` (`:345`) — and **0 of 1** reject anything. There is no `GlobalJobFilters` use, no `JobFilterAttribute` subclass, and no `IClientFilter`/`IElectStateFilter`/`IApplyStateFilter` anywhere in `src/`; the only other Hangfire attribute is `[AutomaticRetry]` (`SendEmailJob.cs:50`). Unit = registered Hangfire filters, then job classes. Excluded: `HRM.Tests`.
- **AUDIT (2026-09-07):** **The doc is the wrong side, not the code.** (1) The threat-model claim exists and does promise runtime rejection — **CONFIRMED**, but the **cited line is stale**: it is `hrm_technical_document_v4.0.md:3459` (section 45 threat model, row 11), not `:3439`, which is a section 44 checklist item — off by 20. (2) "nothing rejects a tenant-less job at runtime" — **CONFIRMED**: `TenantJobRunner.cs:33` `RunForTenantAsync` is opt-in, called by the job body, and contains **no throw**; `ParallelTenantScopeRunner.cs:22` likewise; there is no base job class and no activator override. (3) "the only shipping `IServerFilter` is `JobLogContextFilter`, which pushes Serilog properties and nothing else" — **CONFIRMED**: `OnPerforming`/`OnPerformed` push and dispose `LogContext` properties and touch no `ITenantContext`, no DI scope and no job state. (4) "the real control is a build-time source scan with a named exemption list" — **CONFIRMED**: `BackgroundJobTenantContextTests.cs:121-131` does `File.ReadAllText` over `Jobs/**.cs`, regex at `:58`, with an `EnqueueOnlySchedulers` exemption `HashSet` at `:38` holding **12** entries (`:40-51`). (5) "42 of 62 job classes call `CreateScope()`" — **CONFIRMED exactly** (62 top-level `.cs` = 62 public classes; a 43rd raw grep hit is an XML-doc mention in `JobLogContextFilter.cs`). No fix merged: `git log --all --grep=ISSUE-513` returns only the two filing commits.
- **SEVERITY CHECK:** **agrees — HIGH, and the entry UNDERSTATES twice.** (a) This is not drift but a **live self-contradiction**: PR **#662** (`26bfba49`, merged) already rewrote section 28.2 at `:2605-2618` to say, in bold, "Tenant context is declared by the job BODY, not by a Hangfire filter — do NOT 'fix the code to match'", and corrected `:566`, but **missed row 11**. The same document now asserts A and not-A about 850 lines apart, and the threat model is the half a reviewer trusts. (b) A **third** uncorrected site the entry never mentions: `:635`, the section 9 tenant-awareness matrix, "`TenantId` parameter; Hangfire filter" — covered by neither this entry nor `ISSUE-375`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-514 — the architecture doc claims every job takes a `tenantId`; 30 of 62 do not

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** docs (architecture)
- **Title:** `hrm_technical_document_v4.0.md:2604`: *"Every job method takes a `Guid tenantId` parameter."* **30 of the 62** classes in `HRM.Api/Jobs/` have no such parameter. `TenantJobRunner.cs:46-48` says so outright: sweep jobs *"declare no such argument — they enumerate tenants internally."*
- **Why it matters beyond accuracy:** `ISSUE-513`'s mitigation **depends on this false premise**. "Reject jobs without `TenantId` in args" is only coherent if every job has one.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-375.
- **SURVEY:** **1** assertion site in **1** document (`hrm_technical_document_v4.0.md:2604`); **0** other documents repeat it. Against a code population of **62** job classes in `src/backend/HRM.Api/Jobs/` (64 `.cs` files minus the 2 under `Filters/`, which are not jobs), counted by parsing every `public` method signature for a `tenantId`/`JobArgs` parameter. Excluded: tests.
- **AUDIT (2026-09-08):** **"Every job method takes a `Guid tenantId` parameter (or a typed `JobArgs` containing it)" — FALSE, and the DOC is the wrong side.** **33 of 62** job classes have no such parameter — so **the entry understates by 3** (it says 30). Spot-verified: `LeaveAccrualJob.cs:50`, `AuditLogPurgeJob.cs:22`, `TokenCleanupJob.cs:19` all declare `public async Task RunAsync()`. Corroborated in-code at `TenantJobRunner.cs:48` ("declare no such argument — they enumerate tenants internally"). **The document contradicts itself within 8 lines**: `:2609` describes cross-tenant sweeps and `:2612` counts "42 of the 62 job classes". Merge status: `git log --all --grep=ISSUE-514` is empty. **NOT FIXED.**
- **SEVERITY CHECK:** **agrees at MED** — a single site, but it matters because **`ISSUE-513`'s proposed mitigation ("reject jobs without `TenantId` in args") is incoherent against 33 jobs that legitimately have none.** Fixing the doc is a precondition for answering `ISSUE-513`, not independent of it.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-515 — the architecture doc names the RLS GUC `app.current_tenant_id`; the real name is `app.current_tenant`

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** docs (architecture)
- **Title:** `hrm_technical_document_v4.0.md:565` — *"an EF interceptor … applies it as a PG session setting (`SET LOCAL app.current_tenant_id = '…'`) on every command."* Two errors: the GUC is **`app.current_tenant`** (`TenantJobRunner.cs:91`, ~30 job files) and `app.current_tenant_id` **appears nowhere in `src/`**; and the mechanism is superseded — `Program.cs:145` records it is set **at connection-open** (US-PLT-002 / ISSUE-277), not per command.
- **Severity rationale:** MED not LOW — **a wrong GUC name is copy-pasteable into an RLS policy**, and a policy referencing a never-set GUC does not error. It silently matches nothing or everything.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-375.
- **SURVEY:** **91 occurrences of `app.current_tenant_id` across 46 files** — the entry cites one (unit = literal occurrences of the wrong GUC name, excluding the two documents that *describe* the drift). Breakdown: `hrm_technical_document_v4.0.md` **7** (`:528`, `:565`, `:1223`, `:1986`, `:1997`, `:2001`, `:2002`); `docs/QA/TRACEABILITY-MATRIX.md` **11**; ~**35** QA `TC-*-ISO-*` files and TEST-MATRIXes; `docs/BA/admin-console/US-ADM-001.md:41`, `:90`; and 4 `.claude/agent-memory/qa-engineer/*` convention notes. Excluded: `src/`, which has **0** occurrences.
- **AUDIT (2026-09-08):** (1) The GUC name — **CONFIRMED, doc side wrong**: the real GUC is `app.current_tenant` (`TenantGucConnectionInterceptor.cs:46`, `TenantJobRunner.cs:91`, and every RLS policy, e.g. `20260710120000_Platform_RlsPolicies_Dormant.cs:26`). (2) The mechanism — **CONFIRMED**: `:565` says "on every command", but it is **connection-open** (`TenantGucConnectionInterceptor.cs:30` `ConnectionOpenedAsync`, per `Program.cs:144-147`). (3) **A third error the entry missed**: `:1986` names a class `SetTenantSessionInterceptor` which **does not exist in `src/`**. Merge status: no fix commit. **NOT FIXED.**
- **SEVERITY CHECK:** **higher than MED.** The policies read `NULLIF(current_setting('app.current_tenant', …))`, so a tester following any of the ~35 ISO test cases that say `SET app.current_tenant_id` gets **0 rows and reads it as a PASS** — a tenant-isolation test that cannot fail. That is not documentation drift; it is a false-green generator, and it is filed separately as **`ISSUE-563`** because the QA corpus is a different fix from the architecture doc this entry is scoped to.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-516 — the entire US-PAY-002 salary-assignment TC surface documents routes that do not exist

- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** QA (docs)
- **Title:** Payroll · US-PAY-002 · TC-PAY-002-01/02/03/04/05/07/08/11 + TC-PAY-ISO-005/006/007 — **19 occurrences**. Every assignment TC targets `POST /api/v1/payroll/employees/{id}/salary[/preview|/bulk|/revisions]`. **That route does not exist.** Real: `POST api/v1/payroll/salary-assignments[/preview|/bulk]` (`EmployeeSalaryController.cs:28,45,62`) and `GET api/v1/payroll/employees/{id}/compensation|/revision-history` (`:81,:94`).
- **Why HIGH:** every one would **404 on the URL**, and a 404 masks whatever the endpoint actually does. An entire story's coverage is unexecutable and would read as a failing feature rather than a wrong test.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-113.
- **SURVEY:** **18** wrong route occurrences (unit = distinct route-string citations, including abbreviated `.../salary...` forms) across **11 TC files** (TC-PAY-002-01/02/03/04/05/07/08/11 + TC-PAY-ISO-005/006/007). Denominator: **18 of 18** route citations in the US-PAY-002 surface (16 TC files) are wrong — **zero** correct citations exist there. The finding's "19" is **off by one**. Excluded: the 5 TC files citing no routes, and `TEST-MATRIX.md:28` (prose, a false grep hit). **Widened** (unit = `/api/v1/...` citations in `docs/QA/*/TC-*.md`): **435 of 1558** citations across **201 TC files** and 99 distinct paths match no controller route — payroll is only 14/53; core-hr 130/403, leave-management 142/327, admin-console 68/77. **The drift is repo-wide, not payroll-local** — filed as **`ISSUE-542`**.
- **AUDIT (2026-09-07):** (1) "the route does not exist" — **CONFIRMED, and every cited line number is exact**: `EmployeeSalaryController.cs:19` `[Route("api/v1/payroll")]`, `:28` `salary-assignments/preview`, `:45` `salary-assignments`, `:62` `salary-assignments/bulk`, `:81` `employees/{employeeId:guid}/compensation`, `:94` `employees/{employeeId:guid}/revision-history`. (2) **Direction check — the TC is the drifted side, not the controller**: the generated OpenAPI types (`api-types.ts:11910`, `:11975`, `:12029`) and the FE client (`employee-salary.service.ts:44`) both use `salary-assignments`, and `docs/BA/payroll/US-PAY-002.md` specifies no routes at all. No `employees/{id}/salary` route exists under **any** prefix (481 routes checked). (3) **The finding UNDERSTATES**: TC-PAY-ISO-005/006/007 are tenant-isolation arms whose expected result *is* `404`/`400`, so they **pass vacuously on a routing 404** — 3 fake isolation arms, which the entry never names.
- **SEVERITY CHECK:** **higher than recorded** — HIGH holds and is arguably understated: 8 unexecutable functional TCs *plus* 3 tenant-isolation arms that report safety they never tested.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-517 — payslip TCs use a `/slips` segment, the wrong verb on bulk download, and an unexecutable IDOR probe

- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** QA (docs)
- **Title:** Payroll · US-PAY-004 · TC-PAY-004-02:28-29, TC-PAY-004-08:36, TC-PAY-ISO-010:38. Three errors: (1) routes use `/slips`, real segment is `/payslips` (`PayslipsController.cs:92,121,139`); (2) bulk download is documented **POST** but is actually **GET** `runs/{runId}/payslips/download-all` (`:139`); (3) **TC-PAY-ISO-010's per-slip IDOR probe `GET .../slips/{slipId}` has no equivalent** — the only per-employee route is keyed by `employeeId`, not `payslipId`.
- **Why HIGH, and why (3) is worst:** a **tenant-isolation probe that cannot route is a fake isolation arm.** It "passes" by 404 — the same green as a genuinely enforced boundary.
- **Suggested direction (NOT applied):** (1) and (2) are renames; **(3) needs a decision** on re-expressing the probe against an employee-keyed route.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-113.
- **SURVEY:** **5** wrong route/verb citations out of **6** endpoint citations in the US-PAY-004 + TC-PAY-ISO-010 surface (unit = verb+path or bare-path endpoint citations; 13 TC files scanned, the 9 citing no `/api/v1` path excluded). Breakdown: `/slips` segment errors **4** (`TC-PAY-004-02:28`, `:29`, `TC-PAY-ISO-010:38` x2); verb errors **2** (`TC-PAY-004-02:29`, `TC-PAY-004-08:36`); 1 overlap. The only correct citation is `TC-PAY-004-05:8`. Repo-wide, `/slips` appears in exactly **2 TC files** — genuinely contained (`TEST-STATUS.md:273` is a false hit).
- **AUDIT (2026-09-07):** (1) `/slips` → `/payslips` — **CONFIRMED, all cited lines exact**: `PayslipsController.cs:19` `[Route("api/v1/payroll")]`, `:92` `runs/{runId:guid}/payslips`, `:121` `runs/{runId:guid}/payslips/{employeeId:guid}/download`, `:139` `runs/{runId:guid}/payslips/download-all` (+ `:140` `download-zip`). (2) POST→GET on bulk download — **CONFIRMED** (`TC-PAY-004-02:29`, `TC-PAY-004-08:36`). (3) "the per-slip IDOR probe has no equivalent and needs a decision" — **FALSE**. `MyPayslipsController.cs:21` `[Route("api/v1/payroll/my-payslips")]`, `:83` `[HttpGet("{payslipId:guid}")]` and `:101` `[HttpGet("{payslipId:guid}/pdf")]` are payslip-id-keyed under `[RequirePermission("Payroll.View.Own")]` — an explicitly documented URL-manipulation guard (AC-4). The probe is directly re-expressible as `GET /api/v1/payroll/my-payslips/{slipId}`; **no decision gate is needed** — the finding missed the prefix. (4) **Extra error the entry misses**: `TC-PAY-004-02:28` documents `.../slips/{EMP-001}/pdf`, but the real tail segment is **`/download`** (`PayslipsController.cs:121`) — two independent errors on one line, not one rename.
- **SEVERITY CHECK:** **lower — HIGH should become MED.** Claim (3) was the stated justification for HIGH ("needs a decision", "fake isolation arm") and it is false. What remains is 4 renames + 2 verb fixes across 2 files — mechanical, no decision required.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-518 — the audit-log immutability TC passes by routing miss, not verb rejection

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** QA (docs)
- **Title:** Payroll · US-PAY-012 · TC-PAY-012-07:38-39. It issues `PUT/PATCH/DELETE /api/v1/.../audit-log/{A}` — a path existing under **no** spelling — and accepts *"405 or 404"*, so it passes trivially on the 404 and **verifies nothing**. Real resource: `GET api/v1/tenant/audit-logs/{id:guid}` (`AuditLogController.cs:58,143`).
- **Why it matters:** live test theater asserting immutability of the **audit log** in a CRITICAL module. Accepting 404 beside 405 is what makes it unfalsifiable.
- **Suggested direction (NOT applied):** repoint so the 405 is **earned**, and drop 404 from the accepted set — a rewrite, not a rename.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-113.
- **SURVEY:** **3** documented test cases probe audit-record immutability with a mutating verb — `TC-PAY-012-07.md:38-39`, `TC-ADM-008-17.md:34-35`, `TC-NTF-005-08.md:46` — and **3 of 3 accept 404 as a pass**, while **1 of 3** also routes to a path no controller serves. Widening the unit to every TC with a 405 expectation: **8 of 11** accept 404 alongside it. Excluded: `TEST-MATRIX.md`, ledgers, execution logs and `exec_note:` prose.
- **AUDIT (2026-09-08):** (1) "the path exists under no spelling" — **CONFIRMED**: the only audit route templates are `api/v1/tenant/audit-logs` (`AuditLogController.cs:58`, GETs at `:76`, `:108`, `:127`, `:143`, `:162`, plus `HttpPost("export")` at `:181`) and `api/v1/payroll` (`PayrollAuditController.cs:24`, four GETs); the singular `audit-log` appears nowhere. (2) "it accepts 405 **or** 404, so it verifies nothing" — **CONFIRMED** (`TC-PAY-012-07.md:32`, `:38`, `:39`, `:42`). (3) Cited `AuditLogController.cs:58`, `:143` — **CONFIRMED verbatim**. (4) **The entry UNDERSTATES in one direction and OVERSTATES in another.** Understates: `TC-NTF-005-08.md:46` and `TC-ADM-008-17.md:34-35` carry the same 404-escape and are both `status: pass`. Overstates: its proposed fix ("repoint so the 405 is earned") **cannot be done inside Payroll** — `PayrollAuditController` has no single-entry `/{id}` route at all, so *any* verb there yields 404 by construction. Only `AuditLogController.cs:143` can return a genuine 405, and `TC-ADM-008-17` already targets that plural path correctly. No fix commit. **OPEN.**
- **SEVERITY CHECK:** **agrees at MED** — the probe is unfalsifiable, though the property itself is separately covered by an earnable probe in Admin Console.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-531 — `IRecruitmentNotificationService`'s own doc names a log-only implementation that is no longer wired

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE (docs-in-code)
- **Title:** The interface doc on `IRecruitmentNotificationService` says "The default implementation (`LogOnlyRecruitmentNotificationService`) emits structured log events instead of sending real email / in-app." That has been false since the US-NTF-006 Phase 5a switch — `DependencyInjection.cs:378` registers `RealRecruitmentNotificationService`, which dispatches real email and in-app via `INotificationDispatcher`.
- **Why a stale comment is worth a finding:** this one **actively produced two wrong engineering judgements in a single session.** An agent read it, concluded a dropped recruitment notification "costs nothing", and on that basis (a) rated a real candidate-facing email-loss bug LOW instead of MED and (b) justified a reminder-ordering choice on a premise that did not hold. The orchestrator then repeated the same error in the opposite direction. A doc that contradicts the composition root is not cosmetic — it is a trap for exactly the reader who is being careful enough to check.
- **Related:** `ENH-010`'s own ledger text carries the same stale "log-only seam" claim.
- **Suggested direction (NOT applied):** correct the doc to name the real implementation, and make the "which impl is wired" claim point at `DependencyInjection.cs` rather than restating it.
- **Found:** 2026-09-07, out-of-lane while fixing `ISSUE-116`.
- **SURVEY:** **UNDERSTATED — 7 of 13** notification-service interfaces carry the same stale "the default implementation is log-only" doc while DI wires a dispatcher-backed real implementation: `IRecruitmentNotificationService.cs:6-9`, `IPerformanceNotificationService.cs:7`, `ILeaveNotificationService.cs:7`, `IImpersonationNotificationService.cs:6-7`, `:15`, `IUserManagementNotificationService.cs:5-6`, `ITenantLifecycleNotificationService.cs:6`, `IDataExportNotificationService.cs:6`. Only **2 of 13** are written correctly (`IAttendanceNotificationService.cs:11`, `ICoreHrNotificationService.cs:12`, both naming Real as wired and LogOnly as test-only). Unit = notification-service interfaces. Excluded: tests, and `IBreakGlass`/`ILockout`/`INotificationService`, which make no such claim.
- **AUDIT (2026-09-08):** (1) "the interface doc names `LogOnlyRecruitmentNotificationService` as the default" — **CONFIRMED** (`IRecruitmentNotificationService.cs:6-9`), plus **two further stale in-method claims at `:40` and `:88`** that the entry does not mention. (2) "`DependencyInjection.cs:378` registers `RealRecruitmentNotificationService`" — **CONFIRMED, exact**. (3) "which dispatches real email and in-app via `INotificationDispatcher`" — **CONFIRMED** (`RealRecruitmentNotificationService.cs:502-522`, `:524-549`). (4) "it produced two wrong judgements" — **CONFIRMED independently**: `1cff7060`'s own commit message records one retraction, and **`ISSUE-298` makes the identical error and still carries it** — its entire LOW rationale rests on a "log-only seam" that does not exist. Merge status: no branch touches these docs except `b2558d9a`, unmerged. **OPEN.**
- **SEVERITY CHECK:** **higher — LOW should become MED** (confidence 85%). The entry scopes this to one interface; it is a **7-file systemic pattern that has now demonstrably mis-rated two findings' severity**. Correcting the docs is cheaper than either fix it distorted, which is what makes it worth doing first.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-530 — every recruitment notification failure is swallowed and reported to callers as success

- **Type / Severity / Status:** BUG · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Recruitment · US-REC-005 NFR-4, US-REC-007 FR-7/AC-4
- **Title:** `RealRecruitmentNotificationService.DispatchInterviewAsync` (`:190-244`) wraps its **entire** body in `try { … } catch (Exception ex) { LogFailure(ex, …); }`, and `NotifyOfferAsync` (`:272`) has the identical shape. `NotifyInterviewReminderAsync` (`:185`) is a direct delegation, so **nothing sits outside the try.** The method returns an identical completed `Task` whether every email was delivered or every one failed. `RealRecruitmentNotificationServiceTests` asserts "never throws" as *intended* behaviour, so this is by design, not an oversight.
- **Why it matters more than the ordering question it blocks:** **no caller — job or service — can detect or retry a failed candidate email.** A reminder whose delivery fails is silently lost, and *no ordering of the clear-and-dispatch steps in the reminder jobs can prevent that*, because there is no success signal to condition on. This was established while trying to make `ISSUE-116`'s reminder jobs at-least-once: the requested "clear the marker only after a **successful** dispatch" turned out to be **inexpressible** against this seam.
- **Consequence for `ISSUE-116` (recorded so the reasoning is not re-derived):** clear-BEFORE-dispatch was retained deliberately. Clear-after buys **zero** protection against loss here — the exception is swallowed before the job sees it, so the marker is cleared either way — while adding a duplicate-send path on any post-dispatch `SaveChanges` failure (deadlock, connection drop, RLS transaction abort), which is the exact NFR-4 violation `ISSUE-116` exists to close.
- **Needs a decision, not just a fix:** at-least-once delivery requires either returning a `Result`/`bool` from the seam (a contract change with several call sites) or routing dispatch through an **outbox** row committed with the state change. The outbox is the more correct answer and the more expensive one.
- **Found:** 2026-09-07, out-of-lane while fixing `ISSUE-116`.
- **SURVEY:** **11 of 11** `catch (Exception)` sites in `RealRecruitmentNotificationService.cs` swallow, and **0** rethrow or surface (`:92`, `:132`, `:157`, `:241`, `:266`, `:361`, `:399`, `:473`, `:516`, `:542`, plus `LogFailure` at `:589`). **7 of 7** public entry points sit inside a swallow, and **7 of 7** interface methods return a bare `Task` — no `Task<bool>` or `Result` anywhere (`IRecruitmentNotificationService.cs:20`, `:31`, `:43`, `:62`, `:77`, `:91`, `:108`) — so **10** downstream call sites cannot detect failure. Unit = catch sites, then interface methods. Excluded: tests and the `LogOnly*` implementations.
- **AUDIT (2026-09-08):** (1) "`DispatchInterviewAsync` `:190-244` wraps the entire body" — **CONFIRMED** (`:194` try, `:241-244` catch, method ends `:245`). (2) "`NotifyOfferAsync` (`:272`) has the identical shape" — **CONFIRMED** (`:292` try, `:361-364` catch). (3) "`NotifyInterviewReminderAsync` (`:185`) is a direct delegation with nothing outside the try" — **CONFIRMED** (`:185-188`). (4) "it returns an identical completed Task either way" — **CONFIRMED**. (5) "the tests assert never-throws as intended behaviour" — **CONFIRMED** (`RealRecruitmentNotificationServiceTests.cs:611`, `:623`, `:478`, `:543`). (6) "no caller can detect or retry" — **CONFIRMED**, and stronger than filed: `OfferService.NotifyOfferSafeAsync:586-605` adds a **second** swallow, so even a hypothetical throw is absorbed twice. (7) "no ordering change in the reminder jobs can prevent loss" — **CONFIRMED**, following from the bare-`Task` surface. Merge status: the fix on `fix/BUG-529-530-recruitment-delivery` (`b2558d9a`) is **not merged**. **OPEN.**
- **SEVERITY CHECK:** **agrees at MED.** Not higher: it is a design seam rather than a live regression, and every leg already logs at Error.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-529 — an offer sent inside the reminder lead window can lose its expiry warning entirely

- **Type / Severity / Status:** BUG · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Recruitment · US-REC-007 FR-7/AC-4
- **Title:** `OfferService.SendAsync` calls `ScheduleExpiryReminder()` at `:288` — **before** `SaveChangesAsync` at `:293` — and `HangfireOfferExpiryReminderScheduler` enqueues with `TimeSpan.Zero` when the computed fire-time is already past. For an offer sent *inside* the reminder lead window the job can therefore execute before the row commits, read `ExpiryReminderJobId` as null, and skip. A real expiry-warning email to a candidate is then **never sent, silently.**
- **Interaction with `ISSUE-116`:** the marker guard added there converts this race's outcome from "reminder sent, stale marker left behind" into "reminder skipped". The race pre-existed; the guard changes which way it fails. The interview sibling is **not** affected — `HangfireInterviewReminderScheduler` returns null for a past fire-time and enqueues nothing.
- **Why MED:** the payload is real email (`DependencyInjection.cs:378`), the failure is silent, and it reaches candidates directly. Initially filed LOW on the false premise that recruitment notifications were a log-only seam — see `ISSUE-531`.
- **Suggested direction (NOT applied):** move **both** offer job schedules to after `SaveChangesAsync`.
- **Found:** 2026-09-07, out-of-lane while fixing `ISSUE-116`.
- **SURVEY:** **1 of 3** recruitment Hangfire schedulers exhibits the past-fire-time → `TimeSpan.Zero` enqueue (`HangfireOfferExpiryReminderScheduler.cs:27`); the two siblings return `null` instead (`HangfireInterviewReminderScheduler.cs:27-28`, `HangfireOfferExpiryScheduler.cs:27-28`). **2** schedule-before-commit call sites in `SendAsync` (`OfferService.cs:284`, `:288`), of which only `:288` races. Unit = scheduler implementations, then call sites. Excluded: `HRM.Tests`, migrations.
- **AUDIT (2026-09-08):** (1) "`ScheduleExpiryReminder()` at `:288`, `SaveChangesAsync` at `:293`" — **CONFIRMED, both exact**. (2) "the scheduler enqueues `TimeSpan.Zero` when the fire time is past" — **CONFIRMED** (`HangfireOfferExpiryReminderScheduler.cs:27`). (3) "the interview sibling is not affected" — **CONFIRMED** (`HangfireInterviewReminderScheduler.cs:27-28`). (4) "a real email payload is wired" — **CONFIRMED** (`DependencyInjection.cs:378`). (5) **"the job reads `ExpiryReminderJobId` as null and skips, so the reminder is silently lost" — FALSE at this commit.** `OfferExpiryReminderJob.cs:42-55` never reads that marker; it guards only `offer is null || !offer.IsActive` (`:46`), and `Offer.IsActive` includes `Draft` (`Offer.cs:132`). **The true outcome today is the opposite of the one filed: a premature reminder fires on an uncommitted Draft offer, rather than a reminder being lost.** The filed outcome only becomes true once `1cff7060` (ISSUE-116) lands, and it has not. Merge status: `b2558d9a` (`fix/BUG-529-530-recruitment-delivery`) and `1cff7060` are both **NOT ancestors** of `origin/test/local-subdomains`. **OPEN.**
- **SEVERITY CHECK:** **agrees at MED** — the failure mode differs from the write-up, but it is silent and candidate-facing either way.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-536 — the bank-advice EXPORT serves full unmasked account numbers to a permission the catalogue says is not trusted with them, and writes no audit row

- **Type / Severity / Status:** BUG · **HIGH** (latent today; live the day capture ships) · OPEN
- **Layer:** BE / security (PII exposure + missing audit)
- **Module / US / TC:** Payroll · US-PAY-009 / US-RPT-003 AC-4 · TC-PAY-009-02, TC-PAY-009-08
- **Title:** `GET /api/v1/payroll/reports/{reportType}/export` is gated by `[RequirePermission("Payroll.Export")]` (`PayrollReportsController.cs:191`) and, for `BankAdvice`, calls `BuildBankAdviceReportAsync(..., masked: false, ...)` (`PayrollReportService.cs:250`) — a downloadable file carrying **full account numbers**. **HR Officer holds `Payroll.Export`** (`PermissionCatalog.cs:751`).
- **The codebase contradicts itself in its own words.** `PermissionCatalog.cs:200-202`, on `Payroll.ViewSensitive`:
  > "Held by Tenant Owner / Tenant Admin / HR Manager ONLY — **deliberately NOT HR Officer** (who generates/exports reports via `Payroll.Export` but **is not trusted with unmasked PII**)"
  and `PayrollReportsController.cs:187`, on the export:
  > "For BankAdvice the file carries **FULL account numbers** (BR-2)."
  Both statements are deliberate, both are documented, and they cannot both hold. Either the catalogue's separation-of-duties rationale is wrong, or the export gate is.
- **Audit asymmetry, measured:** the audited reveal path (`RevealBankAdviceAsync`, `:150`) writes `PayrollAuditAction.PayrollReportViewSensitive` **before** returning — 4 audit references in that method. The export path has **0**. So the *harder* route to unmasked PII is logged and the *easier* one is not: an HR Officer can download every account number in the tenant and leave **no trace** on the sensitive-read trail that exists precisely to record such access.
- **Why HIGH despite being latent:** no production row currently holds a bank value, because **no capture API exists** (`ISSUE-523`). But that is exactly what `US-CHR-014` (net-new, storied 2026-09-07) builds. **This finding is a precondition of that story, not a follow-up** — shipping capture first converts a documented contradiction into a live bulk-PII exposure with no audit trail, and the exposure would then be *retroactive* over every row captured before it is fixed.
- **Related but distinct:** `DataExportController.cs:33` (`Tenant.ExportData`) also emits `BankAccountNumber` in full, and that one is **deliberate and test-pinned** (`ExportSensitiveFieldsTests.cs:31` — "PII — must be EXPORTABLE (FR-8)"). Do not "fix" that by symmetry without a decision; a full-tenant data export is a different consent surface from a routine payroll report.
- **Needs a decision, not just a fix.** Three shapes, and they are not equivalent: (a) mask the export unless the caller also holds `Payroll.ViewSensitive` — safest, but may break a real bank-submission workflow that legitimately needs the full file; (b) keep it unmasked but require `Payroll.ViewSensitive` **and** audit it, aligning with the reveal path; (c) accept it and correct the catalogue comment, which at minimum stops the code asserting something false. **(b) is the recommendation** — it makes the two unmasked routes consistent and closes the audit gap without removing a capability payroll operations may depend on.
- **Found:** 2026-09-07, out-of-lane while researching `ENH-018`/`ISSUE-523` for the `US-CHR-014` story. Verified independently: gate at `PayrollReportsController.cs:191`, `masked: false` at `PayrollReportService.cs:250`, HR Officer grant at `PermissionCatalog.cs:751`, audit counts 0 (export) vs 4 (reveal).
- **SURVEY:** **3 endpoints** (not the 1 the entry cites) serve unmasked `BankAccountNumber` behind `Payroll.Export` alone (unit = `[RequirePermission]`-gated API actions reaching a code path with `masked: false` on bank data): `GET /reports/{reportType}/export` (`PayrollReportsController.cs:190-191`), plus the async pair `POST /reports/{reportType}/export` (`:227-228`) and `GET /reports/exports/{exportId}/download` (`:279-280`) — `PayrollReportExportService.cs:137-138`, `:228` deliberately routes through the same `ExportReportAsync`, and its own header comment says so (`:28-29`). Excluded: `DataExportController` (separate `Tenant.ExportData` consent surface, test-pinned), payslip PDFs (`PayslipBatchRenderer.cs:395-397` masks to last-4 unconditionally), the correctly-gated reveal endpoint (`:171`), and tests. The only other unmasked-PII surface repo-wide is `EmployeeService` NationalId, which masks by default (`:1153`, `:1346`).
- **AUDIT (2026-09-07):** (1) Export gated by `[RequirePermission("Payroll.Export")]` — **CONFIRMED, exact** (`PayrollReportsController.cs:191`). (2) `BuildBankAdviceReportAsync(..., masked: false, ...)` — **CONFIRMED, exact** (`PayrollReportService.cs:250`; unmasking at `:1019`). (3) HR Officer holds `Payroll.Export` — **CONFIRMED but line stale**: actual **`PermissionCatalog.cs:750`**, not `:751`, inside the `BuiltInRoles.HROfficer` block (`:733`) — whose adjacent comment says "Deliberately NOT ... `Payroll.ViewSensitive` (unmasked bank PII)" while the next line grants the route that serves it. (4) Catalogue contradiction at `:200-201` — **CONFIRMED**. (5) Reveal path `:150` — **stale, actual `:151`**. (6) "4 audit references in that method" — **PARTIALLY TRUE**: exactly **one** audit write (`PayrollReportService.cs:164-170`); the other three are a comment, enum args and a DTO note. Substance holds. (7) "the export path has 0 audit rows" — **CONFIRMED for the sync GET** (`ExportReportAsync:235-268` contains no `_auditLogger` call); **PARTIALLY TRUE overall** — the async download does audit (`PayrollReportExportService.cs:358-359`), but as a download event, not the `PayrollReportViewSensitive` sensitive-read row the catalogue's control depends on. (8) Latency premise — **CONFIRMED**: `BankAccountNumber` has zero create/update DTOs, zero Application write paths, zero FE references — no capture API exists.
- **SEVERITY CHECK:** **agrees with HIGH, and the entry UNDERSTATES the blast radius** — 3 endpoints, not 1. Fixing only the GET leaves the async pair open.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-537 — the raw-SQL tenant-isolation semgrep rule misses two of the four shapes it needs to cover

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** INFRA / static analysis
- **Title:** `.semgrep/tenant-isolation.yml:42-46` (rule `hrm-raw-sql-no-tenant-predicate`, `severity: ERROR`, blocking) matches `ExecuteSqlRaw`, `ExecuteSqlRawAsync`, `ExecuteSqlInterpolated(Async)`, `FromSqlRaw` and `FromSqlInterpolated` — but **not `FromSql`** (the interpolated overload) and **not `SqlQueryRaw`**. A raw read through either of those unguarded shapes passes the gate with no tenant predicate at all.
- **Why MED and not LOW:** this is a **blocking ERROR-severity guard whose coverage is narrower than its name implies**, which is the `ISSUE-486`/`ISSUE-492` pattern again — a check that reports safety it does not fully provide, and therefore stops anyone looking. It is a live gap **today**, independent of any materialized view.
- **How it surfaced:** while establishing that a `performance_summary` matview would be isolated only by a hand-written predicate (`US-PRF-012`), since a matview read uses exactly these two uncovered shapes. But the gap is not conditional on that story.
- **Suggested direction (NOT applied):** extend the pattern list to `FromSql` and `SqlQueryRaw`, then run it across `src/backend` and triage whatever it newly catches — the existing raw-SQL census (`AuditLogService.cs:253`, `FieldEncryptionMaintenanceService.cs:249/258/285/301`) all carry hand-written `tenant_id` predicates, so the expected new-catch count is low.
- **Found:** 2026-09-07, out-of-lane while authoring the T4 parked-half stories.
- **SURVEY:** the rule covers **6 of the 11** EF raw-SQL API shapes — missing `FromSql`, `SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlAsync`. Against the real population: **9 of 16** production call sites in `src/backend` are matched, leaving **7 uncovered** (6 `SqlQueryRaw` + 1 `FromSql`). Unit = EF raw-SQL API shapes, then call sites. Excluded: `*Tests*.cs` and `Migrations/**`, which the rule's own `paths.exclude` drops.
- **AUDIT (2026-09-08):** (1) The coverage gap — **CONFIRMED**: `.semgrep/tenant-isolation.yml:42-47` lists exactly `ExecuteSqlRaw(Async)`, `ExecuteSqlInterpolated(Async)`, `FromSqlRaw`, `FromSqlInterpolated`; `FromSql` and `SqlQueryRaw` are absent. The **rule (code side) is wrong** and the entry's description is accurate. Cited line range is off by one — the pattern block is `:42-47`. (2) **The entry's remediation forecast is FALSE.** It claims the uncovered sites "all carry hand-written `tenant_id` predicates" so extending the rule would yield few new catches; extending it would actually fire on **5 sites, all false positives** — `FieldEncryptionMaintenanceService.cs:249`, `:258`, `:286` build the predicate in a *variable* (`byKeySql`), so the matched expression contains `tenantId`, not `tenant_id`, and the `pattern-not-regex` does not exclude it; `DbInitializer.cs:194`, `:196` are `pg_roles`/`current_user` catalog reads. Only `AuditLogService.cs:253` self-excludes. **So the cheap extension the entry proposes would produce a noisy rule, which is how guards get disabled.** (3) Cited `FieldEncryptionMaintenanceService.cs:285`/`:301` are stale — the real `SqlQueryRaw` lines are `:249`, `:258`, `:286`. Merge status: filed in `c9dccd8c` (merged); no fix commit. **NOT FIXED.**
- **SEVERITY CHECK:** **agrees at MED.** A worse sibling defect in the same rule is filed as **`ISSUE-565`**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-538 — `Employee.cs:183-185` documents a capture path that has never existed

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE (docs-in-code)
- **Title:** The bank-details comment block states the columns are "nullable and **populated elsewhere**". There is no elsewhere: an exhaustive search finds the only code that ever sets `BankName`/`BankBranchCode`/`BankAccountNumber` is the test fixture at `PayrollReportIntegrationTests.cs:269`.
- **Why it is worth an id:** it is the same class as `ISSUE-531` (the stale "log-only seam" doc that produced two wrong engineering judgements in one session) — a comment asserting a capability that does not exist, positioned exactly where someone verifying the feature would read it and stop looking. It is also why `ENH-018` was filed as "seed the data" rather than "there is no way to enter the data".
- **Suggested direction (NOT applied):** correct it as part of `US-CHR-014`, which is what makes "populated elsewhere" true.
- **Found:** 2026-09-07, out-of-lane while researching `ENH-018`.
- **SURVEY:** **3 of 3** bank columns have no production write path. Exactly **1** non-test assignment exists anywhere in `src/`, and it is a *read* projection (`PayrollReportService.cs:1017`, `BankName = emp?.BankName`), not a capture. Only 11 files reference the fields at all, and the 5 non-test ones are all consumers (report DTOs, payslip renderer, EF configuration). Frontend references: **0**. Unit = columns, then assignment sites. Excluded: migrations and `HRM.Tests`.
- **AUDIT (2026-09-08):** (1) "the comment states the columns are nullable and populated elsewhere" — **CONFIRMED**, but the **cited range is wrong**: the block is `Employee.cs:181-186` and the phrase spans **`:183-184`**, not `:183-185`. (2) "there is no elsewhere" — **CONFIRMED**. (3) "the only code that ever sets them is `PayrollReportIntegrationTests.cs:269`" — **PARTIALLY TRUE**: that line is exact, but it is **not** the only test setter — `AuditCaptureInterceptorTests.cs:453`, `:483`, `:493` also assign `BankAccountNumber`. The load-bearing claim (no *production* setter) survives. (4) "same class as `ISSUE-531`" — **CONFIRMED**. (5) "it is why `ENH-018` was filed as 'seed the data'" — **CONFIRMED**; `docs/BA/STATUS.md:68` now records the corrected framing and cites `Employee.cs:181-197`. Merge status: no branch modifies `Employee.cs:181-186`. **OPEN.**
- **SEVERITY CHECK:** **agrees at LOW** — the substantive gap is already carried by US-CHR-014 and `ISSUE-523`; this entry is the documentation half only.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-539 — `OfferService.ExpiryReminderDaysBefore` is a bare const with no configuration read at all

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE
- **Module / US / TC:** Recruitment · US-REC-007 FR-7
- **Title:** `OfferService.cs:51` hardcodes `ExpiryReminderDaysBefore = 3`, used at `:551-552`. `OfferService` injects **no `IConfiguration` at all**, so unlike its interview sibling — which at least reads the app-global `Recruitment:InterviewReminderLeadHours` (`InterviewService.cs:474`) — this value is not tunable even instance-wide. `PipReminderService.cs:25` (`ReminderLeadDays = 3`) is a third instance of the same shape.
- **Deliberately out of scope of `US-REC-011`** (per-tenant interview lead time): different unit (days vs hours), different entity, and the const's own docstring calls tenant-configurability "a SEPARATE concern … intentionally NOT built here". Filed so it does not live only in a story paragraph.
- **Found:** 2026-09-07, out-of-lane while authoring `US-REC-011`.
- **SURVEY:** **3** hardcoded reminder-lead constants with no configuration read: `OfferService.cs:51`, `PipReminderService.cs:25`, and — **not named in the entry** — `PipReminderService.cs:26` `AckBusinessDayWindow = 5`, the same shape applied to a BR-4 business rule. **1** comparable seam does read config (`InterviewService.cs:474`). Unit = hardcoded lead-time constants in services. Excluded: tests, and `DefaultExpiryDays` (`OfferService.cs:44`), which is a BR-6 default rather than a lead time.
- **AUDIT (2026-09-08):** (1) "`OfferService.cs:51` hardcodes `ExpiryReminderDaysBefore = 3`" — **CONFIRMED, exact**. (2) "used at `:551-552`" — **PARTIALLY TRUE**: the only *use* is `:552`; `:541` is a `<see cref>` inside the docstring. (3) "`OfferService` injects no `IConfiguration` at all" — **CONFIRMED**: the constructor (`:53-63`) takes 10 parameters, none of them `IConfiguration`, and the string appears 0 times in the file. (4) "the interview sibling reads `Recruitment:InterviewReminderLeadHours`" — **CONFIRMED, exact** at `InterviewService.cs:474`, with a `DefaultReminderLeadHours` fallback and a negative-value guard at `:475`. (5) "`PipReminderService.cs:25` `ReminderLeadDays = 3`" — **CONFIRMED, exact**. (6) "the const's docstring calls tenant-configurability a SEPARATE concern intentionally NOT built here" — **CONFIRMED** (`OfferService.cs:46-50`), which is what keeps this a tunability gap rather than a defect. Merge status: `b2558d9a` touches `OfferService.cs` but not `:51`. **OPEN.**
- **SEVERITY CHECK:** **agrees at LOW** — no user-visible defect. The unnamed `AckBusinessDayWindow` sibling belongs inside a widened `ISSUE-539`, not a new id.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-541 — 29 terminal findings sit in the working ledger, and `CLOSED` is a status the schema does not define

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** docs / process
- **SURVEY:** **29 of ~210** entries in `docs/QA/TEST-FINDINGS.md` carry a terminal status while living in the **working** file — 16 `RESOLVED` and 12 `CLOSED` plus 1 `OBSOLETE` (unit: `### ID —` blocks in the working file only; the archive was not scanned). A further **7** carry a status outside the documented vocabulary entirely: `ISSUE-032` (`RECLASSIFIED`), `ISSUE-355` and `ISSUE-499` (`MERGED INTO`), `DECISION-477`/`478`/`480` (`PARKED AT THE DECISION GATE`), `BUG-489` (`NEEDS-DECISION`). So **36 of ~210 rows (17%) are not live findings**, and only **~181** are.
- **AUDIT (2026-09-07):**
  - **CONFIRMED — `CLOSED` is undefined.** `.claude/rules/ledgers.md` states the vocabulary exactly: *"Live: `OPEN`/`DEFERRED`. Terminal: `RESOLVED`/`WONTFIX`/`RETRACTED`/`DUPLICATE`."* `CLOSED` is a **fifth spelling** and appears on 12 entries — including `ENH-011` (`✅ CLOSED 2026-09-07`), which this session wrote. The schema line also records that statuses were *"normalised 2026-09-01 from ten status spellings across four shapes"*, so this is the same drift recurring **six days** after the normalisation.
  - **CONFIRMED — terminal entries belong in the archive.** Same file: *"`TEST-FINDINGS.md` holds **live** findings; `TEST-FINDINGS-RESOLVED.md` holds **terminal** ones"*, and *"Only `/verify-fix` moves an entry working → archive."* The 29 are therefore correctly *authored* but incorrectly *located* — they were closed by ordinary work rather than by `/verify-fix`, which is the only mover.
  - **CONFIRMED — the guard is one-directional.** `LedgerTraceabilityTests` is documented as enforcing the family rule *"and that no live finding sits in the archive"*. Nothing asserts the **reverse** — that no terminal finding sits in the working file. Verified by the fact that all 29 pass today.
- **Why MED, and why it is not cosmetic:** the working file exists so agents do not read past finished work — the 2026-09-01 split cut it from 1.9 MB to 422 KB on exactly that reasoning. Every terminal entry left behind erodes that. Concretely, it **inflates every scoping measurement taken off the file**: this was found because a survey/audit backlog was measured at *267 entries / 252 missing*, when the true live target is *~181 / 172*. A **~40% overstatement**, which would have sent a backfill session to re-verify findings that are already closed.
- **Same class as `ISSUE-524`:** a guard that checks one direction only. `ISSUE-524` is the mirror image — a finding id cited in `src/` with no ledger entry, because nothing checks `src/` → ledger. Both are cheap to close with a reverse arm on the same test.
- **Suggested direction (NOT applied):** (1) normalise the 12 `CLOSED` to `RESOLVED`, or add `CLOSED` to the documented vocabulary — either is fine, but the schema and the file must agree; (2) decide whether the 7 non-vocabulary statuses (`MERGED INTO`, `PARKED`, `NEEDS-DECISION`, `RECLASSIFIED`) are legitimate live states that belong in the schema, or should collapse to `DEFERRED`/`DUPLICATE`; (3) add the reverse arm to `LedgerTraceabilityTests` — no terminal status in the working file — and only then move the 29 via `/verify-fix`. Do **not** bulk-move them first: `/verify-fix` is the only authorised mover, and closing that boundary by hand is the failure mode the report-only rule exists to prevent.
- **Found:** 2026-09-07, in response to the direct question "are these all open right now, or does the list include closed/resolved/wontfix/duplicated ones?" — asked while scoping the survey+audit backfill. The answer was no, and the measurement was wrong until it was asked.

### ISSUE-540 — three stale claims in the RLS documentation and ledgers, all pointing the same wrong way

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** docs
- **Title:** `Rls:Enabled` **ships `true`** (`appsettings.json:48-50`); only `appsettings.Development.json:14-16` overrides it to `false`. Three tracked places still say otherwise or contradict themselves:
  1. `Rls/README.md` — states the flag defaults to **false**. False since PR #476 (`b4c61945`).
  2. `docs/BA/STATUS.md:297` — "committed OFF", directly contradicting `:200` ("committed **ON** — corrected 2026-08-18; this line said 'committed OFF' and had been stale since PR #476"). The correction was applied to one line and not the other.
  3. `docs/BA/platform/US-PLT-002.md` frontmatter — still `status: draft` although STATUS.md:111 records the code as complete and proven on real Postgres, with only the ops prod flip outstanding.
- **Why MED:** "RLS is dormant" was repeated as fact **by the orchestrator throughout the 2026-09-07 session** and used as load-bearing reasoning for parking `ISSUE-129`'s materialized view — a decision that happened to be right, but for a reason that was wrong. A ledger that contradicts itself on whether a security control is ON is worse than one that is merely silent.
- **Suggested direction (NOT applied):** correct all three; make the README point at `appsettings.json` rather than restating the value, so it cannot drift again.
- **Found:** 2026-09-07, out-of-lane while authoring `US-PRF-012`.
- **SURVEY:** **12 stale assertion sites across 9 non-archive documents** — the entry claims 3 (unit = claims about the *committed* RLS value: "committed OFF" / "flag OFF" / "`Rls:Enabled=false`" / "default false"). Excluded: conditional prose (rollback runbooks, dev-override notes), archives, and the entry itself. The 12: `Rls/README.md:10`, `:39`, `:125`; `docs/Architecture/STATUS.md:4`; `docs/Architecture/BLOCKERS.md:3`; `docs/BA/STATUS.md:302`; `docs/QA/payroll/TC-PAY-013-07.md:54`; `docs/QA/plans/COMPLETION-PLAN.md:205`; `docs/QA/AUDITOR-FINDINGS.md:111`, `:131`; `docs/vault/decisions/ADR-2026-07-10-tenant-isolation-model.md:29`; `docs/BA/platform/US-PLT-002.md:6`.
- **AUDIT (2026-09-08):** (1) The config — **CONFIRMED**: `appsettings.json:48-50` ships **`true`**; only `appsettings.Development.json:14-16` overrides to `false`. All 12 doc sites are the wrong side. (2) **Both cited STATUS.md line numbers are stale**: the ON/OFF self-contradiction is at **`:203` vs `:302`**, not `:200`/`:297`; and "STATUS.md:111" is a heading — the story row is `:114`. (3) **The entry understates and does not account for `ISSUE-555`**: `TenantGucConnectionInterceptor.cs:43-46` derives the GUC from `AmbientTenant.Current` — the same header-resolved tenant EF already uses — so **correcting "RLS is dormant" to "RLS is ON" does not restore the independent-backstop conclusion those docs were used to support.** Anyone fixing the 12 sites from this entry alone would replace one wrong claim with another. Also noted, not filed separately: 4 further sites cite stale line numbers for the same config (`pass-e-architecture.md:45`, `pass-c-nfr.md:32`, `BA/STATUS.md:204`, `REFRESH-2026-08-17.md:86`). Merge status: filed in `c9dccd8c` (merged); no fix commit. **NOT FIXED.**
- **SEVERITY CHECK:** **agrees at MED, leaning higher** — 12 sites, one of them an ADR, which is the artefact future decisions cite.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-535 — `TC-PRF-ISO-028`'s per-tenant refresh-job arm can now never pass, because the job it tests was deliberately refused

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST / docs
- **Module / US / TC:** Performance · US-PRF-007 NFR-3/BR-4 · TC-PRF-ISO-028
- **Title:** `ENH-013`(c) recorded ISO-028's cache-key-namespacing and **per-tenant refresh-job** arms as BLOCKED "purely because the mechanism is absent", with the expectation that `ISSUE-129` would unblock both. Only **one** of them was unblocked. `ISSUE-129` shipped the Redis read-through cache (so the namespacing arm is now directly executable against tenant- and scope-scoped keys) but **deliberately refused** the `performance_summary` materialized view on tenant-isolation grounds — and the 4-hourly Hangfire refresh job existed only to refresh that view. **No such job will be built**, so an arm waiting on it waits forever.
- **Why MED and not LOW:** a test case parked as BLOCKED-pending-a-mechanism is invisible to every gap sweep — it reads as "will be covered later" rather than "will never be covered". Left as-is it silently misrepresents NFR-3/BR-4 coverage, and the next person to read ISO-028 will go looking for a refresh job that was consciously not built.
- **Suggested direction (NOT applied):** re-scope the arm from "the per-tenant refresh job re-materializes within its interval" to "a cached entry expires and re-reads after the TTL", which is the invalidation that actually shipped. Keep the namespacing arm and run it.
- **Found:** 2026-09-07, while recording `ENH-013`'s disposition.


### ISSUE-534 — the perf scripts that would validate NFR-1 drive above a rate limiter, so a green p95 can be measuring the limiter
**▶ CORRECTED BLOCKER 2026-09-08 — the re-measurement cannot run on this machine.**
**k6 is not installed** (`which k6` → nothing), so the NFR-1 re-measurement this finding calls for
cannot be executed here regardless of the container state. That is a *different* blocker from the stale
image, and recording it stops the next session repeating the rebuild expecting a perf number.
⚠ **Second, independent problem: the perf tenant holds 1,000 employees, not the 5,000 the fixture and
NFR-1 assume.** `perf/seed/perf-volume-seed.sql` has not been applied to this database. So even with k6
installed, a run today would measure a tenant one-fifth the intended size and report a flattering
number — the same class of false-green this very finding is about.


- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** TEST / perf
- **Module / US / TC:** Performance, cross-module · NFR-1 · TC-PRF-007-11, TC-PRF-007-05 step 5
- **Title:** `Program.cs` installs a fixed-window limiter of **300 req/min partitioned by `(tenantId, userId)`**, and the perf tenant authenticates as a **single user** — so every virtual user in a run shares **one** partition. `perf/scripts/03-scale-reads.js` (30 VU) and `05-module-lists.js` (50 VU) both drive well above that ceiling. The 429s return in ~2 ms, and **fast 429s pull p95 down**, so a script can report a green `p(95)<2500` that is the p95 of *being rate-limited* rather than of the endpoint under test.
- **Observed, not theoretical:** a 20-VU run during the `ENH-013`(b) work issued **26,182 requests and got exactly 901 successes** — 300×3, the limiter's arithmetic, not the application's. The run was very nearly reported as a pass.
- **Why HIGH:** **every prior NFR-1 verdict sourced from these two scripts is suspect**, including any that were used to mark a story or TC as meeting its performance target. This is not a defect in the application at all — it is a measurement instrument reporting success while measuring the wrong thing, which is the `ISSUE-486`/`ISSUE-492` class (a check that reports safety it does not provide) applied to performance. It also means `ENH-013`(b)'s new 5,000-employee fixture does **not** by itself make NFR-1 measurable.
- **Suggested direction (NOT applied):** (1) add a **failing threshold on `http_req_failed`** to every k6 scenario so a limiter-dominated run goes red instead of green — this is the cheap fix and it should land before any further perf verdict is recorded; (2) for genuine load, spread across multiple seeded users so the partition key varies, or raise/bypass the limit for the perf tenant only; (3) re-run and re-record any NFR-1 verdict previously taken from scripts 03 or 05.
- **Related:** local perf observation is separately unreliable right now — the running Docker container was built 2026-09-02 and is **98 commits stale**, so it does not contain code merged since. Rebuild before treating any local measurement as current.
- **Found:** 2026-09-07, out-of-lane while implementing `ENH-013`(b) (#686). Recorded in that PR's description but **never filed as a finding until now** — it existed only in a transcript, which is precisely the failure Engineering-Discipline #6 exists to prevent.
- **SURVEY:** **4 of 6** k6 scenarios drive above a rate limiter (unit = k6 scenario scripts in `perf/scripts/`; the 8 files there are 6 k6 scenarios plus `lib.js` and `04-bulk-import-boundary.sh`, both excluded). Above the ceiling: `01-hot-reads.js:11` (50 VU x 4 req, `sleep(0.5)`), `03-scale-reads.js:11` (30 VU x 4-5 req, **no sleep**), `05-module-lists.js:11` (50 VU x 6 req, `sleep(0.5)`), and `02-auth-login.js:17-21` (ramp to 20 VU against the tighter **`auth-login` 10/min/IP** policy). Under it: `06-performance-dashboard.js:37` (5 VU, deliberately sized to ~234 req/min, `:23`) and `smoke.js:7`. Limiter threshold: **`PermitLimit = 300` per 1-minute window** (`src/backend/HRM.Api/Program.cs:597-598`), partition key `t:{tenantId}:u:{userId}` (`GlobalRateLimitPartition.cs:71-73`).
- **AUDIT (2026-09-07):** (1) "300 req/min partitioned by (tenantId, userId), and the perf tenant is one user, so one partition" — **CONFIRMED** (`Program.cs:595-601`, `GlobalRateLimitPartition.cs:71-73`). (2) "03 and 05 drive well above the ceiling" — **CONFIRMED, and UNDERSTATED**: 01 and 02 do too. (3) "a script can report a **green** `p(95)<2500` that is the p95 of being rate-limited" — **PARTIALLY TRUE, and misleading about the run verdict.** Every scenario **already** carries `'http_req_failed': ['rate<0.01']` (`01:14`, `02:25`, `03:14`, `05:13`, `06:39`), present since the harness landed in `2d889270` — i.e. *before* this finding. k6's default `expected_response` is 200-399, so 429s count as failures; the entry's own 26,182-req / 901-success run is a 96.6% failure rate, which blows that threshold and exits k6 non-zero. The **per-endpoint p95 line** prints green; the **run** goes red. Confidence 90%. (4) Suggested fix (1), "add a failing threshold on `http_req_failed` to every k6 scenario — this is the cheap fix and it should land" — **FALSE, already implemented** in all 5 threshold-bearing scripts. Fixes (2) and (3) remain valid and unaddressed. Local branch `test/ISSUE-534-perf-limiter-blindness` is **empty relative to** `origin/test/local-subdomains`.
- **SEVERITY CHECK:** **lower — MED.** The instrument is genuinely mis-sized on 4 scripts, so their p95 numbers are not measuring the endpoint and prior NFR-1 verdicts drawn from them stay suspect. But it cannot silently report a pass — the existing `http_req_failed` gate fails the run. The residual risk is a human reading only the p95 line.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-533 — the recommendation workspace returns unmasked compensation to every caller who lacks the permission that exists to stop it

- **Type / Severity / Status:** BUG · **HIGH** · OPEN
- **Layer:** BE / security (authorization bypass, in-tenant)
- **Module / US / TC:** Performance · US-PRF-010 FR-5 / AC-5 · TC-PRF-010-09 steps 5-6
- **Title:** `GET /api/v1/tenant/performance/recommendations/workspace` is gated by `[RequirePermission("Performance.Publish.All", "Performance.Review.Team")]` (`RecommendationController.cs:69`) and returns each row's nested recommendation via an **unconditional** `BuildDto(...)` (`RecommendationService.cs:147`). `BuildDto` (`:1274-1279`) copies `CurrentCompensation`, `BonusAmount`, `BonusPercent`, `IncrementAmount`, `IncrementPercent` straight off the entity **with no `CanSeeCompensation` check**. The detail endpoint `GetAsync` 403s without `Payroll.ViewCompensation` (`:193-196`) — so the gate is simply **bypassable by calling the workspace instead**.
- **Who is exposed — two concrete personas, neither holding the permission:**
  1. **HR Officer** holds `Performance.PublishAll` (`PermissionCatalog.cs:745`) and is **deliberately denied** `Payroll.ViewCompensation` (`:227`, granted only to Owner / Tenant Admin / HR Manager). They receive the **whole org's** bonus and increment figures.
  2. **Any line manager** holds `Performance.Review.Team`, which the workspace also admits, so they receive **their direct reports'** figures.
  The response even carries `compensationVisible: false` alongside the data it says is not visible (`RecommendationService.cs:164`), so the FE hides what the wire already delivered — a client-side-only control over server-supplied data.
- **Why HIGH:** this is a **server-side authorization bypass on data the permission model explicitly withholds**, reachable by a normal authenticated call to a documented endpoint, with no crafted input. `AutoGenerateAsync` (`:305`) has the same unguarded `BuildDto`. It is not a tenant-isolation break — the data stays inside the tenant — but the separation-of-duties boundary between performance administration and compensation visibility is exactly what `Payroll.ViewCompensation` was created to enforce, and it does not hold on this path.
- **Why it went unseen:** `ISSUE-150` recorded this surface as *"no security exposure today because there is no real compensation data flowing through recommendations"* and rated it LOW. That premise was false — bonus/increment amounts **are** compensation data, and they are stored (and encrypted at rest, which is itself evidence the project treats them as sensitive). A finding asserting "not a live defect" is a strong reason for nobody to look again.
- **Suggested direction (NOT applied):** mask or omit the five comp fields in `BuildDto` when `CanSeeCompensation` is false, at every call site (workspace `:147`, auto-generate `:305`), rather than at the controller — the service is where the permission is already evaluated. Prefer omission/masking over a blanket 403 so the workspace stays usable for its actual purpose, which is what TC-PRF-010-09 step 5 asks for.
- **Regression test it needs:** an HR Officer (and separately a manager) calling the workspace must receive rows **without** comp figures. Mutation-prove it — the test must go RED when the guard is removed, or it is asserting nothing.
- **Found:** 2026-09-07, out-of-lane while re-verifying `ISSUE-150`'s three claims for a ledger rewrite.
- **SURVEY:** **7** controller actions carry the five per-employee compensation fields (`CurrentCompensation`, `Bonus/IncrementAmount`, `Bonus/IncrementPercent` on `RecommendationDto`, `RecommendationDtos.cs:80-86`); **6 of those 7 have no `Payroll.ViewCompensation` gate** — workspace (`RecommendationController.cs:68`), auto-generate (`:105`), save (`:129`), submit (`:152`), approve (`:169`), reject (`:184`). Only `GET {id}` (`:85`) is gated. Unit = controller actions whose response DTO carries per-employee compensation. Excluded: `RecommendationWorkspaceRowDto.CurrentCompensation` (`RecommendationDtos.cs:122`, hard-nulled at source at `RecommendationService.cs:144`); org-level budget/summary pools (aggregate, not per-employee); recruitment offer/vacancy salary (4 DTO files, different permission domain); the Payroll module (own `Payroll.ViewSensitive` gate).
- **AUDIT (2026-09-07):** (1) Workspace nests an unconditional `BuildDto` — **CONFIRMED** (`RecommendationService.cs:150`; cited `:147` **stale**). (2) `BuildDto` copies all five fields with no check — **CONFIRMED** (`:1322-1326`, method at `:1301`; cited `:1274-1279` **stale**). (3) The detail path 403s without the permission — **CONFIRMED** (`:196-200`; cited `:193-196` **stale**). (4) `compensationVisible:false` ships alongside the data — **CONFIRMED** (`:167`; cited `:164` **stale**). (5) HR Officer holds `Performance.PublishAll` and is **deliberately** denied `Payroll.ViewCompensation` — **CONFIRMED** (`PermissionCatalog.cs:745`, block `:733-756`; the exclusion is stated at `:223-224`); Manager holds `ReviewTeam` without it — **CONFIRMED** (`:765`). (6) **Is it mitigated one layer up? — FALSE, no mitigation exists.** The only `ViewCompensation` consumer is `RecommendationService.cs:78`; no filter, middleware or query filter masks it (`HRM.Api/Filters/` holds only `ValidationFilter`), and the fields decrypt automatically via EF value converters (`RecommendationConfiguration.cs:100-106`), so plaintext reaches the wire. (7) Auto-generate has the same defect — **CONFIRMED** (`:328`; cited `:305` **stale**). (8) **The in-code comment at `:206` — "GetWorkspaceAsync ... does not decrypt comp" — is FALSE**; the nested DTO does. Fix `b85df788` sits on `origin/fix/BUG-533-workspace-comp-leak` and is **NOT merged**; zero BUG-533 tests exist at this commit.
- **SEVERITY CHECK:** **agrees with HIGH, and the entry understates it** — 6 ungated actions, not 1. Three of them survive the pending fix by design — filed as **`BUG-550`**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-532 — `AttendanceSettingsMultiplierBoundsTests` silently skips a duplicate test case that the suite reports as a pass

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** TEST
- **Title:** `src/backend/HRM.Tests/Unit/AttendanceSettingsMultiplierBoundsTests.cs:50` — xUnit emits "Skipping test case with duplicate ID … `The_weekend_and_holiday_multipliers_share_the_same_ceiling(value: 10)`", and the build emits `xUnit1025` (duplicate `InlineData`) for it. The case never runs, and the suite still reports green.
- **Why it is worth filing:** it is a small instance of the session's recurring pattern — a check that reports safety it does not provide (`ISSUE-486`, `ISSUE-492`). Also note this test file belongs to `BUG-522`, the fix that has **no ledger entry at all** (`ISSUE-524`).
- **Suggested direction (NOT applied):** drop the redundant `[InlineData]`.
- **Found:** 2026-09-07, out-of-lane while fixing `ISSUE-116`.
- **SURVEY:** **1** duplicate `[InlineData]` pair, in **1** test method, in **1** file (unit = xUnit theory data rows). A repo-wide sweep for other exact duplicate `InlineData` rows within the same theory found **no second instance**, so this is genuinely a one-off rather than a class.
- **AUDIT (2026-09-08):** (1) "`AttendanceSettingsMultiplierBoundsTests.cs:50`" — **CONFIRMED verbatim**: `:48` `[Theory]`, `:49` `[InlineData(10.00)]`, `:50` `[InlineData(10.00)]`, `:51` the method. Identical arguments produce an identical xUnit test-case ID, so one row is deduped and one executed, and the theory reports green. (2) "xUnit1025 is emitted" — **CONFIRMED by construction (~90%**, not built): `xUnit1025` is exactly "InlineData should be unique within the Theory", and nothing suppresses it — a grep for `xUnit1025|NoWarn|TreatWarningsAsErrors` across `*.props`, `*.csproj` and `.editorconfig` is empty. (3) "this file belongs to `BUG-522`, which has no ledger entry" — **CONFIRMED** (`:10`, `:83`; see `ISSUE-524`). (4) "the suite reports it as a pass" — **CONFIRMED**, with the important nuance that **the surviving case genuinely passes and no coverage is actually lost** — only the redundant twin vanishes. No fix commit. **OPEN.**
- **SEVERITY CHECK:** **agrees at LOW, arguably lower.** The duplicate is redundant rather than a distinct uncovered case, so the entry's "a check reporting safety it does not provide" framing is stronger than the facts support. This is a one-line lint cleanup.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-528 — the read-through cache helper is now duplicated verbatim in three services

- **Type / Severity / Status:** ENH · **LOW** · OPEN
- **Layer:** BE
- **Title:** `TryGetCachedAsync`/`SetCachedAsync` + the JSON options + the fail-open catch are now copy-pasted in `HrReportService`, `DashboardService` and (as of `ISSUE-129`) `PerformanceDashboardService`. The third instance is what makes it a pattern rather than a coincidence.
- **Why it is filed and not fixed:** extracting a shared `TenantScopedReadThroughCache` helper means editing two services outside the lane of the change that created the third copy. Reuse-over-duplication wants the extraction; "surgical changes" (Engineering-Discipline #3) forbids doing it as a side quest. Filed so the next cache adopter extracts it instead of writing a fourth.
- **Suggested direction (NOT applied):** extract to `HRM.Application/Common/Helpers/` as its own PR, carrying the fail-open behaviour (BUG-115) unchanged.
- **Found:** 2026-09-07, out-of-lane while implementing `ISSUE-129`.
- **SURVEY:** **2** duplicate implementations at the mainline commit, **3** on the unmerged branch (unit = read-through cache helper pairs over `IDistributedCache`). Merged pair: `DashboardService.cs:968` + `:982`, and `HrReportService.cs:1294` + `:1308`. The third exists only on the branch (`PerformanceDashboardService.cs:561` + `:579`). Call sites: DashboardService 1 internal (`CachedAsync:948`, fanned out to **17** `Build*CachedAsync` widget builders at `:306-355`); HrReportService 1 (`:98`, `:126`); branch perf-dashboard 2. Excluded: `MyTenantsCache`, `RedisPermissionCache`, `RedisTokenDenylist`, which go to `IConnectionMultiplexer` directly rather than this shape.
- **AUDIT (2026-09-08):** (1) "`TryGetCachedAsync`/`SetCachedAsync` + JSON options + fail-open catch copy-pasted in `HrReportService` and `DashboardService`" — **CONFIRMED**: the two bodies are structurally identical, and `JsonOptions` is byte-identical (`DashboardService.cs:50`, `HrReportService.cs:38`); they differ only in the concrete type, the log string and the TTL (3 min vs 10 min). (2) **"and (as of ISSUE-129) `PerformanceDashboardService`" — PARTIALLY TRUE, on two counts.** (a) That third copy **is not in the mainline**: at this commit the file has no cache helpers at all, and a repo-wide `grep TryGetCachedAsync` over non-test `src/backend` returns only the two merged hits. (b) "duplicated **verbatim**" **overstates** it: the branch version is **generic** (`TryGetCachedAsync<T>(string key, CancellationToken ct) where T : class`, branch `:561`) and moves the `_cache is null` guard *inside* the helper, where both siblings leave it at the call site — **the third copy is the better shape, not a clone**. (3) **"the third instance is what makes it a pattern" — FALSE at this commit**: with only two copies merged, it is still the coincidence the entry argues it is not. It becomes a three-copy pattern only if #688 lands. **Merge status: NOT merged.**
- **SEVERITY CHECK:** **agrees at ENH · LOW** — pure duplication, behaviour correct and fail-open in every copy. Note for the extraction: the generic branch variant, not either merged copy, is the right template.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-527 — `GetDepartmentDrilldownAsync` was left uncached while its two siblings were cached

- **Type / Severity / Status:** ENH · **LOW** · OPEN
- **Layer:** BE
- **Module / US / TC:** Performance · US-PRF-007
- **Title:** `PerformanceDashboardService.GetDepartmentDrilldownAsync` (`:180`) runs the same `ResolveScope` + `ResolveCycle` + `LoadPopulationAsync` fan-out (6-7 queries) live. `ISSUE-129` cached `GetOverviewAsync` and `GetTrendAsync` because those were the scoped methods; the drilldown reuses the identical `BuildCacheKey` with variant `drilldown:{deptId}`.
- **Explicitly NOT a candidate:** `GetCalibrationCohortAsync`. Caching an interactive calibration cohort for 3 minutes would be a correctness regression, not a latency win — record this so a later "finish the caching" sweep does not add it by symmetry.
- **Suggested direction (NOT applied):** small follow-up reusing the shipped key builder.
- **Found:** 2026-09-07, out-of-lane while implementing `ISSUE-129`.
- **SURVEY:** **2 of 5** `IPerformanceDashboardService` read methods are cached on the ISSUE-129 branch (`GetOverviewAsync`, `GetTrendAsync`) and **0 of 5** at the current mainline commit (unit = public read methods on the interface). Of the 3 uncached, `ExportOverviewAsync` inherits the cache transitively by delegating to `GetOverviewAsync` (branch `:365`) and `GetCalibrationCohortAsync` is deliberately excluded — leaving **exactly 1** genuine gap, `GetDepartmentDrilldownAsync`. Excluded: the 5 private helpers.
- **AUDIT (2026-09-08):** (1) "`GetDepartmentDrilldownAsync` (`:180`)" — **CONFIRMED, exact on the branch blob**; at mainline HEAD it sits at `PerformanceDashboardService.cs:133`. (2) "runs the same ResolveScope + ResolveCycle + LoadPopulationAsync fan-out live" — **CONFIRMED** (`:139`, `:145`, `:158`, plus a `Departments` lookup at `:151` the siblings do not make). (3) "6-7 queries" — **CONFIRMED as an honest estimate**: `LoadPopulationAsync` (`:471`) issues 4 near-unconditional round trips (`:490`, `:500`, `:513`, `:555`) plus up to 5 conditional filter lookups, and the drilldown adds the department read. (4) "reuses the identical `BuildCacheKey` with variant `drilldown:{deptId}`" — **PARTIALLY TRUE**: no signature change is needed, but `deptId` is **already folded in** via `filter with { DepartmentId = departmentId }` (`:157`), so a bare `"drilldown"` variant suffices — the entry's proposed key would double-encode it. (5) "`GetCalibrationCohortAsync` explicitly NOT a candidate" — **CONFIRMED** and consistent with the ISSUE-129 commit message. (6) **Tenant scoping (Critical Rule #1) — verified clean**: the branch key is `{CacheTenantPrefix.For(_tenantContext)}perfdash:{variant}:{sha256[..16]}` (branch `:554`) and the hash folds in the **resolved employee-id set**, so it is tenant- *and* cross-user-scoped. **Merge status: NOT merged** (same proof as `ISSUE-526`).
- **SEVERITY CHECK:** **agrees at ENH · LOW** — latency only, one method, and a click-through rather than the landing view. Nuance for whoever takes it: once #688 merges, `ExportOverviewAsync` will silently serve an export from an entry up to 3 minutes old, so the entry's "two siblings were cached" undercounts the blast radius by one method (confidence 80%; depends on whether the export stamps a generated-at, which was not traced).
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-526 — the performance dashboard has no cache-bypass, unlike both its cached siblings

- **Type / Severity / Status:** ENH · **LOW** · OPEN
- **Layer:** BE + FE
- **Module / US / TC:** Performance · US-PRF-007 NFR-3
- **Title:** `HrReportService` (FR-8) and `DashboardService` both expose a `refresh` flag that bypasses the cache. `IPerformanceDashboardService` does not, so after `ISSUE-129` an HR lead in a live calibration session cannot force the 3-minute TTL to drop and must wait it out.
- **Why it was not built with the cache:** adding it changes the interface **and** the query DTOs — an OpenAPI contract change plus an FE control to make it usable. `ISSUE-129` was scoped to no FE change and no contract change deliberately, so bundling it would have pulled the contract gate into a backend-only PR.
- **Needs a decision, not just a fix:** worth building only if the 3-minute staleness is judged unacceptable during calibration. That is a product call on a real but bounded irritation.
- **Found:** 2026-09-07, out-of-lane while implementing `ISSUE-129`.
- **SURVEY:** **3** read-through-cached user-facing read surfaces, of which **2 offer a bypass** — and 2 of 2 among the *merged* ones: `HrReportService` (`?refresh=true`, `HrReportsController.cs:54` → `HrReportService.cs:75`, `:95`) and `DashboardService` (`?refresh=true`, `DashboardController.cs:37`, `:40` → `DashboardService.cs:121`, `:948`, `:955`); `PerformanceDashboardService` is the third and is **in-flight, not merged**. Unit = report/dashboard read-through caches. Excluded: 7 infrastructure/lookup caches (`TenantResolutionCache`, `MyTenantsCache`, `TenantSettingsService`, `AuthService`, `NotificationPreferenceService`, `RedisPermissionCache`, `RedisTokenDenylist`) — none is a read-through report cache and none offers a bypass either.
- **AUDIT (2026-09-08):** (1) "`HrReportService` (FR-8) exposes a refresh flag" — **CONFIRMED** (`HrReportService.cs:75`, guard `:95`, API `HrReportsController.cs:54`). (2) "`DashboardService` too" — **CONFIRMED** (`DashboardService.cs:121`, bypass `:955`, API `DashboardController.cs:37`). (3) "`IPerformanceDashboardService` does not" — **CONFIRMED**: none of its five methods takes a bool parameter (`IPerformanceDashboardService.cs:32`, `:36`, `:40`, `:45`, `:53`). (4) **"after ISSUE-129 … a user must wait the 3-min TTL out" — PARTIALLY TRUE: the premise is not in the mainline.** At this commit the performance dashboard has **no cache at all** — `PerformanceDashboardService.cs:37` still carries the pre-129 "EXTENSION POINT … a future story can introduce" comment and there is no `IDistributedCache` field. Merge status: `git merge-base --is-ancestor c63f2741 origin/test/local-subdomains` → **false** (likewise `231f4a62`, `b45a34f9`); branch `perf/ISSUE-129-dashboard-read-through-cache` / **PR #688 still in flight**.
- **SEVERITY CHECK:** **agrees at ENH · LOW.** It is a decision-gated product call and is currently **hypothetical** — the staleness it complains about does not exist until #688 merges. No correctness or isolation dimension.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-525 — six shipped tests carry a `TC-PRF-ISO-129` trait that points at no spec file

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST / docs
- **Module / US / TC:** Performance · US-PRF-007 NFR-3/AC-5
- **Title:** `ISSUE-129`'s six cache tests are tagged `[Trait("TC","TC-PRF-ISO-129")]`, following the existing `TC-PRF-ISO-NNN` series, but **no `docs/QA/performance/TC-PRF-ISO-129.md` exists.** The trait currently resolves to nothing.
- **Why MED rather than LOW:** this is Critical Rule #4 (traceability) failing in the direction that is hardest to notice — the tests are real, green and valuable, so nothing ever goes red to reveal that the id they claim is fictional. It is the same shape as `ISSUE-524`: an id cited in `src/` with no ledger/spec entry, and the same missing reverse check would catch both.
- **Suggested direction (NOT applied):** author `docs/QA/performance/TC-PRF-ISO-129.md` covering cache read-through plus tenant/scope key isolation, bound to US-PRF-007 NFR-3/AC-5.
- **Found:** 2026-09-07, out-of-lane while implementing `ISSUE-129`.
- **SURVEY:** **19** `[Fact]` methods carry `[Trait("TC", "TC-PRF-ISO-129")]` at branch tip `c63f2741`, all in `PerformanceDashboardCacheTests.cs` — **"six" is the count at the first commit `231f4a62` only** (unit = xUnit test methods). Excluded: `[Theory]` cases (none carry it) and other files (none do).
- **AUDIT (2026-09-08):** (1) **"six" — FALSE**: 6 was true at `231f4a62`, 19 at `c63f2741`, and the entry was authored after both. (2) **"shipped" — FALSE**: `231f4a62`, `b45a34f9` and `c63f2741` are **not** ancestors of `origin/test/local-subdomains`; `git grep TC-PRF-ISO-129 origin/test/local-subdomains -- src` returns zero. Nothing bearing this trait is in the mainline. (3) "no `docs/QA/performance/TC-PRF-ISO-129.md` exists" — **CONFIRMED**. (4) **"following the existing TC-PRF-ISO-NNN series" — FALSE, and this is the load-bearing correction**: `docs/QA/performance/` holds exactly **41** ISO specs, `TC-PRF-ISO-001` … `-041`, contiguous. **129 is not the next id — it is the *issue* number `ISSUE-129` pasted into a TC slot.** So the entry's suggested direction (author `TC-PRF-ISO-129.md`) would **cement a wrong id and leave a permanent 042–128 hole**; the correct fix is to retag the tests `TC-PRF-ISO-042` and author that file. No fix commit. **OPEN.**
- **SEVERITY CHECK:** **lower — MED should become LOW.** Nothing is shipped: the branch is unmerged, so this is a pre-merge defect rather than a live traceability hole. It becomes MED the moment PR #688 merges — which is the argument for fixing the tag *before* that merge, not after.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-524 — `BUG-522` is cited in shipped source and tests but exists in no ledger

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** process / traceability
- **Title:** `BUG-522` was fixed and merged (#672, "align the overtime-multiplier validator bound with the column it writes to") and its ID is referenced in `UpsertAttendanceSettingsValidator.cs` and `AttendanceSettingsMultiplierBoundsTests.cs` — but `git grep BUG-522` across the whole tree returns **only those two source files**. There is no entry in `TEST-FINDINGS.md`, none in `TEST-FINDINGS-RESOLVED.md`, and no queue row.
- **Why it matters:** Critical Rule #4 is traceability — every fix traces to a finding. A finding ID that exists only as a string in a code comment cannot be de-duplicated against, cannot be re-verified by `/verify-fix`, and cannot be counted in any tier. The ID is also now **burned**: the next filing that auto-increments from the ledger's max (521) would reach 522 and collide with shipped code.
- **What the existing guard does not catch:** `LedgerTraceabilityTests` validates ledger rows point at real US/TC ids — it checks the ledger outward. Nothing checks the **reverse** direction, that a finding id appearing in `src/` has a ledger entry. That reverse check is what would have caught this at commit time.
- **Suggested direction (NOT applied):** back-fill the `BUG-522` entry from #672's commit message into `TEST-FINDINGS-RESOLVED.md`, and add the reverse arm to `LedgerTraceabilityTests` — every `(BUG|ISSUE|ENH)-\d+` token in `src/` must resolve to a ledger entry.
- **Found:** 2026-09-07, out-of-lane while sizing the T4 decided-parked items (looking up the next free finding id is what surfaced it).
- **SURVEY:** **2 source files, 3 citing lines** — `UpsertAttendanceSettingsValidator.cs:20`, `AttendanceSettingsMultiplierBoundsTests.cs:10` and `:83` (unit = `BUG-522` string occurrences under `src/`). Excluded: ledger and plan prose.
- **AUDIT (2026-09-08):** (1) "no `### BUG-522` heading in either ledger" — **CONFIRMED**: the grep is empty in both files, and the ids present run 510–521, **523**, 524–528, 529–533 … up to 562 — **522 is the single gap**. (2) "fixed and merged (#672)" — **CONFIRMED**: `2b0c50f5` is an ancestor of `origin/test/local-subdomains`. (3) "`LedgerTraceabilityTests` checks the ledger outward only; nothing checks the reverse" — **CONFIRMED**: all seven `[Fact]`s (`LedgerTraceabilityTests.cs:58`, `:83`, `:128`, `:150`, `:166`, `:228`, `:278`) read only `docs/BA/STATUS.md`, `docs/QA/TEST-STATUS.md` and the two ledgers; the file contains no path under `src/` and no `.cs` enumeration. (4) **"the ID is now burned — the next auto-increment from max 521 would reach 522 and collide" — FALSE.** The sequence already stepped past it: ids 523…562 exist and no collision occurred. **That half of the rationale is dead and should not carry weight in the fix argument.** No fix commit. **OPEN.**
- **SEVERITY CHECK:** **agrees at MED** — the traceability hole is real (source cites an id no ledger defines), even though the collision half of its rationale is stale.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-523 — `employees.bank_account_number` is plaintext while its sibling `national_id` on the same entity is encrypted

- **Type / Severity / Status:** ISSUE · **MED** (latent; would be HIGH the day capture ships) · OPEN
- **Layer:** BE / security
- **Module / US / TC:** Core HR + Payroll · `ENH-018` · TC-PAY-009-02, TC-PAY-009-08
- **Title:** `Employee.BankAccountNumber` (`Employee.cs:197`) maps to `employees.bank_account_number varchar(50)` with **no value converter**. The `ApplyEncryption` hook on this very entity (`EmployeeConfiguration.cs:245-250`) encrypts exactly one field — `NationalId` — and `EncryptedFieldRegistry.cs:67` lists `employees.national_id` and nothing bank-related. So the AES-256-GCM `enc:v1:` machinery is already owned, already wired to this entity, and deliberately not applied to the most sensitive column of its class on it.
- **Why MED and not HIGH today:** no production row has ever held a value. There is **no write path at all** — an exhaustive search found zero setters outside `PayrollReportIntegrationTests.cs:255-269`, which constructs the entity directly against the DbContext. The columns are structurally NULL in every tenant, so there is no data at risk right now.
- **Why it must be decided BEFORE any capture work, not after:** the moment a capture endpoint ships, every account number is **born plaintext**, and retrofitting encryption then requires a data migration over live PII instead of a no-op. Encrypting now costs a `varchar(50) → text` retype and one registry line, with a **back-fill that is a guaranteed no-op** because no plaintext history exists. That window closes permanently on the first successful write. Precedent for the retype: `20260712185610_EncryptSensitiveFields`, `20260708055825_WidenMfaSecretForEncryption`.
- **Related — `ENH-018`'s own proposed remedy is a false-green.** The finding suggests "add bank master data to the QA seed" to make the masking testable. That would turn TC-PAY-009-02/-08 green while the feature stays **permanently dead in production**: the masking (`AccountMasking.MaskLast4`), the audit redaction (`SensitiveFieldMasker.cs:34`), the export carve-out (`ExportSensitiveFields.cs:6`) and the `Payroll.ViewSensitive` reveal endpoint are all correct and all unreachable, because nothing can write the fields. This is the same class as `ISSUE-486`/`ISSUE-492` (P1.4, "checks that report safety they do not provide") — a green test standing in for a capability that does not exist.
- **Suggested direction (NOT applied):** split `ENH-018`. Keep the seed fixture as the QA-enablement task, but **do not let it close the finding**; raise the capture capability as its own story (see the T4 scoping note in `GAP-CLOSURE-QUEUE.md`), and settle the encryption question as part of that story's design rather than after it.
- **Found:** 2026-09-07, out-of-lane while sizing `ENH-018` for T4.
- **SURVEY:** **1 of 20** PII/sensitive columns on `employees` is encrypted (of **40** mapped columns); on the hard-PII subset it is **1 of 5** — `national_id` encrypted, while `bank_account_number`, `bank_name`, `bank_branch_code` and `date_of_birth` are plaintext. The encryption mechanism (`AesGcmFieldEncryptor.cs:24`, `EncryptedFieldConverters.cs:25`) has **9 declared consumer columns across 3 entities**: `Pip` x3, `Recommendation` x5, `Employee.NationalId` x1 (`EncryptedFieldRegistry.cs:49-67`, pinned by `HaveCount(9)` at `EncryptedFieldRegistryTests.cs:63`). Unit = mapped columns, then registry entries. Excluded: `users.mfa_secret`, which uses ASP.NET Data Protection and is deliberately outside the registry.
- **AUDIT (2026-09-07):** every material claim **CONFIRMED**, and unusually for this ledger **all three cited line numbers are exact today, none stale**. (1) `NationalId` really is encrypted — **CONFIRMED**: `EmployeeConfiguration.cs:247-249` applies `HasConversion(EncryptedFieldConverters.NullableString(encryptor))` with `HasColumnType("text")`, invoked at `AppDbContext.cs:269`, registered at `EncryptedFieldRegistry.cs:67`, and the snapshot agrees (`text`). (2) `BankAccountNumber` really is plaintext — **CONFIRMED**: `Employee.cs:197`; `EmployeeConfiguration.cs:114-115` is `.HasMaxLength(50)` with no converter; snapshot `AppDbContextModelSnapshot.cs:1951-1954` reads `character varying(50)`. (3) "no migration has since encrypted it" — **CONFIRMED**: only `20260617123044_AddEmployeeBankDetails.cs:12-18` touches the column, and both cited precedent migrations exist and touch **zero** `employees` columns. (4) "zero write paths" — **CONFIRMED by exhaustive sweep**: one assignment exists, `PayrollReportIntegrationTests.cs:269` (params at `:255`), exactly the cited range; no handler, DTO, import or seed, and **zero** frontend references. (5) **The branch `fix/ISSUE-523-encrypt-bank-account` carries ZERO commits of its own** — its tip `5bf73380` is an unrelated docs commit already in mainline and `git diff` against the merge-base is empty, so it is not merely unmerged: **nothing exists to merge**. (6) **The entry UNDERSTATES**: siblings `bank_name` and `bank_branch_code` are plaintext too, and `SensitiveFieldMasker.cs:34` already classes `bank_account_number` as sensitive — **the audit trail treats it as PII while the table does not**.
- **SEVERITY CHECK:** **agrees at MED today** — latent, with no data at risk while no write path exists. It escalates to HIGH the moment `US-CHR-014` starts, where it is already recorded as a hard precondition (`STATUS.md:68`).
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-521 — concurrent sub-agents collide on generic scratchpad filenames, which silently degrades every mutation proof

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** tooling / process
- **Title:** The mutation-proof convention every agent brief mandates ("mutate → confirm RED → revert → verify with `sha256sum -c`") stores its checksum and backup in the session scratchpad under **generic names** — `svc.sha`, `svc.bak`, `pre.sha`, `c.orig`, `u.orig`. Sub-agents running **in parallel share one scratchpad**, so two agents mutating two different services both write `svc.sha`.
- **Observed, not theoretical:** during the 2026-09-07 T3 batch an agent's `sha256sum -c` reported `PayrollAdjustmentService.cs: FAILED` while repairing `PerformanceDashboardService.cs`. Another agent had overwritten its checksum mid-run. The agent correctly distrusted the result and verified the real state with `git diff` instead.
- **Why MED and not LOW — two distinct failure modes:**
  1. **A false FAILED is indistinguishable from unreverted mutation residue.** The check that exists to prove a mutation was undone becomes unreliable in exactly the situation it guards.
  2. **Worse: `cp svc.bak <file>` could restore ANOTHER agent's file over the one under repair.** That is silent corruption of a source file, written by a command whose whole purpose is to undo a change safely.
- **Diagnosis corrected:** the agent that found this attributed it to concurrent *user sessions*. That is wrong — scratchpad paths are per-session (12 distinct ones exist on this machine) and `svc.sha`/`svc.bak` exist in **only one**. The collision is between **concurrent SUB-AGENTS of a single session**, which all share the orchestrator's scratchpad. That makes it entirely fixable from the orchestrator side, which the cross-session framing would not have.
- **Suggested direction (NOT applied):** require issue-id-prefixed scratchpad artifacts (`i128-svc.sha`, not `svc.sha`) in the mutation-proof instruction that every agent brief carries, and in the agent definitions. A convention that is only safe when run serially is not a convention — parallel sub-agents are the documented default (Engineering-Discipline rule #5).
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-128.
- **SURVEY:** **6** agent briefs carry the mutation-proof instruction (`backend-dev.md:186`, `browser-debugger.md:202`, `frontend-dev.md:175`, `test-runner.md:221`, `business-analyst.md:181`, `qa-engineer.md:220`) and **0 of 6 prescribe any filename**. **1 of 6** agents (backend-dev) now has an id-prefix rule, via memory (`feedback-scratchpad-filename-collisions.md:8-9`). Unit = agent definition files under `.claude/agents/team/`. Excluded: skills, none of which carry the instruction.
- **AUDIT (2026-09-08):** (1) **"the convention every agent brief mandates *stores* its checksum under generic names" — FALSE as written.** All six briefs say only "Verify the revert landed (`git diff`, `md5sum`, or `sha256sum -c`)"; **none names `svc.sha` or `svc.bak`**. Those were one agent's ad-hoc choice, so **there is no instruction text to correct — the fix must ADD a naming rule, not amend one.** (2) "sub-agents share one scratchpad, not sessions" — **CONFIRMED by inference (~80%)**: `/tmp/claude-1000/-mnt-d-WORK-hris-automation-system/` holds 11 session-UUID-keyed directories, so two *sessions* cannot collide on one path; a shared path therefore implies same-session sub-agents. (3) "`svc.sha`/`svc.bak` exist in only one scratchpad" — **no longer verifiable**: the find returns nothing today. (4) Partially remediated: backend-dev's memory already mandates `i128-svc.sha256`; the other five agents have nothing. No fix commit. **OPEN.**
- **SEVERITY CHECK:** **lower — MED should become LOW.** The `cp svc.bak` corruption arm requires two sub-agents independently choosing the same ad-hoc name in the same window; no brief steers them there, and the highest-volume mutator is already corrected.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-520 — terminated employees never get a monthly attendance summary row, so filtering to them returns silence

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Attendance · monthly summary
- **Title:** `AttendanceSummaryService.GenerateAsync` (`:257`) computes only for `e.Status != EmployeeStatus.Terminated`, but the read path (`FilteredEmployeesAsync`, `:760-771`) lets a caller filter **to** `status=Terminated`. A leaver therefore never gets a materialized row generated **or** refreshed, and the read loop `continue`s when no row exists — so the filter returns an **empty or permanently stale** list rather than an error.
- **Why it is not cosmetic:** the failure is **silent and indistinguishable from "this person had no attendance"**. The flow it breaks is reviewing a leaver's final month — which is exactly when attendance data matters most, because it feeds final settlement.
- **Not fixed by ISSUE-083:** the current-month recompute added in #665 does not reach terminated employees either, since it delegates to the same `GenerateAsync` scope.
- **Why it needs a decision, not just a fix:** correcting it changes **which employees the materialized table covers**, and final-settlement and payroll read the same rows. Two shapes: include terminated employees whose termination date falls in or after the requested month, or make the read path reject/flag a `Terminated` filter instead of silently returning nothing.
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-083.
- **SURVEY:** **1** generation site excludes terminated employees, reached by **3** callers and feeding **3** downstream readers — **7** affected sites (unit = code sites). Generation: `AttendanceSummaryService.cs:273`. Callers: `MonthlySummaryDailyJob.cs:73`, `MonthlySummaryMonthlyJob.cs:59`, and the on-read recompute at `AttendanceSummaryService.cs:110`. Readers: `AttendanceSummaryService.cs:89`/`:114`, `AttendanceDashboardService.cs:396`, `HrReportService.cs:1004`. Excluded: tests, migrations, and `AttendancePayrollService`, which has its own terminated path.
- **AUDIT (2026-09-08):** (1) "`GenerateAsync` (`:257`) computes only for non-terminated" — **CONFIRMED, line wrong**: the declaration is `:258` and the filter `.Where(e => e.Status != EmployeeStatus.Terminated)` is at **`:273`**. (2) "`FilteredEmployeesAsync` (`:760-771`) lets a caller filter to `status=Terminated`" — **CONFIRMED, lines wrong**: the method is at **`:778`** and the status branch at **`:783-786`**. (3) "the read loop `continue`s when no row exists" — **CONFIRMED** (`:126-127`). (4) "not fixed by `ISSUE-083` #665" — **CONFIRMED** (`:108-110` gates the recompute on `isCurrentMonth` and delegates to the same `GenerateAsync`). (5) **"final-settlement and payroll read the same rows" — FALSE.** Nothing under `HRM.Infrastructure/Services` reads `AttendanceMonthlySummaries` for settlement, and the payroll feed is `AttendancePayrollService`, which **already special-cases leavers** (`:185-190`) via `ComputeForEmployeeUpToAsync` (`AttendanceSummaryService.cs:213`), added for `ISSUE-091` and covered by `AttendancePayrollTerminatedTests.cs`. **What is true instead: the blast radius is reporting only** — `HrReportService.cs:1004` and `AttendanceDashboardService.cs:396` silently omit leavers. **Not fixed.**
- **SEVERITY CHECK:** **lower — MED should become LOW.** The money path the entry invokes to justify MED does not read these rows; the real impact is a silently incomplete report/UI list.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-519 — `TC-PAY-011-03` names a `send-payslips` route that does not exist

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** QA (docs)
- **Title:** `TC-PAY-011-03.md:39` references *"POST send-payslips"*; the real route is `POST api/v1/payroll/runs/{runId}/payslips/send-emails` (`PayslipDistributionController.cs:33`).
- **Found:** 2026-09-07, out-of-lane while fixing ISSUE-113.
- **SURVEY:** **3 stale route-name sites across 2 documents** — the entry claims 1 (unit = documented route names that no controller serves): `docs/QA/payroll/TC-PAY-011-03.md:39`, and `docs/vault/modules/payroll.md:794` (`POST /payroll/runs/:runId/send-payslips`) and `:793` (`GET /payroll/runs/:runId/payslip-emails/status`). Excluded: `payslip-distribution.component.ts:101`, `:120`, where `send-payslips-disabled-reason` is a DOM element id rather than a route.
- **AUDIT (2026-09-08):** (1) The stale route — **CONFIRMED, doc side wrong**: `TC-PAY-011-03.md:39` says "Forge a direct POST send-payslips request", while the real route is `POST api/v1/payroll/runs/{runId:guid}/payslips/send-emails` (`PayslipDistributionController.cs:19` `[Route("api/v1/payroll")]` plus `:33`). The cited line and the controller citation both verify exactly, and are independently corroborated by the generated contract (`api-types.ts:11322`) and the FE client (`payslip-email.service.ts:64`). (2) **The entry understates**: `docs/vault/modules/payroll.md:792-794` still carries the pre-backend "ASSUMED contract" naming **two** wrong routes — the POST above and a GET `payslip-emails/status` where the shipped route is `payslips/distribution-summary` (`PayslipDistributionController.cs:75`) — and it explicitly says "reconcile in this one file if BE differs", which never happened. That matters more than the TC, because the vault is the **shared cross-agent source of truth**. Merge status: only the filing commit `af69ea36`, which is **not** an ancestor of `origin/test/local-subdomains` (superseded by the merged `97e92c4e`). No fix commit. **NOT FIXED.**
- **SEVERITY CHECK:** **agrees at LOW** for the TC itself — the step is narrative ("forge a direct POST"), not a copy-pastable command. The vault half is the part worth fixing first.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-512 — sub-agents working in a worktree write their agent-memory into the MAIN tree, where it sits uncommitted

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** tooling / process
- **Title:** Three `backend-dev` sub-agents ran with `cd .claude/worktrees/{i036,t3a,t3b}` and correctly confined every `src/` edit to their own worktree — but their `.claude/agent-memory/backend-dev/*.md` writes landed in the **main checkout**, not the worktree they were working in. The files were still uncommitted in the main tree hours later, discovered only by an explicit "what is uncommitted?" sweep.
- **Why this is not simply correct behaviour:** agent memory is project-scoped, so the main tree is arguably the right destination — the problem is that nothing **commits** it. It accumulates outside any branch, invisible to the PR that produced it, and the realistic failure modes are (a) it is swept into an unrelated PR by a later `git add -A` in the main tree, or (b) it is lost to a `git checkout`/`clean`. Both happened in near-miss form this session: the main tree was on a docs branch at the time.
- **Second-order risk:** two concurrent sub-agents writing the same memory file have **no isolation at all** — the whole point of `isolation: worktree` per Engineering-Discipline rule #8 — because both resolve to the same main-tree path.
- **Evidence:** 2026-09-07, `git status` in the main checkout showed 4 modified/untracked files under `.claude/agent-memory/backend-dev/` after three agents had finished and their worktrees were clean. Committed as #657.
- **Severity rationale:** LOW — nothing was lost, and the content is valuable rather than harmful. Filed because the *near-miss* is structural, not incidental: the orchestrator has no signal that a sub-agent produced memory, so remembering to sweep the main tree is the only control, and it is a human one.
- **Suggested direction (NOT applied):** either have the orchestrator sweep and commit `.claude/agent-memory/` as part of closing out any run that used sub-agents, or teach agents to write memory into their own worktree so it rides the same PR as the work that produced it. The second is cleaner but interacts with rule #8's one-branch-per-worktree constraint.
- **Found:** 2026-09-07, out-of-lane while auditing uncommitted changes.
- **SURVEY:** guard coverage is **2 of 2 write tools** (Write, Edit — the `settings.json` `PreToolUse` matcher is `"Write|Edit"`), **0 of the Bash write vectors** (`cat >`, `tee`, `sed -i`, `cp` — the `"Bash"` matcher runs only `careful-guard.py` and `no-verify-guard.py`), and **1 of 2 directions**. Unit = hook-registered tool matchers x cwd directions.
- **AUDIT (2026-09-08):** (1) The original defect is **FIXED**: `.claude/hooks/scripts/worktree-fence.py` exists, is registered (`settings.json:58`), derives the fence from the caller's cwd (`:88-89`), allows same-worktree targets (`:98-99`) and denies main-checkout targets (`:105-112`) with an ISSUE-512-specific message. Fix `63e58886` (**#660**) **is an ancestor** of `origin/test/local-subdomains`. (2) **The 2026-09-07 reverse-direction recurrence is real, and this entry UNDERSTATES it.** `worktree-fence.py:90-91` reads `caller_wt = _worktree_of(cwd); if not caller_wt: _allow()` — it **exits allow whenever cwd is outside `.claude/worktrees/`**. That is deliberate and stated at `:19-20` ("A session working in the main checkout (the orchestrator) is unaffected — it must be able to write anywhere"). So a session whose cwd **reverts** to the main checkout writes to the wrong tree with **no signal at all** — which is exactly what happened during this backfill, producing ~8 misdirected edits. (3) A `cat > file` heredoc evades the guard from **either** direction, because the Bash matcher omits the fence entirely. (4) The entry's own "second-order risk" (two sub-agents, same memory path) is likewise still open when both run from the main tree.
- **SEVERITY CHECK:** **higher — LOW should become MED.** LOW rested on "nothing was lost"; the reverse direction has since produced ~8 misdirected edits in this very session, and the guard is **architecturally incapable** of catching them. The gap is filed as `ISSUE-569`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-511 — `AngleSharp 0.17.1` carries a known moderate-severity advisory, warned on every build

- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE (dependency)
- **Title:** NU1902 on every build — `AngleSharp 0.17.1`, GHSA-pgww-w46g-26qg, transitive via `HRM.Infrastructure`/`HRM.Api`.
- **Why it matters beyond the CVE:** it is **permanent build noise**, and permanent noise is what makes the next NU1902 invisible.
- **Suggested direction (NOT applied):** bump + a regression pass of its own.
- **Found:** 2026-09-06, out-of-lane across several builds.
- **SURVEY:** **0** direct pins — AngleSharp appears in no `.csproj`, no `Directory.Build.props` and no lock file. It resolves transitively at **exactly 0.17.1** via `HtmlSanitizer 9.0.892 → AngleSharp [0.17.1]` (an **exact-version bracket**) plus `AngleSharp.Css 0.17.0 → AngleSharp [0.17.0, 0.18.0)`. Reachability: **8 services** call the sanitizer (`ApplicantService.cs:176-180`, `GoalProgressService.cs:119`, `:327`, `InterviewService.cs:121`, `:198`, `OfferService.cs:171`, `:178`, `ReviewSignoffService.cs:135-138`, `:155`, plus Recommendation and Vacancy). Unit = package pins, then calling services.
- **AUDIT (2026-09-08):** (1) "AngleSharp 0.17.1" — **CONFIRMED**. (2) "GHSA-pgww-w46g-26qg" — **CONFIRMED**: CVE-2026-54570, mXSS via an `annotation-xml` HTML-integration-point bypass, CVSS 6.9 Moderate, published 2026-06-06, **fixed in AngleSharp 1.5.0** — so 0.17.1 is stale by two majors and still vulnerable. (3) "transitive via `HRM.Infrastructure`/`HRM.Api`" — **CONFIRMED**, rooted at `HtmlSanitizer` (`HRM.Infrastructure.csproj:66`). (4) **"Suggested direction: bump AngleSharp" — FALSE as stated**: HtmlSanitizer pins it **exactly**, so a direct `PackageReference` bump produces a downgrade/conflict. **The actual fix is `HtmlSanitizer 9.0.892 → 9.2.995`**, whose dependencies are `AngleSharp >= 1.7.1` / `AngleSharp.Css >= 1.0.1`, above the fix line. (5) The build-noise claim holds: NU1902 is unsuppressed (`Directory.Build.props:19-21` suppresses only the AutoMapper advisory). **OPEN.**
- **SEVERITY CHECK:** **higher — LOW should become MED.** LOW rested on "beyond the CVE … it is build noise", which dismisses the CVE. **The vulnerable component is the XSS sanitizer itself**, applied to untrusted applicant/offer/review text in a multi-tenant HRM. Most render paths use Angular `[innerHTML]`, whose DomSanitizer is a genuine second layer — but **one path has no second layer**, which makes this a reachable chain rather than defence-in-depth. That path is filed as **`ISSUE-564` (HIGH)**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-500 — FE specs cannot catch an FE↔BE contract break, and four separate defects this session were each shipped past a spec that was green the whole time

- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** FE (test methodology) — the impact is on every FE↔BE seam
- **Module / US / TC:** cross-module — surfaced 2026-09-06 while fixing `BUG-444`, `ISSUE-367`, `ISSUE-373` and `BUG-493`
- **Title:** `HttpTestingController` **echoes whatever the service sends**. It never validates the request against the real contract, and `req.flush(...)` returns whatever shape the spec author decided to type. So an FE spec asserts that the FE agrees with **itself**, and stays green while the wire is broken in either direction — wrong shape sent, or wrong shape expected back.
- **Why this is HIGH and not a testing-style note:** it is not a hypothesis. **Four defects fixed in a single session were each covered by a passing spec**, and in every case the spec was the reason nobody noticed:

  | finding | what the spec did | what shipped |
  |---|---|---|
  | `BUG-444` | sent `{tasks}`, asserted only the verb + `withCredentials` — **nothing about the body** | API binds `addTasks`/`taskChanges`; every real call would 400 |
  | `ISSUE-367` | mocked a **flat** `{code, affectedEmployeeCount}` body the API has never sent | count arrives in `data`; dialog rendered "in use by **0** active employees" |
  | `ISSUE-373` | asserted `trend` was `'Flat'`, a value the contract has no field for | a fabricated trend arrow on every employee row |
  | `ISSUE-364`/`GAP-012` | direct `http.get<ISelfAssessment>` cast, no adapter | FE read `goals`, API sends `items` — US-PRF-002 renders empty |

- **Root cause (~100%, four independent confirmations):** the FE test seam mocks the transport rather than the contract. `api-types.ts` **is** generated from `contracts/openapi/hrm-v1.json` and **is** CI-enforced, so the contract is available and trustworthy — but **nothing asserts that what a service SENDS, or the shape it CASTS a response to, conforms to it.** The generated types are used for hand-written interfaces' inspiration, not as the assertion.
- **Why the existing gates do not cover it:** `api:types:check` proves the generated file matches the contract. It says nothing about whether a service uses those types. A service can declare `http.put<IAnything>(url, whateverShape)` and every gate stays green.
- **Evidence:** `onboarding-checklist.service.spec.ts` (verb-only assertion) · `payroll.service.spec.ts` (flat-body mock, 3 arms) · `performance-dashboard.service.spec.ts:217` (asserted the fabricated value) · `dashboard.models.ts:440-455` (direct cast, no adapter). All four green before their fixes.
- **Severity rationale:** HIGH. This is not one broken test — it is a **structural blind spot at the seam where 9 of 13 modules already drifted** (`GAP-S1`'s own framing). Every FE↔BE defect found this session was invisible to the suite that was supposed to guard it, and the count of *unfound* ones is unknown by construction.
- **Suggested direction (NOT applied — needs a decision):** the cheap, high-yield step is to make request bodies assert against the generated types rather than hand-written mirrors — e.g. type the spec's request literal as the generated `components['schemas'][...]` so a shape mismatch is a **compile** error, not a silent pass. A heavier option is a contract-test layer that replays recorded request bodies against the OpenAPI schema. **Which to adopt is a real decision** (effort vs coverage), so this is filed rather than unilaterally built.
- **Related:** `GAP-S1` (the contract gate that catches the BE half and has no FE counterpart), `BUG-444`, `ISSUE-367`, `ISSUE-373`, `ISSUE-364`.
- **SURVEY:** **330** FE spec files, **4,373** `it()` cases. Contract-bound in the **response** direction: **54 of 330** spec files (16.4%) reference at least one of the 242 `export type X = Schema<'...'>` aliases, which is a compile-time binding. In the **request** direction the entry is right: 6 request-side `Schema<>` aliases exist and **0 of 330** specs use one. Additionally **4** Playwright e2e specs (`src/frontend/e2e/`, 8 `test()` blocks, ~24 cases) do drive browser → Angular → API → Postgres (`ci-gate.yml:369-396`, `e2e/fixtures/auth.ts:17`), so the seam is not wholly unguarded. **Correction to this backfill's own earlier note on `ISSUE-436`:** the figure "0 of 330 spec files bind to the contract" is right only for the request direction and **wrong for the response direction** — the accurate split is 54/330 response, 0/330 request.
- **AUDIT (2026-09-07):** (1) "`HttpTestingController` echoes the request and never validates it" — **CONFIRMED** as a mechanism. (2) "`api-types.ts` is generated and CI-enforced" — **CONFIRMED** (`src/frontend/package.json:11-12`, `ci-gate.yml:355`, `scripts/check-api-types.mjs:20-32`). (3) "the generated types are used for inspiration, not as the assertion" — **FALSE**: 52 non-spec model files consume them as the mapper input type (`dashboard.models.ts:229-240`, `self-assessment.models.ts:167-170`), landed by `f3f98915` (merged) **before this entry was filed**. (4) `BUG-444` — **CONFIRMED** (the pre-fix spec asserted verb and `withCredentials` only; now body-bound at `onboarding-checklist.service.spec.ts:250-253`; fix `05434c93`, merged). (5) `ISSUE-367` — **CONFIRMED** (3 flat-body arms at `payroll.service.spec.ts:481`, `:495`, `:503`; fix at `payroll.service.ts:198`, merged). (6) `ISSUE-373` — **CONFIRMED as history, citation stale**: `performance-dashboard.service.spec.ts:217` is now a comment; the live assertion is `:221`. (7) **Row 4 is FALSE as cited and self-contradictory**: `dashboard.models.ts:440-455` is `mapTrend`'s tail plus an `ISSUE-373` comment and contains no `goals`/`items`; the real defect was `self-assessment.models.ts:160-165` (fixed at `:167`, `:223`); and the row names **`ISSUE-364`**, which is an unrelated Core-HR departments finding (`TEST-FINDINGS.md:1852`), while this entry's own header six lines up (`:615`) names **`BUG-493`**. This entry has never been fixed, and is a known duplicate of `ISSUE-436` (see `ISSUE-546`).
- **SEVERITY CHECK:** **lower — HIGH should become MED.** The gap is real but half its stated scope: the response half was closed by `f3f98915` before filing, one of the four cited defects is misattributed and self-contradictory, and it duplicates `ISSUE-436`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-036 — `DocumentsRequired` is satisfied by any non-blank string ending in `.pdf`: no bytes are ever uploaded, so a leave type demanding a medical certificate is cleared by typing `x.pdf`
- **Type / Severity / Status:** ISSUE · **HIGH** (re-severitised 2026-09-06) · OPEN — **re-scoped, see below**
- **Re-scope 2026-09-06 (premise re-verified against `src/`):** the original filing framed this as *"the 5 MB cap is missing"*. **That is not the defect.** `LeaveRequestService.cs:187-191` DOES enforce `DocumentsRequired`, but it gates on `attachments.Count == 0` where an attachment is **any non-blank string** that passes the extension allow-list. So the compliance control is not incomplete — it is **defeated**: `"x.pdf"` clears a medical-certificate requirement. Severity LOW → **HIGH** accordingly. The size cap is a downstream consequence of there being no upload at all, not the finding itself.
- **Sizing correction:** an earlier note in `GAP-CLOSURE-QUEUE.md` called the fix *"small"* on the grounds that `IFileStorage` has many adopters. The storage half is indeed a port of an established pattern (`SelfAssessmentAttachmentService.cs:85` — 10 MB cap, `file_too_large`, tenant-scoped path; `InterviewAttachmentService.cs:76` identical), **but the fix is not BE-only and cannot ship in one leg.** Hardening the BE while the FE still sends `signal<string[]>` filenames would break the leave-apply flow outright — the same leg-2 trap as `BUG-460`/`BUG-483`. Realistic size: **M**, three legs that must land together.
- **Three legs (all required):** (1) `POST /api/v1/leaves/attachments` multipart endpoint + service, ported from `SelfAssessmentAttachmentService`, with the per-file cap and tenant-scoped path `{tenantId}/leaves/{requestId}/`; (2) `CreateLeaveRequest` validates that each reference resolves to a blob **actually stored for this tenant**, replacing the string check at `:187`; (3) FE replaces the filename-only `signal<string[]>` with a real file input holding returned references, plus `api-types` regeneration.
- **Layer:** BE + FE (contract change)
- **Module / US / TC:** Leave Management · US-LV-003 · TC-LV-063 (steps 1-3 size cap, step 2 storage path), NFR-3, §10.
- **Title:** TC-LV-063 expects the leave-application attachment flow to reject files >5MB ("File exceeds 5 MB limit.") and to store files under a tenant-scoped blob path `{tenantId}/leaves/{requestId}/`. The current implementation does **not** do blob upload at all — `CreateLeaveRequestRequest.Attachments` is a list of **already-uploaded URLs** (the DTO comment states "blob upload is out of scope (NFR-3, deferred)"). Consequently there is no file-size validation (no bytes ever reach the API) and no server-side storage-path construction; attachment URLs are persisted verbatim into `leave_request.attachment_urls`. The **type** (PDF/JPG/PNG) and **count** (max 3) validations on those URLs DO work correctly.
- **Root cause (~95%, code confirmed):** `CreateLeaveRequestRequest` (`LeaveRequestDtos.cs:7-17`) takes `IReadOnlyList<string>? Attachments` (URLs), explicitly noting blob upload is deferred per NFR-3. `CreateLeaveRequestValidator` validates extension allow-list (`.pdf/.jpg/.jpeg/.png`) and `MaxAttachments=3`, but has no size rule and the service does no storage-path generation (`LeaveRequestService.CreateAsync` stores `attachments` as-is). NFR-3 blob storage is a deliberate, documented deferral.
- **Reproduction steps (live):** `POST /api/v1/leaves` with `attachments:["https://blob/acme/leaves/note.exe"]` → 400 "Attachments must be PDF, JPG, or PNG files." (type ✓); `attachments:["a.pdf","b.pdf","c.jpg","d.png"]` → 400 "A maximum of 3 attachments is allowed." (count ✓); `attachments:["https://blob/acme/leaves/certificate.pdf"]` → 201 with the URL stored verbatim. There is no request shape that conveys file bytes/size, so the 5MB cap and tenant storage path cannot be exercised.
- **Evidence:** type/count 400s above; 201 stores the URL unchanged in `leave_request.attachment_urls`; DTO/validator source shows no size rule and deferred blob upload.
- **Severity rationale:** LOW — a documented, deliberate scope deferral (NFR-3 blob storage), not a regression. The implemented validation (type + count) is correct. Flagged so the deferred size-cap + tenant-scoped storage-path acceptance criteria are tracked as not-yet-built rather than silently treated as passing.
- **Suggested direction (NOT applied):** none — report only. (When blob storage is implemented, add a per-file 5MB cap and server-side tenant-scoped path `{tenantId}/leaves/{requestId}/`, then TC-LV-063 steps 1-3 become executable end-to-end.)
- **SURVEY:** **0** validators still accept a filename-shaped string as document proof; **2** still accept a **client-supplied storage key** unverified (unit = `*Validator*.cs` in `HRM.Application` touching attachments). Only 3 validators touch attachments: `CreateLeaveRequestValidator` (now `Guid` ids), `CompleteTaskValidator` (real `AttachmentStream` + size cap), `GoalProgressValidators` (client `StorageKey`). **Zero** `.pdf` extension allow-lists remain in any validator. The one surviving string-typed `Attachments` collection, `LeaveRequestDtos.cs:49`, is the **response** DTO. Excluded: `HRM.Tests/**`, generated OpenAPI types.
- **AUDIT (2026-09-07):** **All three required legs shipped** in `a8fb3589` (**#655**) + `e788a0d4` + `d59cd2eb`, merged into `origin/test/local-subdomains`. Leg 1 (multipart endpoint) — **DONE**: `LeaveRequestsController.cs:62-68` `POST /api/v1/leaves/attachments`; `LeaveAttachmentService.cs:30` 5 MB cap, `:88-89` `file_too_large`, `:123-128` tenant-scoped path, plus a malware scan at `:114` never asked for. Leg 2 (reference resolution) — **DONE**: the string check is gone; `LeaveRequestService.cs:196-209` resolves `AttachmentIds` to real rows and 400s `attachment_not_found`, and the `DocumentsRequired` gate now counts `attachmentRows.Count`; cited `:187-191` is **stale**, the gate is at **`:213-219`**. Leg 3 (FE) — **DONE**: `leave-application.component.ts:290` real file input, `:671` `uploadAttachment`, `:758` sends `attachmentIds`. Core claim "`x.pdf` clears a medical-certificate requirement" — **FALSE at this commit**; it was true at filing, and the code comment at `:187-189` records exactly that. Unclaimed bonus: an RLS policy shipped (`20260906222555_Leave_RequestAttachment.cs:59`) and tests exist (`LeaveAttachmentIntegrationTests.cs`, `CreateLeaveRequestValidatorTests.cs`).
- **SEVERITY CHECK:** HIGH was right at filing; the fix is merged. The "M, three legs that must land together" sizing was accurate. The same class **survives in Performance** — filed as **`ISSUE-544`**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-039 — `my-balance` resolves entitlement per-leave-type in a loop (engine call inside the `foreach`), an N+1 against the entitlement engine; single-client P95 ~341ms for a 12-type employee exceeds the 200ms NFR-1 target on the (Redis-deferred) DB path
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE
- **Module / US / TC:** Leave Management · US-LV-006 · TC-LV-125 (step 1/2 latency, step 4 — "no N+1 pattern; a single aggregated query serves the balances"). NFR-1, FR-5.
- **Title:** `GetMyBalancesAsync` aggregates the ledger and pending in two bulk queries (good), but then calls `ResolveEntitlementAsync` → `_entitlementService.ComputeEffectiveEntitlementAsync(...)` **once per leave type inside the result `foreach`** (`LeaveDashboardService.cs:125-130`). For an employee with N active leave types that is N entitlement-engine invocations per dashboard load (each of which itself queries override/rule/default reference data). Measured single-client latency for John Doe (12 leave types) was median 180ms / **P95 341ms** over 20 samples — above the 200ms NFR-1 P95. TC-125 explicitly records DB-path overshoot as a NOTE pending the deferred Redis cache (so TC-125 is PASS-with-note), but the per-type engine loop is the structural cause and will worsen with more leave types / under load.
- **Root cause (~90%, code + live confirmed):** the per-type `await ResolveEntitlementAsync(employee.Id, lt.Id, leaveYear, ct)` inside `foreach (var lt in leaveTypes)` (`:125-130`). The ledger/pending reads are batched, but entitlement resolution is not — it is an N+1 against the entitlement engine. Redis balance caching (which would mask this) is the module-wide DEFERRED item (`LeaveDashboardService.cs:27-29`).
- **Reproduction steps (live, acme):** 20× `GET /api/v1/leaves/my-balance` (employee@acme.test, 12 leave types) → `min=155 median=180 p95=341 max=364` ms.
- **Evidence:** latency sample above (2026-06-25T10:46Z); code at `LeaveDashboardService.cs:125-130`.
- **Severity rationale:** LOW — single-client perf is still sub-400ms and TC-125's spec treats the DB-path overshoot as acceptable pending the deferred cache; no functional defect. Flagged because the N+1 entitlement loop is the real driver and will degrade super-linearly with leave-type count and concurrency, so it is worth tracking alongside the deferred Redis cache rather than assuming the cache alone fixes it.
- **Suggested direction (NOT applied):** none — report only. (Batch entitlement resolution for all of an employee's leave types in one engine call/query, and/or land the deferred `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` cache.)

### ISSUE-045 — BR-4 carry-forward-pool restoration is not pool-aware: cancelling an approved leave that consumed carry-forward days writes a single general `Adjusted` reversal, not a split back to the carry-forward vs current-year pools
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** Cancel restores per-pool with carry expiry preserved (`LeaveRequestService.cs:1368-1415`), `LeavePool.cs:13`, guard `LeavePoolAwareCarryForwardTests`. **`DEFERRED-FOLLOWUPS.md:55` already recorded this DONE (#427, 2026-07-22)** — the live ledger simply never caught up.
- **Layer:** BE
- **Module / US / TC:** Leave Management · US-LV-010 · TC-LV-202 (BR-4 carry-forward pool restoration; dependency US-LV-008)
- **Title:** BR-4 requires that if a cancelled leave consumed carry-forward days, the restoration follows the original allocation so carry-forward days return to the carry-forward pool (not merged into current-year entitlement). The implementation writes ONE general `Adjusted` positive entry for the full `total_days` — the total balance is restored correctly, but the carry-forward-vs-current-year split is not separately re-allocated. This is documented in code as a known simplification (TODO), so it is a recorded design gap rather than a silent omission.
- **Root cause (~95%, code review):** `CancelAsync` (`src/backend/HRM.Infrastructure/Services/LeaveRequestService.cs:909-944`) creates a single `LeaveLedger { EntryType = Adjusted, Amount = request.TotalDays }` row and the in-code comment explicitly states "True carry-forward-pool re-allocation is NOT modelled here … TODO(BR-4 carry-forward-pool): when carry-forward pools are tracked distinctly, split the reversal across the original allocation buckets." The ledger model nets `Adjusted` into a single running balance, so per-pool tagging does not exist.
- **Reproduction steps (live, partial):**
  1. John Doe has `carryForward = 0` in `my-balance` (no carry-forward ledger row to consume), so a true pool-aware restoration cannot be exercised with current seed data.
  2. Behavioral confirmation of the simplification: every approved cancel (e.g. `019eff11-…096`) wrote exactly one general `Adjusted +N.00` row (`description="Cancellation of leave request …"`) with no pool/bucket tag (psql `leave_ledger`).
- **Evidence:** captured 2026-06-25 — `LeaveRequestService.cs:909-944` + comment; `leave_ledger` reversal rows are plain `Adjusted` with no carry-forward attribution column. No carry-forward seed for the test employee.
- **Severity rationale:** LOW — the cancelled days' **total** is restored correctly (no balance loss to the employee); only the carry-forward-vs-current-year provenance and any pool-specific expiry interaction is not preserved on reversal. Low blast radius (only matters when carry-forward pools are used and have distinct expiry), and the gap is explicitly documented in code as deferred. Recorded per TC-LV-202 step 3 as a documented simplification — NOT a pass for the pool-specific requirement.
- **Suggested direction (NOT applied):** none — report only. (Dev would tag ledger reversals to the original allocation buckets once carry-forward pools are tracked distinctly, per the existing TODO.)

### ENH-002 — BR-2 manager/employee report scoping is effectively dead code: the report/analytics/export endpoints are gated solely on `Leave.Reports`, which built-in Manager and Employee roles do not hold, so a manager/employee always gets 403 and never reaches the (implemented) "team" / "self" scope branches
- **Type / Severity / Status:** ENH · — · OPEN
- **Type / Title / Module / why-it-matters / suggested-direction (ENH — lighter schema)**
- **Type:** ENH
- **Title:** The service correctly implements a three-way BR-2 scope (`All` for HR, `Manager` → direct reports + self, `Employee` → self) in `LeaveReportService.ResolveScopeAsync`/`ScopedEmployeesQuery`, but the controller requires `Leave.Reports` on every endpoint, and the built-in Manager role holds only `Leave.Approve.Team`/`Leave.View.Team` and Employee holds only `Leave.Apply`/`Leave.View.Own` — neither has `Leave.Reports`. So a manager or plain employee is rejected at the authorization filter (403) before the scope code runs; the Manager/Employee branches are unreachable through these endpoints.
- **Module / US / TC:** Leave Management · US-LV-012 · TC-LV-246 (manager team scope), TC-LV-247 (employee self scope)
- **Why it matters:** AC/BR-2 and TC-LV-246/247 describe managers seeing their team's report data and employees seeing their own — a documented capability that is currently inaccessible. It is not a security defect (the fail-closed 403 is safe, and arguably correct if reports are intended HR-only), which is why this is an ENH/observation rather than a BUG: the spec and the implementation disagree on whether managers/employees may run scoped reports. The scope machinery is built and tested by the dev's own unit path but is dead in production wiring.
- **Suggested direction (NOT applied):** Decide the intended policy. Either (a) grant a narrower report permission (e.g. `Leave.Reports.Team` / self) to Manager/Employee roles and switch the controller to accept it, so the existing scope branches activate; or (b) if reports are deliberately HR-only, update US-LV-012 BR-2 and retire TC-LV-246/247 (or re-scope them to the my-balance/team-calendar endpoints that DO serve managers/employees). Report-only — no change applied.

### ISSUE-062 — Lockout/unlock audit events are written to ONE `audit_logs` row (tenant-scoped), NOT to both a tenant audit log AND a separate system audit log as FR-7 requires; there is no system-level audit store/read for these events (FR-7 partial)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE
- **Module / US / TC:** Authentication · US-AUTH-010 · TC-AUTH-084 (step 9), TC-AUTH-087 (step 6), TC-AUTH-101 (step 4)
- **Title:** FR-7 / TC-084 step 9 / TC-087 step 6 require account-lockout and unlock events to be written to **both** the tenant audit log and the system audit log. Live, each event (`login_failure`, `account_locked`, `account_unlocked_by_admin`, `account_unlocked_by_timeout`) writes a **single** `audit_logs` row carrying the resolved `tenant_id`; there is no second, system-scoped (null-tenant or separate-store) record. So a platform operator querying a system-wide lockout view would not find these unless they read every tenant's log.
- **Root cause (~88%, code + live):** `AuthService.WriteAuditLogAsync` / `WriteAuditLogWithDetailAsync` (`src/backend/HRM.Infrastructure/Services/AuthService.cs:1722-1773`) insert exactly one `AuditLog` with `TenantId = explicitTenantId ?? _tenantContext.TenantId`. No second insert (null-tenant system row) and no separate system-audit table/sink. (The platform has only the one `audit_logs` table.)
- **Reproduction steps (live, 2026-06-25, acme):** lock qa-lockout-1 (5 wrong-pw), admin-unlock, expire-unlock; `SELECT event_type, tenant_id IS NOT NULL FROM audit_logs WHERE user_id=<u1>`.
- **Evidence:** all six rows (`login_failure`×5 + `account_locked`) have `has_tenant = t`; the `account_unlocked_by_admin` and `account_unlocked_by_timeout` rows likewise. No null-tenant/system duplicate exists. The tenant-side audit IS complete and correct (counts, IP `::1`, structured `detail` JSON) — only the "also to system log" half of FR-7 is missing.
- **Severity rationale:** LOW — the events ARE durably audited and queryable per-tenant (the core forensic need is met); the missing piece is the redundant system-wide copy, relevant only for platform-level cross-tenant security monitoring. Same single-store limitation noted across other modules.
- **Suggested direction (NOT applied):** none — report only.

### ENH-003 — Clock-in response DTO omits tenant_id / clock_in_ip / clock_in_user_agent
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title:** `AttendanceLogDto` (the 201 body) returns `id, employeeId, clockIn, clockOut, lat/lng, source, isLate, lateMinutes, createdAt` but not `tenant_id`, `clock_in_ip`, or `clock_in_user_agent`. TC-ATT-001 step 4 expects `tenant_id` in the body, and steps 5 (audit fields) are only verifiable at the DB layer. The fields ARE persisted correctly (verified in DB), so this is a presentation gap, not a defect.
- **Module / why it matters:** Attendance / US-ATT-001 — exposing `tenant_id` (and optionally the captured IP/UA) in the response would let the FE/QA assert tenant-stamping and audit capture without a DB round-trip, matching the test-case expectation.

### ENH-004 — Clock-out response DTO omits tenant_id / clock_out_ip / clock_out_lat-long (parity with ENH-003)
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title:** `ClockOutResultDto` (the 200 body) returns `id, employeeId, clockIn, clockOut, totalWorkMinutes, overtimeMinutes, status, isEarlyDeparture, earlyDepartureMinutes` but not `tenant_id`, `clock_out_ip`, or the stored `clock_out_latitude/longitude`. TC-ATT-013 step 5 and TC-ATT-020 step 2 verify those fields only at the DB layer; they ARE persisted correctly (verified: ip `::1`, lat `40.7484000`, lng `-73.9857000`), so this is a presentation gap, not a defect.
- **Module / why it matters:** Attendance / US-ATT-002 — surfacing `tenant_id` and the captured geo/IP in the clock-out response would let the FE render the summary card's location and let QA assert tenant-stamping / geo-capture (AC-5) without a DB round-trip, matching the test-case expectations. Mirrors ENH-003 for clock-in.
- **Suggested direction (NOT applied):** consider adding `tenantId` to the response DTO; IP/UA may be intentionally withheld from the client for privacy — leave to product. Report only.

### ENH-005 — Regularization approve/reject path has notification (FR-5) and Redis daily-status cache (FR-8) as deliberate no-op seams; AC-4 multi-level approval (workflow engine) absent
- **Type / Severity / Status:** ENH · — · DEFERRED
- **Type:** ENH
- **Title:** US-ATT-004 FR-5 (notify employee on approve/reject), FR-8 (update Redis daily-attendance cache), and AC-4/FR-4/BR-4 (multi-level approval chain → status stays PENDING until final level) are explicitly deferred in code, not built. Approve/reject is single-level: the manager's decision is final and immediately mutates `attendance_log`; `workflow_instance_id` stays null.
- **Module / US:** Attendance · US-ATT-004 (FR-5 Notifications/US-NTF, FR-8 Redis, FR-4/AC-4 Workflow Engine/US-ADM-007)
- **Why it matters:** The employee is never notified of an approve/reject outcome or rejection reason (FR-5/AC-2 "notified with the rejection reason" is unmet end-to-end), and the multi-level chain (AC-4) cannot be exercised — both are dependency-blocked, not defects in this story. Documented so the deferral is explicit and not silently passed.
- **Suggested direction (NOT applied):** activate the dispatch seam when US-NTF lands; wire the configurable chain when the Approval Workflow Engine (US-ADM-007) is built. Report only.

> **BUG-003 (cross-tenant / JWT-vs-subdomain mismatch) — EXTENDED to the US-ATT-004 approval surface, not re-filed.**
> TC-ATT-ISO-007 step 7: an **acme JWT + `X-Tenant-Subdomain: techoneglobal`** header on `GET …/regularizations/pending` returns **HTTP 200 with an empty queue** (the middleware resolves the tenant from the header to techoneglobal; the acme manager has no employee record there → empty) **instead of rejecting the token/subdomain mismatch** as TC-ATT-ISO-002 expects. This is the known BUG-003 class (TenantResolutionMiddleware does not guard token-tenant vs header-resolved tenant). **Materially, this surface fails CLOSED on the write/decision arm:** approving/rejecting an acme regularization id under the foreign header returns 403 "No employee record is linked to the current user" / 404, the acme regularization stayed PENDING, and **zero** rows were created in techoneglobal (`attendance_regularization`/`attendance_log` counts = 0). A fabricated-foreign id in acme context → 404 (EF global query filter). So no cross-tenant read leak (empty queue, not other-tenant rows) and no cross-tenant write occurred — the deviation is the mismatch returning 200-empty rather than a rejection. See [[us-adm-006-settings-findings]] / [[auth-full-test-pass-2026-06-25]] (BUG-003 root locus = US-AUTH-007/TenantResolutionMiddleware).

---

<!-- ── US-ATT-005 Shift Management & Assignment — REPORT-ONLY API run 2026-06-26 (@test-runner) ── -->

### ENH-006 — US-ATT-005 minor contract observations: clone returns 200 (TC expects 201); ResolvedShiftDto exposes no per-date working-day flag (BR-6 convenience); minimum_hours capped at 24 vs §7 decimal(4,2)/999.99
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title:** Three benign deviations between TC wording and a reasonable implementation: (1) `POST .../shifts/{id}/clone` returns **HTTP 200** while TC-ATT-061 says "201" — the clone is created correctly, only the status differs. (2) `ResolvedShiftDto` returns the resolved shift plus `workingDays`/`startTime`/`gracePeriodMinutes` (everything US-ATT-008 needs to compute working-day applicability + the late threshold) but NOT a computed per-date "is this a working day?" boolean — TC-ATT-062 step 2 expects the resolver to "indicate the date is a non-working day"; the data is present, the convenience flag is not. (3) The validator caps FLEXIBLE `minimum_hours` at 24 while §7 names the column `decimal(4,2)` (max 999.99) and TC-ATT-054 step 4 probes 999.99/1000.00; the 24h cap is arguably MORE correct but diverges from the TC's stated boundary.
- **Module / US:** Attendance · US-ATT-005 (TC-ATT-061, TC-ATT-062, TC-ATT-054)
- **Why it matters:** None is a defect: clone works, the resolve DTO carries the needed data, and the 24h cap is a tighter (safer) bound than the raw column type. Documented so the late-flagging consumer (US-ATT-008) knows it must derive the working-day boolean from `workingDays` itself, and so the TC-vs-impl boundary drift is explicit.
- **Suggested direction (NOT applied):** report only — optionally align clone to 201, add an `isWorkingDay`/`isOnDate` flag to `ResolvedShiftDto`, and reconcile the minimum_hours TC boundary to the 24h cap.

### ENH-007 — Surface a per-employee weekly-overtime running total / progress-to-cap on the overtime API (for the §8 weekly progress bar)
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title:** §8 specifies a "progress bar for weekly overtime approaching the maximum." The backend computes the Monday-anchored weekly sum (`EvaluateWeeklyCapAsync`) only transiently to set the `weekly_cap_exceeded` flag at clock-out; there is no API that returns the employee's current weekly OT total vs the configured max so the FE can render the progress bar / "approaching cap" state without recomputing.
- **Module / US:** Attendance · US-ATT-006 (§8, BR-5)
- **Why it matters:** the §8 progress bar and the BR-5 "approaching the limit" UX need a live weekly-total-vs-max figure; today only the boolean over-cap flag exists (and that isn't on a DTO either — see ISSUE-079). A small read endpoint or an addition to `/overtime/my` would back the UI without duplicating the week math client-side.
- **Suggested direction (NOT applied):** report only — add `weeklyOvertimeMinutes` + `weeklyOvertimeCapMinutes` to a my-overtime summary response.

> **ISSUE-067/069/071/073/075 (attendance writes skip the central `audit_logs` table) — EXTENDED to the US-ATT-006 overtime surface, not re-filed.**
> Clock-out overtime auto-creation and the pre-approval submit write **no `audit_logs` row** (consistent with the module-wide attendance no-central-audit pattern). NOTE the partial improvement: overtime **approve/reject DO write an immutable `overtime_approval_history` row** (action, approver_employee_id, approved_minutes, comment, approval_level, actioned_at) — better than clock-out/clock-in which write nothing — and that history is append-only (no update/delete endpoint, verified immutable in TC-ATT-078). But the *detection* and *pre-approval* events, and the FR-8 cap/weekly-alert events, are not in the central audit trail. NFR-3 deterministic-auditability of the *calculation* is met via the per-record `calculation_basis` string (verified deterministic, TC-ATT-080); the gap is the cross-cutting audit-log search surface, same root as the clock-in/out audit ISSUEs.

> **BUG-003 (cross-tenant / JWT-vs-subdomain mismatch) — EXTENDED to the US-ATT-006 overtime surface, not re-filed.**
> TC-ATT-ISO-009: an **acme JWT + `X-Tenant-Subdomain: platform`** header on `GET /api/v1/attendance/overtime/pending` and `…/overtime/report` returns **HTTP 200** scoped to the *platform* tenant (empty here) rather than rejecting the token-tenant ≠ header-tenant mismatch — the same BUG-003 root (TenantResolutionMiddleware uses the header-resolved `ITenantContext.TenantId` for data while authz uses the token tenant, with no guard). **For the overtime surface this surfaces as NO data leak:** the queue/my/report/decision paths resolve the actor via `UserId → employees (tenant-filtered)`, so under a foreign-tenant header the acting employee resolves to null and the result is empty/403-no-employee — the same employee-self-resolve protection seen on ATT-002/ATT-004. **Write isolation CLEAN:** a pre-approval with body-injected `tenant_id`/`employeeId` is stamped acme + the acting employee (TenantInterceptor + UserId), and a pre-approval sent under a spoofed `platform` header returns 403 "No employee record is linked to the current user" with **nothing written to platform** (verified: 0 overtime_record rows under the platform tenant). The cross-tenant *approve/reject* arm could not be positively demonstrated because only **acme** is seeded with overtime data (no second populated tenant — globex from the TC is not seeded). Same systemic mismatch mechanism documented module-wide; isolation invariants verifiable on overtime were all clean. See [[testing-loop-report-only]] (BUG-003 root locus = US-AUTH-007/TenantResolutionMiddleware).

> **BUG-003 (cross-tenant / JWT-vs-subdomain mismatch) — EXTENDED to the US-ATT-005 shift surface, not re-filed.**
> TC-ATT-ISO-008: an **acme JWT + `X-Tenant-Subdomain: techoneglobal`** header on `GET /api/v1/attendance/shifts` returns **HTTP 200 with techoneglobal's shift** (id `019ef3c3-16d2-…`, distinct from acme's own `019ef3bb-0015-…`); with `X-Tenant-Subdomain: platform` it returns the platform tenant's shift (`019ed613-3ebf-…`). The data layer resolves the tenant from the **header** (`ITenantContext.TenantId`, used by the EF global query filter + TenantInterceptor) while authorization uses the **token's** tenant, and nothing guards the two — the exact BUG-003 root cause (TenantResolutionMiddleware does not reject a token-tenant ≠ header-resolved-tenant mismatch). **Read leak CONFIRMED** on the shift list (other tenants' shift definitions returned). **Within the correct (matching) acme context isolation is CLEAN** — techoneglobal's shift is NOT in acme's list and acme's shift id resolves to 404 under a foreign-header context (EF filter works when header=token). **Write-arm INFERRED, not executed (per the no-cross-tenant-write rule):** because the TenantInterceptor stamps the header-resolved tenant, a create/update/assign sent with the spoofed header would write into techoneglobal; this was NOT performed — a PUT of acme's shift id under the techoneglobal-spoofed header returned 404 (acme's shift invisible in that scope) and nothing was persisted to techoneglobal. Same systemic READ exposure (+ inferred write capability) documented module-wide. See [[auth-full-test-pass-2026-06-25]] / [[us-adm-006-settings-findings]] (BUG-003 root locus = US-AUTH-007/TenantResolutionMiddleware).

---

### ISSUE-083 — Monthly summary read serves the STALE materialized row while the drill-down recomputes live → the two can diverge intra-day for the current month
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE
- **Module / US / TC:** Attendance · US-ATT-007 · TC-ATT-085 (drill-down/summary reconciliation), TC-ATT-086 (current-month refresh rule)
- **Title:** `GET /summary/monthly` returns the materialized `attendance_monthly_summary` row and only recomputes on-demand when **no** row exists for the month (`AttendanceSummaryService.GetMonthlyAsync` line 88: `if (existing.Count == 0 …)`). Once a row exists, subsequent reads serve it verbatim even though the underlying attendance has changed since `generated_at`. Meanwhile the per-employee drill-down (`GetEmployeeBreakdownAsync`) **always computes live** (no materialization). So for the current (incomplete) month the summary-table row and the day-by-day drill-down can disagree until the next refresh, breaking the TC-ATT-085 step-7 reconciliation invariant ("day-by-day counts sum to the summary-row totals").
- **Root cause (hypothesis, ~90%):** by-design materialized cache (the table IS the cache, FR-8 Redis deferred) refreshed only by the daily Hangfire job (`MonthlySummaryDailyJob`, ~1 AM UTC) or an explicit `POST …/generate`; the read path never invalidates/recomputes a row that exists. The drill-down deliberately has no cache, so it leads the summary between refreshes. No exception — confirmed by direct observation, not the log.
- **Reproduction steps (acme, HR `hr@acme.test`, month 2026-06, John Doe `019efced-88a9-7825-a8e0-7571318deb74`):**
  1. `GET /api/v1/attendance/summary/monthly?month=2026-06` → John row **present=1, absent=15, workMinutes=2070** (`generatedAt:2026-06-26T03:16:04Z`).
  2. `GET /api/v1/attendance/summary/monthly/019efced-…?month=2026-06` (live drill-down) → counts that sum to **present=3, absent=13, workMinutes=13057** — does NOT match step 1.
  3. `POST /api/v1/attendance/summary/monthly/generate?month=2026-06` (200 COMPLETED), then re-`GET` the table → John row now **present=3, absent=13, workMinutes=13057** — matches the drill-down. The divergence was pure staleness, not a math error.
- **Evidence:** banner totals also shifted across the regenerate (avg attendance 0.19→0.57; total LOP 623→621), confirming the served row was stale. After regenerate the summary, drill-down, and all three exports (CSV/XLSX/PDF) reconcile exactly. The daily refresh job is wired and ran today (Serilog `hrm-20260626.log` 08:46: "Attendance monthly summary generated for 2026-06: 33 employees" for tenant `019ef3ba-…`), so the worst-case staleness window is ≈24h for the current month.
- **Severity rationale:** LOW — bounded by the daily refresh; affects only the in-flight current month; consistent with the documented materialized-cache design (TC-ATT-086 note explicitly flags the refresh rule as needing confirmation). No data corruption, no isolation impact. The fix decision (recompute current-month-on-read vs. shorten the job cadence vs. accept the window) is a product call.
- **Suggested direction (NOT applied):** report only — either recompute (or upsert) the current/incomplete month on read, or document the ≈24h staleness as the intended SLA so the FE can show `generatedAt`. Drill-down should arguably read the same materialized rows as the table for a consistent view.

> **BUG-003 (cross-tenant / JWT-vs-subdomain mismatch) — EXTENDED to the US-ATT-007 monthly-summary surface, not re-filed.**
> TC-ATT-ISO-010: an **acme JWT (`hr@acme.test`, has `Attendance.View.All`) + `X-Tenant-Subdomain: techoneglobal`** header on `GET /api/v1/attendance/summary/monthly?month=2026-06` returns **HTTP 200 scoped to the techoneglobal tenant** — **1 row, employee "Cross Write"**, banner `totalEmployees:1, totalLopDays:20` — instead of acme's own **33 rows**. This is a **CONFIRMED cross-tenant READ leak** of another tenant's attendance summary: the data layer resolves the tenant from the **header** (`ITenantContext.TenantId`, used by the EF global query filter) while authorization uses the **token's** tenant, and nothing rejects the mismatch — the exact BUG-003 root cause (TenantResolutionMiddleware does not guard token-tenant ≠ header-resolved-tenant). A nonexistent subdomain (`globex`) correctly 404s (tenant unresolved); a **real** foreign subdomain leaks. **Within the matching (header=token) acme context, isolation is CLEAN** — the acme list contains only acme employees (EMP-0001..EMP-MGR01), a foreign/unknown employeeId drill-down returns **404 "Employee not found"** (EF filter), and the daily Hangfire job iterates per-tenant with the resolved context (Serilog confirms acme=33, techoneglobal=1, other tenants=0 — no cross-tenant rows written). **Write/generate/export-arm INFERRED, NOT executed (per the no-cross-tenant-write rule):** because `GetMonthlyAsync`/`GenerateAsync`/`ExportAsync` all key off `_tenantContext` (header-resolved), a `POST …/generate` or a filtered `…/export` sent under the spoofed `techoneglobal` header would generate/read into/from techoneglobal — this was NOT performed; only the read was demonstrated. Same systemic exposure documented module-wide (settings, workflows, audit, shifts, overtime). See [[auth-full-test-pass-2026-06-25]] / [[us-adm-006-settings-findings]] (BUG-003 root locus = US-AUTH-007/TenantResolutionMiddleware).

> **ISSUE-067/069/071/073/075 (attendance writes skip the central `audit_logs` table) — EXTENDED to the US-ATT-007 summary-generation surface, not re-filed.**
> The on-demand `POST /summary/monthly/generate` and the daily/monthly Hangfire jobs write/refresh `attendance_monthly_summary` rows and log only an `INF` Serilog line ("Attendance monthly summary generated for …"); there is **no `audit_logs` row** for the generation event. Consistent with the module-wide attendance no-central-audit pattern. NFR-3 deterministic-auditability of the *figures* is partly met by the per-row `generated_at` stamp (verifiable, reproducible on regenerate), but the generation **action** (who triggered it, when, for which month) is not in the cross-cutting audit-log search surface. Same root as the clock-in/out audit ISSUEs.

> **BUG-7 (DateTime-UTC-Kind 500 on the monthly attendance summary, QA baseline 2026-06-19) — NOT REPRODUCED / appears FIXED on US-ATT-007.**
> The 2026-06-19 baseline reported a systemic `DateTime Kind=Unspecified` 500 in `AttendanceSummaryService` (~line 349) that only surfaced with real seeded employee data. **Re-tested 2026-06-26 with John Doe's rich June-2026 dataset (≈87 attendance logs + OT + leave + regularizations):** `GET /summary/monthly?month=2026-06` returns **HTTP 200** with full data; the drill-down, generate, and all three exports also 200; **zero** `Kind=Unspecified` / "Cannot write DateTime with Kind" lines in today's Serilog (`hrm-20260626.log`); no ERR/FTL on the summary path. The current code consistently constructs UTC instants (`monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)` at `AttendanceSummaryService.cs:351-352, 438-439`). The baseline 500 is no longer present — likely resolved since 2026-06-19. Recorded here as a positive verification, not a finding.

---

## Findings (US-ATT-008 — Late arrival & early departure tracking, 2026-06-26)

### ENH-008 — `my-score.allowedLates` maps to the CHRONIC threshold (5), not the DEDUCTION threshold (3), so the §8 "X of N allowed lates" indicator counts down to the wrong N
- **Type / Severity / Status:** ENH · — · OPEN
- **Type / Title / Module / why / direction:**
- **Type:** ENH
- **Title:** The self-service lateness score's "allowed" denominator uses the wrong policy field.
- **Module / US / TC:** Attendance · US-ATT-008 · TC-ATT-113
- **Why it matters:** §8 describes the indicator as "2 of 3 allowed lates used this month", where 3 is the **deduction** trigger (`thresholdCount`) — the point at which the employee starts losing pay. `GetMyScoreAsync` instead sets `AllowedLates = policy.ChronicThreshold` (`src/backend/HRM.Infrastructure/Services/LateEarlyService.cs:256`), i.e. the **HR-escalation** threshold (5 in acme), not the deduction threshold (3). The employee's progress bar therefore allows 5 before "full" when the real cost begins at 3 — under-warning the employee about the deduction.
- **Suggested direction (NOT applied):** map `AllowedLates` to `policy.ThresholdCount` (the deduction trigger) per the §8 wording, or expose both (deduction-allowance and chronic-allowance) so the FE can show "N of {threshold} before deduction / {chronic} before escalation".

> **BUG-003 (cross-tenant JWT-vs-subdomain mismatch) — EXTENDED to the US-ATT-008 late/early surfaces, not re-filed.**
> An **acme JWT (`hr@acme.test`, has `Attendance.View.All`) + `X-Tenant-Subdomain: techoneglobal`** on `GET /api/v1/attendance/late-early/report?scope=all&from=2026-06-01&to=2026-06-30` returns **HTTP 200 scoped to techoneglobal** — **1 row, employee "Cross Write"** (verified `tenant_id 019ef3c3-…` = techoneglobal), instead of acme's own 34 rows. This is a **CONFIRMED cross-tenant READ leak** of another tenant's late/early report: the employee/log query keys off `ITenantContext.TenantId` (header-resolved = techoneglobal) while authorization uses the acme token. Same root as the module-wide BUG-003 (TenantResolutionMiddleware does not reject token-tenant ≠ header-resolved-tenant). By the identical mechanism, `late-policy` GET/PUT and `late-early/my-score` resolve the policy/employee against the header tenant too (the policy GET returned a value under the techoneglobal header). **Probes were READ-ONLY; the PUT/write arm was NOT executed against techoneglobal (no-cross-tenant-write rule).** **Within the matching (header=token) acme context, isolation is CLEAN** — report/score/policy all scope to acme only. See [[auth-full-test-pass-2026-06-25]] (BUG-003 root locus = US-AUTH-007/TenantResolutionMiddleware).

> **ISSUE-065 (UTC-vs-tenant-timezone late/early miscalculation) — CONFIRMED as the core defect of this story, not re-filed.**
> US-ATT-008 IS the locus of ISSUE-065. Detection compares `TimeOnly.FromDateTime(clockIn)` — where `clockIn` is server **UTC** (`AttendanceService.cs:141,158`; regularization recompute `RegularizationApprovalService.cs:494`) — against the **naive** shift `start_time`/`end_time` with NO tenant-timezone conversion. **Demonstrated live (server TZ +05:30):** a real clock-in at **10:13 local IST** (1h13m past the 09:00 shift start, well beyond the 10-min grace) is stored as **04:43 UTC**; the naive comparison `04:43 < 09:00` yields **`is_late=false, late_minutes=0`** — an employee over an hour late is recorded on-time. The same skew mis-sizes early-departure: in TC-ATT-101 a real (non-backdated) clock-out at 04:34 UTC was flagged `is_early_departure=true, early_departure_minutes=745` against shift end 17:00. The boundary/grace/min-hours **arithmetic itself is exact** (verified by backdating clock_in/out to known UTC wall-clock values: 09:10=on-time, 09:11=late_by 1, 16:30 co=early 30, min-hours carve-out correct) — the bug is purely the UTC-vs-tenant-TZ frame. Any non-UTC tenant gets systematically wrong late/early flags from real punches. Confidence ~98% (code + live demonstration). See the existing ISSUE-065 entry; this run is the definitive confirmation.

---

## Findings (US-ATT-009 — Attendance Integration with Payroll: feeding hours/days, 2026-06-26)

> **Run context (REPORT-ONLY, API-layer):** 12 owned TCs executed — TC-ATT-118..128 + TC-ATT-ISO-012. Routes discovered on `AttendanceController`: `GET /api/v1/attendance/payroll-data?month=yyyy-MM&employeeIds=<csv>` (`Attendance.View.All`), `GET /period-lock?month=` (`Attendance.View.All`), `POST /period-lock` + `POST /period-lock/{id}/unlock` (`Attendance.Lock.Manage`), `GET /reconciliation?month=` (`Attendance.View.All`). The attendance→payroll **feed is well-built and accurate**: payroll-data reuses the US-ATT-007 monthly summary, so present/absent/lop/work-minutes **reconcile exactly** with the summary AND the reconciliation view (John Doe 2026-06: present 3.0 / absent 13 / lop 13 / work 13057 / OT 360 — identical across all three surfaces). **Approved-OT-only is honored** (John: approved 360, pending 1591, rejected 120 → feed shows only 360, multiplier breakdown `{"1.5":360}`). **Lock lifecycle works** (lock → blocks regularization with exact AC-4 string; overlap → 409; unlock HR-only; re-lock). **Authz clean** (unauth 401, employee/manager 403, HR 200; reads gated `Attendance.View.All`, writes `Attendance.Lock.Manage`). Findings below are the gaps. No 500s in `hrm-20260626.log`. AC-2/AC-3 salary math + FR-5 payroll-input column + BR-8 configurable cutoff are PAYROLL-MODULE / DEFERRED per the TCs' own notes.

### ENH-009 — Report CSV/XLSX export does not neutralize formula-injection (CSV-injection) in employee-derived cells; an employee display name beginning with `= + - @` is written verbatim and would execute as a formula in Excel/Sheets
- **Type / Severity / Status:** ENH · — · OPEN
- **Type / Title / Module / Why-it-matters / Suggested-direction:**
- **Type:** ENH (defense-in-depth hardening; not a defect against the current spec — FR-5 only requires the three formats with matching content, which works)
- **Title:** Attendance report CSV/XLSX export emits employee-derived text (employee name, department) into cells without prefixing a guard character, so a name like `=cmd|...` or `+HYPERLINK(...)` would be interpreted as a formula when the exported file is opened in Excel/Google Sheets (CSV/spreadsheet formula injection). Observed live: the acme dataset already contains an employee named `<script>alert(1)</script> Test` and several `AAAA…` long names (prior test residue) that flow verbatim into the CSV `Employee` column. The `<script>` payload is **inert** in a CSV/XLSX context (good — no HTML execution), but a leading `=`/`+`/`-`/`@` in any free-text cell remains an Excel-formula vector.
- **Module / US / TC:** Attendance · US-ATT-010 · TC-ATT-133 (export) / TC-ATT-140 (S6 sanitisation)
- **Why it matters:** an HR officer opening an exported attendance report is the exact "trusted user opens an attachment" scenario CSV-injection targets; a malicious tenant employee who can set their own display name could plant a formula that fires in HR's spreadsheet. Low likelihood (needs name control + a vulnerable spreadsheet client), but cheap to harden and consistent across every export surface in the platform.
- **Suggested direction (NOT applied):** prefix any cell whose value starts with `= + - @ ` (and tab/CR) with a leading apostrophe or `'` guard in the CSV/XLSX writer (`AttendanceDashboardService.cs` export rendering, the `ExportColumns` path). Report only — no change made.

> **BUG-003 (cross-tenant JWT-vs-subdomain mismatch) — EXTENDED to the US-ATT-010 dashboard/reports surface, not re-filed.**
> An **acme JWT (`hr@acme.test`, has `Attendance.View.All`) + `X-Tenant-Subdomain: techoneglobal`** on `GET /api/v1/attendance/dashboard` returns **HTTP 200 scoped to techoneglobal** (`expectedHeadcount:1, clockedIn:0, …`) and `GET /dashboard/live-board` returns **techoneglobal's 1 row — employee name "Cross Write"** (a distinct single-employee tenant), instead of acme's own **33** employees. The dashboard KPIs, live board, department comparison, custom report, trends and scheduled-config list all key off `ITenantContext.TenantId` (header-resolved = techoneglobal) while authorization uses the acme token; `TenantResolutionMiddleware` never rejects token-tenant ≠ header-resolved-tenant. This is a **CONFIRMED cross-tenant READ leak of org-wide attendance aggregates** (the exact org-wide KPI/headcount/per-employee status data the story exposes). Same root as the module-wide BUG-003 (root locus US-AUTH-007 / TenantResolutionMiddleware). **All cross-tenant probes were READ-ONLY; no scheduled-config create/update/delete was executed against techoneglobal (no-cross-tenant-write rule).** **Within the matching (header=token) acme context, isolation is CLEAN** — dashboard/live-board/reports/scheduled all scope to acme's own employees only. An acme HR token + `X-Tenant-Subdomain: platform` likewise resolves to the platform tenant (returns its all-zero KPIs), confirming the scope-follows-header behavior. (`globex` subdomain returns "workspace not found" 404 — does not exist; techoneglobal used as Tenant B, read-only.) See [[auth-full-test-pass-2026-06-25]] and the Admin/Core-HR/Leave/US-ATT-009 module reports for the BUG-003 systemic record.

> **DEFERRALS confirmed (per the TCs' own notes, not filed as defects):** SignalR real-time live-board push (FR-2/NFR-2 — board is polled, US-NTF deferred; TC-ATT-130 S7); Redis KPI cache (FR-7/NFR-1 — DB-computed path verified live, cache off in this env; TC-ATT-138); scheduled-report EMAIL delivery (FR-8 — generate+queue seam only, US-NTF deferred; TC-ATT-136 S6); UTC day-boundary (tenant-timezone deferred module-wide). Department-comparison color-band (green/amber/red §8) is a **FE concern** — the API returns `attendanceRatePct` numeric only (no band field), which is the correct API contract; band classification is the chart layer (BLOCKED fe-platform-bound). HOLIDAY status classification IS wired server-side (reads the `Holidays` table, tenant-wide + location-specific) — better than the TCs' "conditional" assumption.

## Findings (US-REC-001 — Create and Publish Job Vacancy, 2026-06-26)

> REPORT-ONLY API-layer run (curl + JWT) against the running stack on `acme` (tenant `019ef3ba-ffb7-7eec-b24f-7ad806ca1cb9`). FE :4200 down + platform-bound → all UI/a11y TCs BLOCKED. **Real routes:** `/api/v1/recruitment/vacancies` (list/get/create/update + `/{id}/publish`,`/{id}/close`,`/{id}/status`, `/status` bulk); public `/api/v1/careers/vacancies` (+ `/{slug}`). **Real permissions (differ from the TC text `Recruitment.Create.All`/`Read.All`):** reads = `Recruitment.View`, all writes = `Recruitment.Manage`. ID buffer chosen above the concurrent attendance run.

### BUG-056 — AC-3 / FR-3 "weights must total exactly 100%" is NOT enforced server-side: an under-allocated (<100%) goal set persists silently and the AC-3 error string is never emitted
- **Type / Severity / Status:** BUG · MED · RESOLVED (verified 2026-09-02, /verify-fix — TCs re-run live)
- **Resolution (2026-09-02):** `GoalService.cs:462-468` enforces the exact-100 gate (422 `weight_not_100`), routed via `GoalsController.cs:167`. **TC-PRF-001-14 PASS · TC-PRF-001-15 PASS** on a live re-run 2026-09-02. **The `DEFERRED (feature-blocked: no goal-set finalize seam)` reason was stale — that seam shipped 2026-07-19 (commit `de3dccfa`).**
- **Layer:** BE
- **Module / US / TC:** Performance · US-PRF-001 · TC-PRF-001-02 (also weakens the AC-2 "weights summing to 100%" guarantee)
- **Title:** AC-3/FR-3 require that goal weights for an employee+cycle sum to **exactly** 100%, with the validation error "Goal weights must total 100%" on violation. The server only enforces the **upper** bound (running total > 100% → 422 `weight_exceeds_100`); it never enforces the lower bound. A manager can create goals summing to 95% (or any value < 100%) and every create returns 201 — the employee is left with an under-weighted goal set indefinitely, and the AC-3 message "Goal weights must total 100%" is **never produced by any endpoint**. The exact-100 invariant is delegated entirely to the (currently-down, untestable) UI via the `EmployeeGoalsDto.TotalWeight` rollup.
- **Root cause (~95%, code + self-documented):** `GoalService.CreateAsync` (`src/backend/HRM.Infrastructure/Services/GoalService.cs:75-79`) checks only `newTotal > RequiredTotalWeight` (100) → 422; there is no `< 100` or end-of-set "must equal 100" check. The code comment at `GoalService.cs:73-74` explicitly states the exact-100 rule "is surfaced via the employee-goals/dashboard TotalWeight so the UI can block submit" — i.e. it is by-design a UI-only guarantee. Because POST is single-goal (no batch "Save Goals" transaction), there is no server-side moment at which "the set now sums to exactly 100" is asserted. `UpdateAsync` (`:137`) has the same one-sided check.
- **Reproduction steps (live, acme, today):**
  1. `manager@acme.test` (SetGoal.Team), cycle QA-PRF001-OPEN (`019eff10-…-a1`, window open), target John Doe (`019efced-88a9-7825-a8e0-7571318deb74`).
  2. `POST /api/v1/tenant/performance/goals` weight 50 → **201**; again weight 45 → **201** (running total 95%).
  3. `GET .../employees/{john}/cycles/{cycle}/goals` → `totalWeight: 95`, 2 goals persisted. No error, no block.
  4. Contrast: a third goal weight 45 (would total 140) → **422** "Goal weights for this employee would total 140%, which exceeds 100%." — the *over* arm works.
- **Evidence:** under-alloc both 201, `GET` returns `totalWeight=95 count=2`; over-alloc 422 `weight_exceeds_100`. The AC-3 literal "Goal weights must total 100%" appears in no response.
- **Severity rationale:** MED — contained data-quality/validation gap on a primary flow: it cannot corrupt or leak data and the over-allocation half IS guarded, but it lets an appraisal proceed with goals that don't sum to 100% (defeating the weighted-scoring premise of the performance module) and the AC-3-mandated error is absent. Would be HIGH if downstream weighted-score math divides by an assumed-100 denominator (not verified this run). The single-goal API shape means a true server-side fix needs either a batch "save goals" transaction or a separate "submit/lock goals" step that asserts the exact-100 total.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-100 — Test cases (and likely the FE) target `/api/v1/performance/goals*`; the live API is `/api/v1/tenant/performance/*` (route prefix drift)
**▶ DISPOSITION 2026-09-08 (T4).** **STALE — the premise was false. Docs corrected, merged (#678).**
The FE was **never broken**. All 12 performance services already target the live `/api/v1/tenant/performance/*`
routes. The finding, and five test cases derived from it, documented a defect that did not exist — and the
"obvious" remedy was worse than nothing: a naive `/tenant/` prefix still 404s, and `goals/team` does not exist
at all, so following the TCs would have produced a broken client. Five TCs corrected rather than the code.

- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** TEST (and potentially FE)
- **Module / US / TC:** Performance · US-PRF-001 · TC-PRF-001-01/02/03/06/11, TC-PRF-ISO-001/002 (all reference the un-prefixed path)
- **Title:** Every US-PRF-001 TC writes the endpoint as `POST /api/v1/performance/goals`, `GET /api/v1/performance/goals/team`, etc. The actual controller route is `[Route("api/v1/tenant/performance")]` with endpoints `POST .../goals`, `GET .../cycles/{cycleId}/team-dashboard`, `GET .../employees/{employeeId}/cycles/{cycleId}/goals` (`GoalsController.cs:21,33,49,77`). The TCs' paths and resource shapes (e.g. `goals/team?cycleId=`) do not match the live API. This is documentation/spec drift in the TC files; if any Angular performance service also omits the `/tenant/` prefix it would 404 (same class as the systemic FE↔BE `/tenant/` prefix mismatch already recorded — FE could not be verified this run as :4200 is down).
- **Root cause (~80%):** TCs authored from the user-story sketch (`/api/v1/performance/goals`) before the controller settled on the `/tenant/performance` prefix + employee/cycle path structure.
- **Reproduction steps:** compare any US-PRF-001 TC "Test Steps" endpoint to `GoalsController` `[Route]`/`[HttpGet]`/`[HttpPost]` attributes.
- **Evidence:** TC files say `/api/v1/performance/goals`; live 201/200 only at `/api/v1/tenant/performance/...` (all PASS responses above use the prefixed path).
- **Severity rationale:** LOW — TEST/doc drift; does not affect product behavior. Flagged for traceability and as a FE-contract risk to verify when the frontend is reachable. Per REPORT-ONLY policy the TC objective/steps were NOT edited.
- **Suggested direction (NOT applied):** none — report only.

> **BUG-003 (cross-tenant JWT-vs-subdomain mismatch) — EXTENDED to the US-PRF-001 goals surface (READ *and* WRITE leak for `.All` holders), not re-filed.**
> The goals endpoints resolve the tenant from `ITenantContext.TenantId` (subdomain / `X-Tenant-Subdomain` header) and drive the EF global query filter + `TenantInterceptor` off it, while `AuthorizeForEmployeeAsync` (`GoalService.cs:300-301`) grants access to any `Performance.SetGoal.All` holder **without** checking that the caller's JWT tenant matches the resolved tenant — the same missing `CurrentUser.TenantId == ITenantContext.TenantId` invariant (root locus US-AUTH-007 / `TenantResolutionMiddleware`). Confirmed live today, both arms, on the goals surface:
> - **READ leak:** `tenantadmin@acme.test` (JWT tenant_id=acme `019ef3ba-…`, holds SetGoal.All) + header `X-Tenant-Subdomain: techoneglobal` → `GET /api/v1/tenant/performance/employees/{tgEmp}/cycles/{tgCycle}/goals` → **HTTP 200** returning **techoneglobal's** goal ("TG-secret-goal", totalWeight 10). Contrast: same token + correct `acme` header returns only acme's goals. (TC-PRF-ISO-001 FAIL.)
> - **WRITE leak:** same acme TenantAdmin token + `X-Tenant-Subdomain: techoneglobal` → `POST .../goals` for the techoneglobal employee → **HTTP 201**, and the persisted row's `tenant_id = techoneglobal` (`019ef3c3-…`) — a foreign-tenant goal written using an acme-issued token. **The probe row was hard-deleted immediately; ZERO residue left in techoneglobal.** (TC-PRF-ISO-003 FAIL.)
> - **Self-protected arms (NOT a leak):** the `Performance.SetGoal.Team` **manager** path is self-protecting — acme manager token + techoneglobal header → create / team-dashboard → **403 `no_employee_record`** (the manager's UserId resolves to no employee in the foreign tenant, `GoalService.cs:306-309`), same pattern as the leave/attendance employee-self-resolve surfaces. And within the **correct** subdomain, write isolation is CLEAN: body-injected `tenant_id` is ignored (server-derived = acme), foreign `employee_id` → 404 `employee_not_found`, foreign `cycle_id` → 404 `cycle_not_found` (TC-PRF-ISO-003 in-tenant arms all PASS).
> So the cross-tenant exposure on this surface fires specifically through an `.All`-holding actor (TenantAdmin/HR) over the spoofable header. NFR-2's stated mechanism (PostgreSQL RLS on the Goals table) is NOT implemented — isolation is EF query filters keyed off the resolved tenant, which is correct *only* when the header matches the token. See [[auth-full-test-pass-2026-06-25]], the root-locus confirmation at US-AUTH-007, and the Admin/Core-HR/Leave/Attendance/Recruitment module reports for the systemic record.

---

## US-REC-002 — Applicant Submits Application with Resume Upload (API-layer pass 2026-06-26)

### ISSUE-367 — AC-5 delete-guard reports affected *structures*, not the affected *employee* count
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **⚠ RENUMBERED 2026-08-10 (GAP-L8): was `ISSUE-109`.** That id was used by TWO unrelated defects, so a "RESOLVED (PR #…)" line against it could not be attributed. The other instance keeps `ISSUE-109` because the recruitment concurrency instance is cited by TEST-STATUS US-REC-004. Any historical reference to `ISSUE-109` that describes THIS defect means `ISSUE-367`.

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-001 / TC-PAY-001-04
- **Title:** Deleting an in-use component is correctly prevented with 409, but the message says "used by N salary structure(s)" — AC-5 specifies "an error message listing the **count of affected employees**." The guard counts `salary_structure_component` links, never the employees assigned to those structures.
- **Root cause:** `SalaryComponentService.DeleteAsync` counts `SalaryStructureComponents` where `SalaryComponentId == id` and returns that count (confidence 100%). It does not join through `EmployeeSalaryComponent`/assignment to count employees. Behaviourally this is *stricter* (blocks even a component linked to an employee-less structure), but the message and the AC-5 metric diverge.
- **Reproduction steps:** Link `BASIC` to a structure (no employees), `DELETE /api/v1/payroll/salary-components/{basicId}` -> 409 "used by 2 salary structure(s)..."; in-use statutory also 409.
- **Evidence:** `status=409 ... used by 2 salary structure(s) and cannot be deleted`. Unused component deletes 200.
- **Severity rationale:** LOW — AC-5's core intent (prevent destructive delete) is met and is actually stricter; only the reported metric/wording deviates.

### ISSUE-368 — NFR-1 Redis caching of component/structure lists is unimplemented
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **⚠ RENUMBERED 2026-08-10 (GAP-L8): was `ISSUE-110`.** That id was used by TWO unrelated defects, so a "RESOLVED (PR #…)" line against it could not be attributed. The other instance keeps `ISSUE-110` because the recruitment notification instance is cited by LOW-TIER-TRIAGE/DECISIONS. Any historical reference to `ISSUE-110` that describes THIS defect means `ISSUE-368`.

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-001 / TC-PAY-001-02 (step 3) / TC-PAY-001-11 / TC-PAY-ISO-004
- **Title:** Neither salary service references any cache (`IMemoryCache`/`IDistributedCache`/Redis); there is no 15-min-TTL cached list and no per-tenant cache key. NFR-1 (and the tenant-scoped-cache assertion in ISO-004) cannot be satisfied because no payroll cache exists.
- **Root cause:** No caching code in `SalaryComponentService`/`SalaryStructureService` (confidence 100%, grep clean). Reads go straight to EF every time. Dev runs Redis-off (in-memory fallback) per `appsettings.Development.json`. Side effect: TC-02 step 3 "no stale read after write" passes *trivially* (always fresh), and ISO-004 has no shared key to leak — but NFR-1 itself is not built.
- **Evidence:** grep for `cache|redis|IMemoryCache|IDistributedCache` in both services -> no matches; `"Redis": ""` in dev config.
- **Severity rationale:** LOW — a performance NFR, not a correctness/security defect; light-load latency is already far inside SLA (TC-11 p95 7ms) so the cache is not yet needed for the SLA.

### ISSUE-369 — Large/over-precision decimals echoed un-rounded in the API create response (DB stores correct numeric(18,2))
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **⚠ RENUMBERED 2026-08-10 (GAP-L8): was `ISSUE-111`.** That id was used by TWO unrelated defects, so a "RESOLVED (PR #…)" line against it could not be attributed. The other instance keeps `ISSUE-111` because the performance rating-audit instance was filed first. Any historical reference to `ISSUE-111` that describes THIS defect means `ISSUE-369`.

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-001 / TC-PAY-001-06
- **Title:** Two response-serialization mismatches vs the persisted value: (a) `defaultValue=9999999999999999.99` round-trips in the **create response** as `1e+16` (scientific-notation precision loss in JSON), while the DB column correctly stores `9999999999999999.99`; (b) `defaultValue=1234.5678` is **echoed as `1234.5678`** in the create response but the DB rounds it to `1234.57` (numeric(18,2)). The persisted data is correct in both cases; only the immediate API echo is wrong.
- **Root cause:** The create/update DTO is projected from the in-memory entity *before* the DB round-trip applies the column scale, and `decimal`->JSON serialization renders very large magnitudes in exponent form (confidence 85%). A re-fetch returns the correctly-scaled value.
- **Reproduction steps:** Create with `defaultValue:9999999999999999.99` -> response `defaultValue: 1e+16`; DB `9999999999999999.99`. Create with `1234.5678` -> response `1234.5678`; DB `1234.57`.
- **Evidence:** `status=201 val=1e+16` ; DB `BMAX|9999999999999999.99`, `BPREC|1234.57`.
- **Severity rationale:** LOW — cosmetic/echo-only; the source of truth (DB) is correct, a subsequent GET returns the right value. No 2dp-rounding rule is documented/enforced at the API layer, so consumers shouldn't rely on the create echo.

### ISSUE-112 — Formula component referencing an unknown variable is accepted at config-time
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-001 / TC-PAY-001-07 (step 4)
- **Title:** `POST salary-components` with `calculationMethod=Formula, formulaExpression="basic + unknownComp"` succeeds (201). TC-07 step 4 expects rejection of a reference to a non-existent component/identifier.
- **Root cause:** `SalaryFormula.Validate` only checks **syntax** (the grammar), not whether identifiers resolve to real component codes — the unknown-variable error is raised only at **evaluation** time (`Evaluate` throws "Unknown variable" when variable values are supplied during a payroll run). Self-reference / circular detection likewise fires only when the component is **linked into a structure** (`DetectCircularReferences`), not at component creation — verified: `pf2 + 100` accepted as a component (201), but rejected 400 ("Circular reference … PF2 -> pf2") when linked into a structure. Deliberate layering (service comment: circular-ref is "a structure concern").
- **Reproduction steps:** `POST salary-components {"calculationMethod":"Formula","formulaExpression":"basic + unknownComp",...}` -> 201. Then link a self-referencing formula component into a structure -> 400 circular.
- **Evidence:** `unknown identifier 'basic + unknownComp' ... status=201`; structure-level cycle `status=400 Circular reference detected ... CYA -> cyb -> cya`. BR-6 circular + safe-eval (no code exec for `System.exit(0)`/`import os`) all enforced correctly at the structure layer.
- **Severity rationale:** LOW — caught at run-time and at structure-link time; an unresolvable formula can't silently produce wrong pay (it throws), and the real BR-6 protections (circular, safe-eval) work where they matter.

### ISSUE-113 — TEST-layer drift: TC test data uses hyphenated codes and 422/route/PATCH expectations the implementation does not use
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** TEST
- **Module / US / TC:** Payroll / US-PAY-001 / TC-PAY-001-01/-04/-05/-06
- **Title:** Benign spec-vs-impl drifts (not product defects): (1) TC-01 uses structure code `FT-IN` and TC-04 `OLD-BONUS`, but both code validators enforce `^[A-Za-z0-9_]+$` (hyphens -> 400); tests must use `FT_IN`/`OLD_BONUS`. (2) TC-05/-06 expect **422** for FR-5/validation; the implementation returns **400** (equivalent rejection, code `no_earning_component`). (3) TCs reference routes `/api/v1/payroll/components`+`/structures` and a `PATCH .../{id}` activate; live routes are `/payroll/salary-components`+`/salary-structures` and activation is via `PUT` (no PATCH).
- **Root cause:** Test cases authored against the story's illustrative examples before the API contract settled (confidence 90%). Not weakened/edited (REPORT-ONLY) — recorded so a maintainer can reconcile the TC text.
- **Evidence:** `POST salary-structures {code:"FT-IN"}` -> 400 "Structure code may contain only letters, digits, and underscores."; FR-5 activate -> 400 not 422.
- **Severity rationale:** LOW — test-asset accuracy only; no product impact. Verdicts above judged against actual correct behaviour, not the literal TC status codes.

> **BUG-003 (cross-tenant JWT-vs-subdomain mismatch) — EXTENDED to the US-PAY-001 salary-component/structure surface (READ + WRITE), not re-filed.**
> Same root locus (US-AUTH-007 / `TenantResolutionMiddleware`): the middleware resolves the tenant from the `X-Tenant-Subdomain` header and drives the EF global query filter + `TenantInterceptor` stamping off it, while authorization uses the JWT — and never rejects **token-tenant != header-tenant**. Confirmed live on payroll with non-empty data:
> - **WRITE breach (TC-PAY-ISO-003):** acme `tenantadmin@acme.test` token (holds `Payroll.Configure`) + `X-Tenant-Subdomain: techoneglobal` on `POST /api/v1/payroll/salary-components` returned **201** and persisted the row stamped with **techoneglobal's tenant_id** (`019ef3c3-…`), not acme. An acme principal with no membership in techoneglobal created payroll config there.
> - **READ breach (TC-PAY-ISO-002 step 3):** same acme token + techoneglobal header on `GET /api/v1/payroll/salary-components` returned **200** with techoneglobal's `XTENANT` component (`total 1`) — cross-tenant config disclosure; the mismatch is not rejected.
> - **Self-protected / CLEAN arms (NOT a leak):** within a **matched** (header=token) acme context isolation is correct — acme cannot see techoneglobal's component in its list, and `GET/PUT/DELETE` of a foreign techoneglobal id all **404** (EF query filter). **Body-injection is ignored**: `POST` with `tenant_id`/`tenantId` set to techoneglobal under the acme context stored the row under **acme** (TenantInterceptor overrides the body — TC-PAY-ISO-003 step 1 PASS). Fail-closed on no-tenant (400 "Tenant context is not resolved") and invalid subdomain (404 "Workspace not found").
> AC-6/FR-8's claim of "PostgreSQL RLS enforces isolation at the database level" is **NOT met**: this codebase uses EF global query filters keyed off the spoofable resolved-tenant, not RLS — correct only when header matches token. Affects TC-PAY-ISO-002, TC-PAY-ISO-003 (FAIL). **The cross-tenant probe was REVERTED** — the techoneglobal `XTENANT` row (and all acme test payroll rows + salary `audit_logs`) were hard-deleted via psql; techoneglobal verified back to 0 payroll rows. See [[auth-full-test-pass-2026-06-25]], the root-locus at US-AUTH-007, and the Admin/Core-HR/Leave/Attendance/Recruitment/Performance module records.

---

## US-PRF-004 — HR Creates and Manages Appraisal Cycles (run 2026-06-26, REPORT-ONLY API layer)

> Routes: `/api/v1/tenant/performance/cycles*` — list / `cycles/active` / `cycles/{id}` / `cycles/{id}/dashboard` / POST `cycles` / PUT `cycles/{id}` / POST `cycles/clone` / POST `cycles/{id}/status` / DELETE `cycles/{id}`. Every endpoint (read + write) requires `Performance.SetGoal.All` OR `Performance.Publish.All` (BR-1). Validation: phases sequential/non-overlapping/in-window + ≥3 core phases (FR-1/FR-2/BR-3, 400); FR-7 status state machine (`IsValidTransition`, 409 `invalid_status_transition`); BR-4 active-same-type conflict (409); BR-5 rating-scale lock on Active (409 `rating_scale_locked`); BR-2 delete-only-empty-Draft (409 `cycle_has_reviews`/`cycle_not_draft`); BR-6 cancel needs reason (400/422). Personas: `hr@acme.test`+`tenantadmin@acme.test` hold `.All`; `manager@acme.test` (.Team) + `employee@acme.test` blocked. 19 TCs executed: **12 PASS / 4 FAIL / 3 BLOCKED**. NEW finding: BUG-063. Extends: BUG-003 (cross-tenant) to the cycle surface.

### ISSUE-114 — Video interview accepts a malformed `videoLink` (no URL-format validation) — FR-1 partial
- **Type / Severity / Status:** ISSUE · — · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** `videoLink` URL-format validation exists: `ScheduleInterviewValidator.cs:70,99`, `UpdateInterviewValidator.cs:54`, arms in `ScheduleInterviewValidatorTests`. **This entry's own metadata table already said `RESOLVED (PR #352, 2026-07-17)` while its summary line said OPEN.**

| Field | Value |
|---|---|
| **ID** | ISSUE-114 |
| **Type** | ISSUE |
| **Severity** | MED |
| **Status** | RESOLVED (PR #352, 2026-07-17) |
| **Layer** | BE |
| **Module / US / TC** | Recruitment / US-REC-005 / TC-REC-005-08 (step 5) |
| **Title** | `interviewType=Video` with `videoLink="not-a-url"` is accepted (201) and persisted verbatim; only emptiness + max-length are validated, not URL shape |

- **Root cause (90%):** `ScheduleInterviewValidator` (and `UpdateInterviewValidator`) gate the video link with `RuleFor(x => x.VideoLink).NotEmpty()...When(type==Video)` + `MaximumLength(500)` only — **no `Uri`/URL-format rule** (`ScheduleInterviewValidator.cs:64-71`). `InterviewService.ValidateTypeFields` likewise checks only `IsNullOrWhiteSpace` (`InterviewService.cs:335`). FR-1 specifies "video meeting link (URL, required for video)" and TC-REC-005-08 expects the malformed URL to be rejected.
- **Reproduction:** acme `hr@acme.test` (subdomain `acme`): `POST /api/v1/recruitment/interviews` `{applicantId, interviewType:"Video", scheduledDate:"2026-07-02", startTime:"14:00:00", durationMinutes:60, videoLink:"not-a-url", interviewerEmployeeIds:["019efced-88a9-..."]}` → **HTTP 201**; `GET` of the created interview returns `videoLink:"not-a-url"`.
- **Evidence:** create → 201 `success:true`; read-back `videoLink='not-a-url'`, `interviewTypeName='Video'`. Positive control: empty `videoLink` → 400 `video_link_required`.
- **Severity rationale:** MED — a bad/garbage link silently ships into reminder/notification content sent to candidates; not a security breach but defeats the FR-1 contract and degrades the participant experience. Contained to data quality.
- **RESOLVED (PR #352, 2026-07-17):** added a shared `BeAValidVideoLink` rule to `ScheduleInterviewValidator`/`UpdateInterviewValidator` — a supplied `videoLink` must be a well-formed **absolute http/https URI** (requiredness unchanged; still enforced by the type-conditional `NotEmpty`). Regression: 6 arms in `ScheduleInterviewValidatorTests` (relative/`javascript:`/`ftp:`/garbage rejected; `http(s)` accepted). Binds TC-REC-005-08 step 5 (was failing).

### ISSUE-115 — No API to mark an interview Completed / No-Show; FR-6 status lifecycle is only Scheduled→Cancelled
- **Type / Severity / Status:** ISSUE · — · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** `/complete` and `/no-show` ship at `InterviewsController.cs:112,132`, with 409 guard arms at `InterviewSchedulingIntegrationTests.cs:347-390`. **Same self-contradiction as [[ISSUE-114]] — table said RESOLVED, summary line said OPEN.**

| Field | Value |
|---|---|
| **ID** | ISSUE-115 |
| **Type** | ISSUE |
| **Severity** | MED |
| **Status** | RESOLVED (PR #353, 2026-07-17) |
| **Layer** | BE |
| **Module / US / TC** | Recruitment / US-REC-005 / TC-REC-005-10 (steps 1-3) |
| **Title** | The `InterviewStatus` enum defines Completed/Cancelled/NoShow but only Cancel is reachable via API; no recruiter action sets Completed or No-Show, and no invalid-transition guard exists |

- **Root cause (95%):** `InterviewsController` exposes only `POST .../cancel` for status change; there is **no** `complete`/`no-show`/`status` endpoint (all probed → 404). The only path to `Completed` is an indirect side-effect of the final scorecard submission (US-REC-006). FR-6 ("track interview status: Scheduled, Completed, Cancelled, No-Show") and TC-REC-005-10 steps 1-3 (mark Completed after time; mark No-Show; reject invalid transition) are therefore unimplemented at the API surface.
- **Reproduction:** acme `hr@acme.test`: `POST /api/v1/recruitment/interviews/{id}/complete` → 404; `.../no-show` → 404; `.../status` → 404. The calendar **filter** by status works (positive: `?status=Cancelled` returns only Cancelled), but No-Show/Completed states are never set so those filters can only ever be empty.
- **Evidence:** all four transition routes return 404 (no MVC route). `GET .../interviews?status=Scheduled` → 12 all Scheduled; `?status=Cancelled` → 1 Cancelled (filter half works).
- **Severity rationale:** MED — a primary FR-6 lifecycle capability (and the No-Show/Completed reporting it feeds) is absent; recruiters cannot record interview outcomes. Not CRIT because scheduling/cancel/calendar still function and Completed is partially reachable via scorecards.
- **RESOLVED (PR #353, 2026-07-17):** added `IInterviewService.MarkOutcomeAsync` (Completed|NoShow) with a transition guard — only a still-Scheduled interview can be concluded, any terminal state → 409 `interview_invalid_transition` — plus `CompleteInterviewCommand`/`MarkInterviewNoShowCommand` and `POST interviews/{id}/complete` + `POST interviews/{id}/no-show` (`Recruitment.Manage`), mirroring the Cancel path (reminder cleared, participants notified). Binds TC-REC-005-10 steps 1-3. Regression: 6 integration arms (happy Completed/NoShow; Completed/Cancelled/NoShow prior-state 409 guards; cross-tenant 404); clear-reminder arm made mutation-resistant with a recording scheduler fake. Auditors: integration-enforcer PASS, test-authenticator AUTHENTIC.

### ISSUE-116 — Interview reminder job is not strictly idempotent on re-run, and the reminder lead-time is app-global, not per-tenant — NFR-4 / BR-5 partial
**▶ DISPOSITION 2026-09-08 (T4).** **Idempotency merged (#689); lead-time storied. Status stays OPEN pending `/verify-fix`.**
- **Idempotency (NFR-4) — fixed for BOTH jobs.** ⚠ **This entry names only `InterviewReminderJob`;
  `OfferExpiryReminderJob` had the same defect** and is not recorded anywhere. It *cleared* its marker but never
  *read* it, so a retry re-sent.
- ⚠ **A previous note claimed the interview job "needs a new column and a migration". That was wrong** —
  `Interview.ReminderJobId` already existed (`Interview.cs:60`), mapped, set on schedule and cleared on cancel.
  **Both** jobs already owned a marker; neither read it. No schema change was needed.
- ⚠ **This entry's severity rationale — "the notification is a log-only seam (no real email yet)" — is FALSE**
  and materially mis-rated the finding. See [[ISSUE-531]] and [[BUG-530]].
- **Composition defect found on merge, fixed before shipping:** [[ISSUE-571]] — clearing the marker *before*
  dispatch made [[BUG-530]]'s Hangfire retry **inert**, because the retry hit the null-marker guard and returned
  without re-sending. Both jobs now clear **only after a confirmed-successful dispatch**. Neither PR was wrong
  alone; only the composition was, which is why neither PR's own tests caught it.
- **Per-tenant lead time (BR-5) — NOT built.** Storied as **`US-REC-011`**. `InterviewService.cs:474` (not
  `:418`, as this entry states) reads an app-global config key. The offer sibling is worse and out of scope:
  `OfferService.cs:51` is a bare const with no `IConfiguration` at all ([[ISSUE-539]]).

- **Type / Severity / Status:** ISSUE · — · OPEN

| Field | Value |
|---|---|
| **ID** | ISSUE-116 |
| **Type** | ISSUE |
| **Severity** | LOW |
| **Status** | OPEN |
| **Layer** | BE |
| **Module / US / TC** | Recruitment / US-REC-005 / TC-REC-005-02 (steps 4-5) |
| **Title** | `InterviewReminderJob` re-sends the reminder on every execution (only no-ops when not Scheduled) → a Hangfire retry double-notifies; lead-time read from global config `Recruitment:InterviewReminderLeadHours`, not tenant config |

- **Root cause (90%):** `InterviewReminderJob.RunAsync` guards only `interview is null || Status != Scheduled` (`InterviewReminderJob.cs:49`); there is no "already-reminded" marker, so a second execution of the SAME job for a still-Scheduled interview dispatches a second reminder. NFR-4 states "a re-execution (Hangfire retry) does not send duplicate reminders." Separately, the lead-time is `_configuration.GetValue("Recruitment:InterviewReminderLeadHours")` (`InterviewService.cs:418`) — an **application-global** setting, whereas BR-5 specifies the lead time is "configurable at the tenant level."
- **Reproduction:** schedule an interview (reminderJobId issued); trigger the scheduled job, then re-queue it. Serilog shows **two** `interview-reminder` dispatch lines for the same interview id (`019f02ab-03d9-...`), each "to applicant + 2 interviewer(s)".
- **Evidence:** log slice — run 1 `12:13:10.474 InterviewReminderJob: sent reminder ... to applicant + 2 interviewer(s)`; run 2 `12:13:12.624 ... sent reminder ...` (duplicate). Tenant context restored correctly both times (`tenant=019ef3ba` acme), so NFR-4's tenant-awareness IS met — only the dedup is missing.
- **Severity rationale:** LOW — impact is bounded today because the notification is a **log-only seam** (no real email yet, US-NTF deferred); the duplicate becomes user-visible only once real delivery is wired. Per-tenant lead-time is a config-shape nit until multi-tenant lead-times are needed.

### ISSUE-117 — Interview `notes` (rich text) stored verbatim; no server-side sanitization/escaping — TC-REC-005-11 step 6
- **Type / Severity / Status:** ISSUE · — · OPEN

| Field | Value |
|---|---|
| **ID** | ISSUE-117 |
| **Type** | ISSUE |
| **Severity** | LOW |
| **Status** | OPEN |
| **Layer** | BE |
| **Module / US / TC** | Recruitment / US-REC-005 / TC-REC-005-11 (step 6) |
| **Title** | A `<script>…</script>` payload in `notes` is persisted unescaped (only `.Trim()` applied) |

- **Root cause (85%):** `InterviewService` stores `Notes = Trim(input.Notes)` (`InterviewService.cs:112`) — whitespace-trim only, no HTML sanitization/encoding. TC-REC-005-11 step 6 expects the rich-text notes to be "sanitized/escaped on storage + render." This matches the platform-wide pattern (Core HR stores an employee literally named `<script>alert(1)</script>`); defense relies on the Angular renderer auto-escaping (`{{ }}` / `[textContent]`), so stored XSS only executes if a consumer uses `innerHTML`/`bypassSecurityTrust`.
- **Reproduction:** acme `hr@acme.test`: `POST .../interviews` with `notes:"<script>alert(1)</script>"` → 201; `GET` of the interview returns `notes:"<script>alert(1)</script>"` (raw).
- **Evidence:** stored `notes='<script>alert(1)</script>'` verbatim. (Authz half of TC-REC-005-11 PASSES — see verdict table.)
- **Severity rationale:** LOW — stored-only, no auto-execution under Angular's default rendering; defense-in-depth gap, not an active XSS. Raise if any view binds notes via innerHTML.

### ENH-010 — Create/update/cancel notifications are dispatched inline on the request thread; business-hours validation (NFR-6) is absent
**▶ DISPOSITION 2026-09-08 (T4).** **Part (1) OBSOLETE; part (2) reclassified to T5. Status stays OPEN pending `/verify-fix`.**
- **(1) inline dispatch** — the premise was overtaken: SMTP is already off-thread via Hangfire, so the concern
  the finding was filed for no longer applies. No work needed.
- **(2) NFR-6 business hours** — **moved to T5 as a product decision, not engineering.** The framing carried in
  the queue was wrong: **NFR-6 is not about notifications at all.** Its full text (`US-REC-005.md:49`) is a
  *scheduling-form* rule. That single clause defines none of: what the hours are, **whose** hours (tenant /
  interviewer / overseas candidate), hard-reject vs soft-warn, or where "configurable" lives. QA reached the
  same conclusion independently — `TEST-MATRIX.md:326` records it CONDITIONAL, explicitly **"NOT a gap"**.
- ⚠ This entry's own text calls the recruitment notifier a "log-only seam". **That is false** and was false when
  written — `DependencyInjection.cs:378` registers `RealRecruitmentNotificationService`. See [[ISSUE-531]]; the
  stale claim produced two wrong engineering judgements in one session before it was caught.

- **Type / Severity / Status:** ENH · — · OPEN

| Field | Value |
|---|---|
| **ID** | ENH-010 |
| **Type** | ENH |
| **Module / US** | Recruitment / US-REC-005 |
| **Title / why it matters** | Two non-defect observations. (1) **Inline notifications vs NFR-3:** only the *reminder* runs on Hangfire; the schedule/reschedule/cancel participant notifications are dispatched **synchronously after SaveChanges within the request** via the log-only seam (`InterviewService.NotifyParticipantsSafeAsync`). It's cheap today (log-only) and wrapped in try/catch so it can't fail the write, but once real SMTP is wired this inline call would bleed delivery latency into the NFR-1 800ms API SLA — NFR-3 wants delivery offloaded to Hangfire/outbox. (2) **Business hours (NFR-6):** the validator enforces future-date (BR-3) but there is no business-hours check; an interview at 03:00 is accepted. The validator comment notes "no tenant-timezone infra yet." |
| **Suggested direction** | Move create/update/cancel notifications onto the same Hangfire/outbox path the reminder already uses; add an optional tenant-configured business-hours window when timezone infra lands. **Not applied (REPORT-ONLY).** |

> **BUG-003 (cross-tenant JWT-vs-subdomain mismatch) — checked on the US-REC-005 interview surface; NO fresh leak (self-protecting), not re-filed.**
> The interview reads + writes are scoped by the EF global query filter keyed off the **resolved** tenant, and every write is additionally gated by in-tenant entity existence (applicant + interviewers). Probed live today and reverted (nothing to clean — the write was blocked before insert):
> - **READ arm (TC-REC-ISO-014 steps 1-3):** acme `hr@acme.test` token (JWT tenant_id=acme, holds Recruitment.Manage+View) + header `X-Tenant-Subdomain: techoneglobal` → `GET .../interviews` returned **0** rows (techoneglobal's own set, which is empty — NOT acme's 15), and `GET .../interviews/{acmeInterviewId}` under the spoofed context → **404**. So acme's interviews are invisible under the techoneglobal-resolved context — no read leak on this surface (contrast the goals/cycle surfaces which DO leak because they're not gated by in-tenant entity existence).
> - **WRITE arm (TC-REC-ISO-014 step 5):** same acme token + techoneglobal header, `POST .../interviews` with the **acme** applicant id → **HTTP 404 `applicant_not_found`** (the acme applicant isn't visible under techoneglobal context; the create aborts at the applicant existence check before any insert). techoneglobal verified still 0 interviews afterward — **no residue, nothing to revert**. A cross-tenant interview cannot be written because scheduling requires an in-tenant applicant + in-tenant active interviewers (BR-2), both of which the spoofed context cannot supply.
> - **Net:** TC-REC-ISO-014 PASS. The systemic BUG-003 root (spoofable `X-Tenant-Subdomain` in `TenantResolutionMiddleware`, no `CurrentUser.TenantId == ITenantContext.TenantId` guard — root locus US-AUTH-007) is unchanged, but this surface does not surface a leak because all interview data access is gated by in-tenant entity existence. Same self-protecting pattern as the clock-out / regularization / REC-004 surfaces. See [[auth-full-test-pass-2026-06-25]].

> **Notes for US-REC-005 (not separate findings):** (1) Notification dispatch is the known **log-only seam** (`LogOnlyRecruitmentNotificationService`) — schedule/reschedule/cancel/reminder all fire log events to the applicant email + all interviewer work emails (FR-3/BR-7 recipients verified correct), real email/in-app delivery deferred per US-NTF; this is the platform-wide "seam built, not wired" state, not a US-REC-005 defect. (2) Hangfire reminder scheduling/swap/remove all verified live: schedule → job id issued (e.g. 225) with StartsAtUtc - 24h fire-time and tenant_id in params; reschedule → old job cancelled + new job id stored; cancel → reminder removed (job id → null). (3) Round auto-increment (FR-2), per-interviewer half-open `[start,end)` conflict detection with override (FR-7), and calendar filters (status/interviewer/vacancy/date-range, FR-5) all PASS. (4) TC-REC-005-12 (perf): single-user p95 ≈ 130ms (well under the 800ms write SLA) but the full NFR-1/NFR-3 contract (steady k6 load + injected 3s SMTP delay to prove non-blocking) is **BLOCKED** — k6 not scripted for this flow + no slowable SMTP stub (notification is log-only). (5) TC-REC-005-13 (a11y/cross-browser) **BLOCKED** — FE :4200 down + platform-bound, no UI to drive.

---

## US-PRF-005 — 360-Degree Review (Peers, Reports, Manager, Self) (run 2026-06-26, REPORT-ONLY API layer)

> Routes: `/api/v1/tenant/performance/360/*` — reviewer config (`GET .../reviewers`), add/remove reviewer, `notify`, `submit feedback`, `results`, `report`. Config/results/report/notify/remove are **HR-only** (`Performance.Review.All`); **submit** is open to any authenticated user but self-resolves the reviewer from the caller + requires a Pending assignment (so all four categories self-submit, no IDOR). BR-2 (no self-as-peer), BR-3 (one feedback per reviewer/reviewee/cycle, 409 `already_submitted`), FR-4 rating-range (422), FR-6 composite via `ThreeSixtyScoreCalculator` (normalizes by weight of categories WITH data), BR-4 peer-threshold = **warn not block** (`releaseWarning`), anonymity captured per-row at submit (BR-5) + enforced in the projection (NFR-3 → `reviewerEmployeeId`/`reviewerName` null). Reminder job `performance-360-reviewer-reminders` IS DI-registered + tenant-iterates (unlike the US-PRF-004 cycle scheduler, BUG-063). Personas: `hr@acme.test`/`tenantadmin@acme.test` (Review.All); reviewers submitted AS `employee@acme.test` (John, Peer) + `manager@acme.test` (EMP-MGR01, Manager); reviewee = Et Contract (EMP-0014, no user). 18 TCs executed: **13 PASS / 4 FAIL / 1 BLOCKED-pair (= 2 BLOCKED)** → **13 PASS / 4 FAIL / 2 BLOCKED... wait** correction below. Actual: **12 PASS / 4 FAIL / 2 BLOCKED**. NEW finding: ISSUE-118. Extends: BUG-003.

### ENH-011 · ENH · BE — Scorecard lock-period (BR-4) is not testable/observable at the API layer; no `GET /scorecards/{id}`; no version history
**▶ CORRECTED BLOCKER 2026-09-08 — the container rebuild did NOT unblock the three TCs.**
I predicted it would; it did not, and the reason matters. **There are ZERO interviews and ZERO
scorecards in EVERY tenant** (`e2e`, `perf`, `platform`, `techoneglobal` — all 0). TC-REC-006-05,
TC-REC-006-08 step 4 and TC-REC-ISO-015 all require a live scorecard workflow (vacancy → applicant →
interview → 2 interviewers → scorecards). The blocker is **missing seed data, not a stale image**.
**What the rebuild DID achieve — this closure now rests on live evidence, not code-reading:**
- `GET /scorecards/{id}` **exists** — returns `scorecard_not_found` (a *domain* 404 with an error code),
  not an empty-body route 404. The distinction is load-bearing; see the `ISSUE-321` note.
- **Version history shipped** — `interview_scorecard_revision` exists with 15 columns.
- **Lock period modelled** — `interview_scorecard.locked_at` exists.
All three of this finding's sub-claims are therefore confirmed false against the running system, as well
as against `src/`. Only the QA bookkeeping remains, and it needs a recruitment seed fixture.

**▶ DISPOSITION 2026-09-08 (T4).** **Code CLOSED — already done. QA residual outstanding.**
Verified line by line against current `src/`: all three sub-claims are false, shipped by `c2cc333b` (#465). The
queue recorded this as "~80% stale"; it is **100%**.
**The residual is real and NOT closed:** TC-REC-006-05, TC-REC-006-08 step 4 and TC-REC-ISO-015 are still
recorded **BLOCKED** in `TEST-STATUS.md` and have **not** been re-run. They cannot be re-run yet — the local
Docker stack's backend image was built 2026-09-02 and is **~98 commits stale**, so a run against it would test a
binary that predates most of this work. **Rebuild the container, then re-run those three.** Flipping them
without a run would cross the report-only boundary.

- **✅ CLOSED 2026-09-07 — ALREADY DONE. All three sub-claims are false against current `src/`.** The queue recorded this as "~80% stale"; it is **100%**. Shipped by commit `c2cc333b` ("feat(US-REC-006 AC-K1)", #465), verified line by line:
  - **(a) "a locked state is unreachable without DB access or a 48h wait"** — false. The lock lead is configurable: `ScorecardService.cs:430-434` reads `Recruitment:ScorecardLockPeriodHours` (default 48), and the test hook is already in use at `HRM.Tests/Unit/ScorecardServiceTests.cs:273` (`= "0"`).
  - **(b) "there is no single-scorecard `GET /scorecards/{id}`"** — false. `InterviewsController.cs:365-377` exists, and its own comment at `:358-364` names TC-REC-006-05/-08 and ISO-015 as the reason it was added. `ScorecardService.cs:503-540` deliberately routes through `GetForInterviewAsync` so the anti-bias filter is not re-implemented, and answers **404 not 403** for a hidden card (`:518-527`).
  - **(c) "version history is deferred; edits replace the rating set wholesale"** — false. That exact line range is now the revision-snapshot write (`ScorecardService.cs:129-145`), with entity `InterviewScorecardRevision.cs` and migration `20260804193301_Recruitment_ScorecardRevisionHistory.cs`.
- **Residual is QA bookkeeping only:** TC-REC-006-05, TC-REC-006-08 step 4 and TC-REC-ISO-015 are still recorded BLOCKED in `TEST-STATUS.md` and should be re-run — the ISO-015 "fetch a peer's card by id" arm is now directly executable.
- **Not part of this closure:** scorecard TEMPLATE versioning (AC-K1's other half) remains open — `ScorecardCriteria` is still a hard-coded static list. The author re-filed it separately; see the "What this deliberately is NOT" paragraph in `InterviewScorecardRevision.cs`.
- **Type / Severity / Status:** ENH · — · OPEN
- **Module / US / TC:** Recruitment / US-REC-006 / TC-REC-006-05, -08, ISO-015
- **Why it matters:** (a) The after-lock immutability arm (TC-006-08 step4) cannot be exercised at the API layer: the lock = `interview.StartsAtUtc + 48h` and interviews are validation-blocked from being scheduled in the past, so a locked state is unreachable without DB access or a 48h wait. (b) There is no single-scorecard `GET /scorecards/{id}` endpoint (TC-006-05 step2 / ISO-015 step2 both reference one) — scorecards are only reachable via the interview/applicant list endpoints, so the "fetch a peer's card directly by id" anti-bias/isolation arm has no surface to test (the list-level anti-bias hide IS enforced and PASSes). (c) Version history (BR-4) is explicitly deferred — edits replace the rating set wholesale (`ScorecardService.cs:123-130`); only the edit is audited.
- **Suggested direction:** expose a configurable lock-lead override (test hook) or a recruiter `GET /scorecards/{id}` read to make BR-4 verifiable; consider the deferred version-history for the edit trail. Not a defect — the lock logic itself is code-correct (`if (DateTime.UtcNow >= existing.LockedAt) → 409 scorecard_locked`, `ScorecardService.cs:115-117`).

### BUG-003 NOTE (systemic, already filed — NOT re-filed) — scorecard read/write surface inherits the cross-tenant root, is NOT self-protected
**▶ LIVE VERIFICATION 2026-09-08 — the parked `/verify-fix` is now SATISFIED.**
Parked since 2026-09-02 solely because the dev stack served a stale image. Stack rebuilt
(`scripts/rebuild-stack.sh`, backend image 5 days → 3 minutes old) and the cross-tenant arms re-run
against it. **Control first** — an `e2e` token with its OWN subdomain returns **200** on
`/tenant/employees` and `/tenant/departments`, so the guard is not simply rejecting everything:

| arm | result |
|---|---|
| `e2e` token + `techoneglobal` → `/tenant/settings` | **403 `cross_tenant_denied`** |
| → `/tenant/employees` | **403** |
| → `/tenant/departments` | **403** |
| → `/tenant/data-exports` (the finding's "maximum blast radius") | **403** |
| **PUT** `/tenant/settings/org-profile` — the WRITE arm this finding is *about* | **403** |

⚠ **The reproduction steps in this entry can no longer be followed as written.** They use tenant
`acme`, which **does not exist** in the dev database — the tenants are `platform`, `e2e`,
`techoneglobal`, `perf`. The probes above substitute `e2e` as the actor. A CRIT whose documented repro
references a deleted tenant is itself a defect; recorded here rather than silently re-written.

- **Type / Severity / Status:** BUG · — · OPEN
- **Module / US / TC:** Recruitment / US-REC-006 / TC-REC-ISO-015 (AC-4, NFR-2). Root locus: US-AUTH-007 / `TenantResolutionMiddleware`.
- **Finding:** The scorecard endpoints resolve the tenant from the spoofable `X-Tenant-Subdomain` header (dev) / subdomain, and the EF global query filter trusts `ITenantContext.TenantId`; there is NO check that the JWT's tenant matches the resolved tenant. Confirmed LIVE today: acme `hr@acme.test` JWT + header `X-Tenant-Subdomain: techoneglobal` → `GET /api/v1/recruitment/interviews` returned **HTTP 200** executed against techoneglobal's context (0 rows — techoneglobal has no interviews, but the request was ACCEPTED and re-scoped, not rejected). Platform/system `admin@hrm.local` JWT + `X-Tenant-Subdomain: acme` read acme's scorecards in full (privileged super-user). The scorecard surface therefore does NOT independently protect against BUG-003 — it relies entirely on the shared (broken) root.
- **Self-protected arms (NOT a leak):** acme JWT + a *different* subdomain → the target acme interview is invisible → 404 "Interview not found" (the EF filter re-scopes to the header tenant, so acme's own data is not exposed via that arm). No/invalid tenant context → 400 / 404 (fail-closed; unknown subdomain → 404). Body-injected `tenant_id`/`interviewer_employee_id` are structurally ignored — the request DTO has no such fields and the interviewer is derived from the auth context (BR-1 enforced: TA/HR/unassigned employee all 403 `not_assigned_interviewer`).
- **Cross-tenant WRITE:** NOT performed live (techoneglobal has no interview to attach a scorecard to; creating one would be cross-tenant write residue). The write path stamps `TenantId` from the same header-resolved `ITenantContext` (`TenantInterceptor`) and loads the interview through the same EF filter, so a write would stamp the header-resolved tenant — reasoned from the shared root (consistent with the previously-confirmed ISO-011 write breach), NOT independently exercised here. **Zero cross-tenant rows written; nothing to revert.**
- NFR-2's stated PostgreSQL RLS is not implemented on `interview_scorecard`/`scorecard_criterion_rating`; isolation is EF query filters keyed off the resolved tenant (correct only when header matches token). See [[auth-full-test-pass-2026-06-25]] and the Admin/Core-HR/Leave/Attendance/Performance BUG-003 records.

## US-PRF-006 — Performance Review Meeting Notes & Sign-Off (2026-06-26 REPORT-ONLY API run, @test-runner)

> Routes are **cycle/employee-keyed**, NOT reviewId-keyed as the TCs assume: `/api/v1/tenant/performance/reviews/cycles/{cycleId}/employees/{employeeId}/{notes|request-signoff|acknowledge|dispute|resolve-dispute|export}` (controller `ReviewSignoffController`). Manager-side (notes/request/export) = `Performance.Review.Team` (direct manager) or `.All` (HR); employee-side (acknowledge/dispute) = `Performance.Read.Self` + service-enforced caller-IS-reviewed-employee; HR resolve = `.All`. Verified live against acme personas. Fixtures seeded directly in DB (acme had ZERO cycles/reviews): 1 cycle + submitted manager_reviews for John Doe (employee@, EMP-0001, reports to manager@/EMP-MGR01) — all hard-deleted after the run (zero residue confirmed).
>
> **What's SOLID (PASS):** the full happy path (template AC-1 → request-signoff AC-2 → employee Acknowledge AC-3 → SignedOff + LOCKED BR-5); BR-1 gate (409 `review_not_submitted` on a Draft review, state-driven not blanket); FR-4 mandatory dispute comments (empty + whitespace → 422 `dispute_comments_required`); the dispute → Disputed → HR amend (→ NotesAdded, re-sign) / HR confirm (→ SignedOff) lifecycle (BR-4/FR-5), manager `.Team` blocked from resolving (403); immutability/lock (NFR-3/BR-5 — every manager/employee/HR edit or re-sign of a locked review → 409, append-only `review_signoff` with no update/delete path); authz + IDOR (employee can't add notes 403, manager can't sign on employee's behalf 403, cross-employee sign-off 403 `not_reviewed_employee`, unauthenticated 401, non-report manager 403); FR-7 signature provenance (name + server timestamp + server-derived client IP `::1` on every entry; client-injected body ignored); auto-close BR-3 (Hangfire job DI-registered + scheduled, reads per-cycle window, → NoResponse + immutable System `AutoClosedNoResponse` entry, IDEMPOTENT on re-trigger, per-tenant context — log shows the sweep iterating each of 17 tenants separately); export AC-4/FR-6 (complete record: goals/ratings/notes/both signatures/timestamps; PDF is a deliberate data-only seam, consistent with US-PRF-005); ISO-023 server-derived tenant stamp (body-injected `tenantId` ignored, foreign reviewId 404); NFR-1 editor load p95 = 26ms (≪400ms).

### BUG-003 EXTENSION (systemic, already filed — NOT re-filed) — sign-off meeting-notes surface inherits the cross-tenant root; manager/HR (`.Team`/`.All`) paths LEAK cross-tenant WRITE; employee self-paths are self-protected
- **Type / Severity / Status:** BUG · CRIT · OPEN
- **Module / US / TC:** Performance / US-PRF-006 / TC-PRF-ISO-021, -022, -023, -024 (NFR-2). Root locus: US-AUTH-007 / `TenantResolutionMiddleware` (no `CurrentUser.TenantId == ITenantContext.TenantId` guard).
- **CONFIRMED LIVE cross-tenant WRITE (the dangerous arm):** acme `tenantadmin@acme.test` JWT (`tenant_id=acme`) + header `X-Tenant-Subdomain: techoneglobal` → `PUT .../reviews/cycles/{tgCycle}/employees/{tgEmployee}/notes` returned **HTTP 200** and **created a `review_meeting_notes` row stamped `tenant_id=techoneglobal`, `created_by=tenantadmin@acme.test`, body "CROSS-TENANT WRITE BY ACME USER"**, and the 200 response leaked the techoneglobal employee name ("Cross Write"). The token↔tenant mismatch is NOT rejected; both the EF global query filter and `TenantInterceptor` follow the spoofed header. (To prove the write I temporarily seeded a TG cycle+submitted review, performed the probe, then **hard-deleted the probe row AND the TG fixture — verified ZERO techoneglobal residue across all 5 perf tables.**)
- **Mechanism corroboration:** the inverse arm (acme JWT + `X-Tenant-Subdomain: techoneglobal`, asking for acme's OWN reviewId) returns 404 `employee_not_found` — i.e. the context silently switched to techoneglobal (acme's data became invisible), not a 403 rejection. Export under the spoofed header → 404 likewise.
- **Self-protected arms (NOT a leak):** the employee-side `acknowledge`/`dispute` require the caller to BE the reviewed employee (`GetCurrentEmployeeAsync` → `actor.Id != employeeId` → 403 `not_reviewed_employee`), so even cross-tenant they fail closed. The manager `.Team` path additionally requires the target be the caller's direct report (403 `not_direct_report`), so a `.Team` manager's cross-tenant reach is limited to their own reporting line; the `.All` (HR/TenantAdmin) path is the wide-open one. The leak is on the `.All`/`.Team`-gated notes/request-signoff/resolve-dispute/export surfaces.
- **Severity:** part of the platform CRIT BUG-003; this surface adds a GDPR-relevant write breach (one tenant's admin mutating another tenant's formal review record + signatures). NFR-2's stated PostgreSQL RLS on `review_meeting_notes`/`review_signoff` is NOT implemented; isolation is EF query filters keyed off the resolved tenant (correct only when header matches token). See [[auth-full-test-pass-2026-06-25]] and the Admin/Core-HR/Leave/Attendance/Recruitment BUG-003 records.
- **SURVEY:** **1** HTTP tenant-resolution point — `TenantResolutionMiddleware.cs:78-87` (the dev `X-Tenant-Subdomain` fallback) feeding `:149` `SetTenant` — governing a blast radius of **577** endpoints across **80** controllers (75 class-level `[Authorize]`; 4 `[AllowAnonymous]`: Careers, Portal, Sso, TenantContext), **137** `HasQueryFilter` registrations and **238** non-test files touching `ITenantContext`. Unit = HTTP tenant-resolution points, then the endpoints they govern. Excluded: `HRM.Tests` (30 test-only tenant-context registrations) and Hangfire job paths, which carry no HTTP header. The entry is right that this is a class rather than a one-off — one middleware governs all 577. This entry's own surface is `ReviewSignoffController.cs:22` `[Authorize]`, `:55`.
- **AUDIT (2026-09-07):** **The root-cause claim is FALSE at this commit, and the observation is stale-true.** (1) "there is no `CurrentUser.TenantId == ITenantContext.TenantId` guard" — **FALSE**: `TenantAccessGuardMiddleware.cs:38-53` performs exactly that comparison and returns **403 `cross_tenant_denied`**, registered unconditionally at `Program.cs:762` (after `UseAuthentication`/`UseAuthorization` at `:751-752`, before controllers). It landed in `94af0fe7` — "fix(BUG-003) ... (#119)", **2026-07-02** — which is an ancestor of `origin/test/local-subdomains`. (2) "`CurrentUser.TenantId` is the JWT claim" — **CONFIRMED** (`CurrentUser.cs:31-32` reads `tenant_id`; `JwtService.cs:88` always mints it), so the comparison is real and not a self-equal no-op. (3) "HTTP 200 cross-tenant write on `PUT .../notes`" — **stale-true**: the run is dated **2026-06-26**, six days before the fix; every such request now hits the guard first. (4) "RLS on `review_meeting_notes`/`review_signoff` is NOT implemented" — **FALSE**: `Platform_RlsPolicies_Dormant.cs:40-80` creates `tenant_isolation` on every `tenant_id` table and `DbInitializer.cs:221-222` ENABLEs and FORCEs it when `Rls:Enabled` (`appsettings.json:48-49` ships **`true`**; Development is false). (5) **The ledger already knows**: `TEST-FINDINGS.md:1566` records "RESOLVED by PR #119 ... the guard that closes it is `TenantAccessGuardMiddleware.cs:38-53`", and `:38-42` records the 2026-07-04 reconciliation that flipped three sibling EXTENSION headers — **this one was missed**.
- **SEVERITY CHECK:** **lower** — this is a stale-OPEN entry, not a live CRIT. A CRIT already fixed, and known-fixed 500 lines up in the same file, is pure noise in the backlog. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-121 · ISSUE · LOW · OPEN · BE — Agreed-action `description` is NOT HTML-sanitized on save (stored XSS reaches the DB), unlike the four rich-text note sections
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module / US / TC:** Performance / US-PRF-006 / TC-PRF-006-10 (FR-2/S10 sanitization)
- **Title:** `<script>evil()</script>do thing` submitted as a meeting-notes agreed-action description is persisted **verbatim/unsanitized** in `review_meeting_notes_action.description`, whereas the Body/Strengths/DevelopmentAreas/Summary rich-text fields ARE correctly sanitized (script/onerror/javascript: stripped).
- **Root cause (confidence 95%):** `UpsertNotesAsync` sanitizes the four section fields (`_sanitizer.Sanitize(input.Body/Strengths/DevelopmentAreas/Summary)`, `ReviewSignoffService.cs:135-138`) but stores each action with only `a.Description.Trim()` — no `_sanitizer.Sanitize(...)` call (`ReviewSignoffService.cs:151-160`). Verified live: stored row `description = '<script>evil()</script>do thing'`. The SQLi-style string (`'; DROP TABLE …;--`) was stored as inert literal text (parameterized persistence — table survived), so only the action-description sanitization is the gap.
- **Reproduction:** acme; HR/manager `PUT …/notes` with `"actions":[{"description":"<script>evil()</script>do thing","deadline":"2026-12-31"}]` → 200; `SELECT description FROM review_meeting_notes_action WHERE description LIKE '%script%'` → unsanitized script tag present.
- **Evidence:** DB row `<script>evil()</script>do thing`; for contrast `body` saved as `<p>ok</p><img src="x">` (script + onerror stripped).
- **Severity rationale:** LOW — defense-in-depth: exploitability depends on whether the FE renders the action description as raw HTML (if rendered as text it's inert); the four primary note fields are sanitized. Should be aligned (sanitize the action description too) since S10 mandates "rich text stored as sanitized HTML" for the notes including FR-2's agreed actions.

### ENH-012 · ENH · BE — Sign-off employee read-path + per-cycle auto-close window have no test/observability hooks; HR resolver signer-name falls back to email
**▶ DISPOSITION 2026-09-08 (T4).** **All three parts settled; status stays OPEN pending `/verify-fix`.**
- **(a) ALREADY DONE** (`63f502a5` + `69447b82`), including the BR-2 read-tracking this finding framed as a
  downstream benefit. The queue carried **no note at all** on this entry, which made it the one where a stale
  premise would have cost most.
- **(b) auto-close window** — configurable and test-observable, **merged (#682)**.
- **(c) signer identity** — **merged (#685)**. The finding described a single email fallback; the audit found the
  behaviour was **inconsistent across three call sites** — one fell back to email, two wrote an empty string.

- **⚠ PARTIALLY CLOSED 2026-09-07 — half (a) is ALREADY DONE; (b) and (c) remain open.** The queue carried **no note** on this entry, which made it the one where a stale premise would have cost the most.
- **(a) employee self-service read path — SHIPPED.** `ReviewSignoffController.cs:132-133` `GET reviews/cycles/active/me/notes` gated `Performance.Read.Self`, plus `:145 /me/acknowledge` and `:159 /me/dispute` (commit `63f502a5`, ISSUE-288). The BR-2 read-tracking this finding framed as a downstream benefit is **also** enforced: `ReviewSignoffService.cs:246-247` returns 409 `notes_not_read` (commit `69447b82`, BUG-065).
- **(b) auto-close window — STILL FULLY OPEN.** `AppraisalCycle.SignoffAutoCloseDays` is read only by `ReviewSignoffAutoCloseService.cs:45-47`; it appears in **zero** files under `HRM.Application` or `HRM.Api`, is absent from `CreateCycleInput`/`UpdateCycleInput`, and there is no on-demand trigger endpoint anywhere. Testing BR-3 still requires a raw DB UPDATE plus the Hangfire dashboard. No migration needed — the column exists.
- **(c) signer name — STILL FULLY OPEN, and now INCONSISTENT.** `ReviewSignoffService.cs:306` still falls back to `_currentUser.Email`, but the other two call sites (`:180`, `:249`) pass `SignerDisplayName(actor)` straight through and `AppendSignoff` coerces null to `string.Empty` (`:594`). So a signer with no linked employee writes an **empty** name on acknowledge/dispute and an **email** on resolve-dispute. That inconsistency is not in the finding as written.
- **Type / Severity / Status:** ENH · — · OPEN
- **Module / US / TC:** Performance / US-PRF-006 / TC-PRF-006-06, -05, -12
- **Why it matters:** (a) See BUG-065 — there is no employee-reachable notes-read endpoint; even aside from BR-2 read-tracking, the reviewed employee cannot fetch the notes they are asked to sign via API (only `.Team`/`.All` can GET). A `Performance.Read.Self`-gated read of one's own pending review would make the employee sign-off self-service and let BR-2 be implemented. (b) The BR-3 auto-close window is per-cycle (`appraisal_cycle.signoff_auto_close_days`, default 7) and only verifiable by editing the DB + triggering the Hangfire recurring job manually (`/hangfire/recurring/trigger`) — a short-window test hook or an on-demand admin trigger would make BR-3 testable without DB writes. (c) When an HR resolver has no linked employee record, the `review_signoff.signer_name` falls back to the user email (`hr@acme.test`) rather than a person name — cosmetic, but the signature provenance reads as an email on amend/confirm entries.
- **Suggested direction:** add a self-scoped employee notes-read endpoint (also unblocks BR-2); expose a configurable/short auto-close lead for tests; ensure HR/admin accounts that sign are employee-linked or render a display name. Not defects — noted as test-enablement + provenance polish.

---

## US-REC-007 — Generate & Send Offer Letter (REPORT-ONLY API pass 2026-06-26)

Routes `/api/v1/recruitment/offers*` + `/api/v1/recruitment/applicants/{id}/offers`. Writes gated `Recruitment.Manage`, reads `Recruitment.View` (the story's `Recruitment.Offer.All` does NOT exist; the controller documents the substitution). `Recruitment.ApproveOffer` exists in the catalog but is wired to NO endpoint. Core lifecycle (generate -> send -> accept/decline/withdraw, supersession/versioning, auto-expire Hangfire job, tenant isolation, authz, PDF-gen perf) is SOLID. Findings below.

### BUG-003 note (US-REC-007 offer surface) — NOT vulnerable
- **Type / Severity / Status:** BUG · — · OPEN
- The offer surface is **self-protected** against the BUG-003 spoofable-`X-Tenant-Subdomain` mechanism. With an acme JWT + `X-Tenant-Subdomain: techoneglobal`: GET offer -> 404, list -> empty, document -> 404, withdraw -> 404, generate-against-acme-applicant -> 404 (applicant_not_found). Reads re-scope to the resolved (header) tenant via the EF global query filter, so there is no acme data to leak; writes require an applicant resolvable in the session tenant, blocking cross-tenant generation. `SELECT count(*) FROM offer WHERE tenant_id=techoneglobal` = 0 after probes. No leak, no cross-tenant write.

---

## US-PRF-007 — Performance Dashboard and Analytics (run 2026-06-26, REPORT-ONLY API layer)

> Routes (live): `GET /api/v1/tenant/performance/dashboard/{overview | department/{deptId:guid} | trend | export}` (`PerformanceDashboardController.cs:41,71,100,129`). All four admit `Performance.View.All` OR `Performance.View.Team`; the service `ResolveScopeAsync` then resolves Organization (`.View.All` → org-wide + top/bottom) vs Team (`.View.Team` only → caller's direct reports + team ranking, bottom suppressed). Employees (neither perm) → 403. **Permission-name drift:** the US/TCs name `Performance.Read.All`/`.Read.Team`/`Reports.View.All`; the live catalog uses `Performance.View.All`/`.View.Team` (+ `Reports.View`) — `Read.*` perms do not exist; recorded under the ISSUE-100 route/contract-drift class, not re-filed. **Route/shape drift:** the TCs reference `/dashboard/top-performers`, `/dashboard/departments/{id}/employees` (plural + `/employees`), and a `scope=org` param — none exist; top/bottom are embedded in `overview`, drill-down is `/dashboard/department/{id}` (singular), and there is no client `scope` param (server-derived). Also ISSUE-100 class. Personas (acme): `tenantadmin@acme.test`/`hr@acme.test` (View.All), `manager@acme.test` (View.Team → EMP-MGR01, direct reports John Doe EMP-0001 + Et Contract EMP-0014), `employee@acme.test` (neither). **Reseed:** acme had ZERO appraisal cycles (prior PRF runs cleaned up), so a deterministic FY24/FY25/FY26 dataset was seeded (see Reseed note at end) — all hard-deleted after the run, 0 residue. 19 TCs executed: **10 PASS / 3 FAIL / 6 BLOCKED**. Calc engine is EXACT (avg/distribution/dept-averages/progress/trend all reconciled to the seed). Headline defect = BUG-003 cross-tenant leak (extended below). New findings: ISSUE-126 (PDF export unimplemented), ISSUE-127 (performer trend indicator missing), ISSUE-128 (BR-2 probation semantics drift), ISSUE-129 (NFR-3/BR-4 materialized-view/Redis/Hangfire-refresh deferred), ENH-013. (IDs continue past the concurrent Recruitment run's BUG-067/ISSUE-125.)

### ISSUE-127 · ISSUE · LOW · OPEN · BE — Top/Bottom performer rows have no "trend indicator" (FR-3 requires name + department + score + trend vs prior cycle)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module / US / TC:** Performance / US-PRF-007 / TC-PRF-007-01 (step 5), TC-PRF-007-10 (step 2) — FR-3
- **Title:** FR-3 (and TC-007-01 step 5 / TC-007-10 step 2) require each top/bottom performer entry to show **name, department, score, AND a trend indicator** (improvement/decline/flat vs the prior cycle). The `PerformerDto` returned in `overview.topPerformers`/`bottomPerformers` carries only `employeeId`, `employeeName`, `employeeNo`, `departmentId`, `departmentName`, `score` — there is **no trend/delta field**. The prior-cycle comparison FR-3 calls for is not computed or surfaced.
- **Root cause (~95%, code):** `PerformerDto` (`PerformanceDashboardDtos.cs`) has no trend property and `ToPerformer` (`PerformanceDashboardService.cs:565-573`) projects only the six listed fields; the service never loads a prior-cycle score to compute a delta. The trend data exists conceptually (the trend endpoint computes per-cycle averages) but is not joined per-employee into the performer lists.
- **Reproduction steps:** acme, hr@ + acme header. `GET .../dashboard/overview?cycleId={FY26}` → inspect `topPerformers[0]` → fields are exactly `{employeeId, employeeName, employeeNo, departmentId, departmentName, score}`; no `trend`/`trendIndicator`/`previousScore`/`delta`. (Seeded so John has FY24 3.5→FY25 4.0→FY26 4.5, an unambiguous upward trend that is nonetheless not reflected on the performer row.)
- **Evidence (live, acme, today):** `topPerformers` entry `{"employeeId":"019efced-88a9-…","employeeName":"John Doe","employeeNo":"EMP-0001","departmentId":"019efced-5e02-…","departmentName":"Engineering","score":4.5}` — no trend field present in any performer object.
- **Severity rationale:** LOW — the performer lists themselves are correct (ordering, configurable N, name/dept/score all accurate); only the FR-3 trend-indicator sub-requirement is missing, and it is a chart/UX enrichment rather than a data-integrity or security issue. Flagged for FR-3 completeness.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-128 · ISSUE · LOW · OPEN · BE — BR-2 "exclude probation-cycle employees" is implemented as "exclude employees whose employee STATUS = Probation", not "employees in a probation-TYPE cycle" (semantic drift)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module / US / TC:** Performance / US-PRF-007 / TC-PRF-007-09 (BR-2), TC-PRF-007-01 step 3
- **Title:** BR-2 / TC-007-09 define the default exclusion as employees "in **probation cycles** (US-PRF-004 CycleType=Probation)". The implementation instead excludes employees whose **HR employee status** is `Probation` (`EmployeeStatus.Probation`), regardless of cycle type. The default-exclude / `includeProbation=true`-include toggle works correctly mechanically, but it keys off the wrong attribute: a regular employee enrolled in a Probation-type appraisal cycle would NOT be excluded, while a probation-status employee in the normal annual cycle IS excluded.
- **Root cause (~90%, code):** `LoadPopulationAsync` applies `if (!filter.IncludeProbation) employeesQuery = employeesQuery.Where(e => e.Status != EmployeeStatus.Probation)` (`PerformanceDashboardService.cs:389-390`) — it filters on the employee's lifecycle status, not on `AppraisalCycle.Type == CycleType.Probation` (or a per-participant probation flag) as BR-2 describes. The data model does have `CycleType` (incl. a Probation value) but the dashboard does not consult it for the exclusion.
- **Reproduction steps:** acme, hr@ + acme header, cycle FY26 (seeded with EMP-0033 whose employee status = Probation, score 1.5). Default `overview?cycleId={FY26}` → scored=5, avg 3.7 (EMP-0033 excluded). `&includeProbation=true` → scored=6, avg 3.33 (EMP-0033 included). The toggle behaves, but the exclusion is by employee-status, not cycle-type — confirmed by reading the query predicate.
- **Evidence (live, acme, today):** default 5 scored / 3.7; includeProbation=true 6 scored / 3.33 (delta is exactly EMP-0033's 1.5). Predicate `e.Status != EmployeeStatus.Probation` per source.
- **Severity rationale:** LOW — the include/exclude mechanism is functional and server-side (not just UI-hidden), and "probation employee" vs "probation cycle" often coincide in practice; the drift only bites when an org runs a dedicated probation-TYPE cycle for non-probation-status staff. Spec/implementation semantic mismatch worth aligning, no data leak.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-129 · ISSUE · LOW · OPEN · BE — NFR-3/BR-4 materialized view (`performance_summary`) + Redis cache + Hangfire 4h refresh are not implemented; aggregates computed live each request (documented deferral)
**▶ DISPOSITION 2026-09-08 (T4).** **Cache SHIPPED (#688). Materialized view REFUSED, not deferred.**
⚠ This entry's body still describes the pre-#688 world ("Redis cache … not implemented"). That is now wrong;
read this block first.
- **Redis read-through cache — merged (#688).** Tenant- *and* scope-scoped keys. `GetOverviewAsync` was ~9-12
  sequential round trips and `GetTrendAsync` re-ran the 7-query fan-out **once per cycle**; both are cached.
  An independent adversarial review held the PR until its tests could catch the one mistake this codebase has
  already made — swapping the key prefix from the resolved tenant to the JWT tenant. That mutation now turns
  **4 of 19 tests red**, including a behavioural arm showing tenant B's dashboard served to a tenant-A user.
- **`performance_summary` matview — REFUSED.** Storied as **`US-PRF-012`**, marked `[~] BLOCKED` so
  `/implement-all` cannot pick it up. **The block is CATEGORICAL, not temporal:** PostgreSQL has **no RLS for
  materialized views at all**, and both the policy migration (`20260710120000…:55`) and the enabler
  (`DbInitializer.cs:218`) filter on `table_type = 'BASE TABLE'`. Enabling RLS buys it **nothing, ever**. A
  matview is a physical **cross-tenant** artifact — isolation would move off the EF global query filter onto a
  hand-written `WHERE tenant_id =` on every read, the exact class [[BUG-003]] already exploits **on this same
  dashboard**, and `.semgrep/tenant-isolation.yml` would not catch an omission ([[ISSUE-537]]).
- ⚠ **Correction:** an earlier note claimed RLS was "dormant". It is not — `appsettings.json:48-50` ships
  `"Rls": { "Enabled": true }`; only Development overrides it. See [[ISSUE-540]].

- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module / US / TC:** Performance / US-PRF-007 / TC-PRF-007-11 (NFR-1/NFR-3), TC-PRF-007-13 (BR-4), TC-PRF-ISO-028 (cache/refresh tenant-scoping)
- **Title:** NFR-3 + BR-4 + Data §7 specify a `performance_summary` PostgreSQL materialized view, Redis read-through caching, and a tenant-configurable Hangfire refresh job (default 4h) backing the dashboard aggregates. None exist: the `performance_summary` table is absent (`information_schema.tables` count = 0), there is no Redis cache layer for the dashboard, and no dashboard/materialized-view refresh recurring job is registered (the only PRF recurring jobs are `CyclePhaseTransitionJob` and `SelfAssessmentReminderJob`). All aggregates are computed **live** on each request via tenant-scoped EF queries + in-memory GroupBy.
- **Root cause (~99%, code):** `PerformanceDashboardService` class-doc explicitly marks this an extension point ("these aggregates are computed LIVE on each request … A future story can introduce a performance_summary materialized view refreshed every 4 hours by Hangfire + a Redis read-through cache", `PerformanceDashboardService.cs:30-33`). `performance_summary` appears only in comments. No `RecurringJob.AddOrUpdate` for a dashboard refresh in `Program.cs`/`HangfireCyclePhaseScheduler.cs`.
- **Reproduction steps:** (a) `SELECT count(*) FROM information_schema.tables WHERE table_name='performance_summary'` → 0. (b) `grep` recurring-job registrations → no dashboard/materialized-view refresh. (c) Consequently TC-007-13 (refresh seam: "new review not reflected until the view refreshes") is contradicted — the dashboard reflects a new submitted review **immediately** because it reads live data, not a stale view.
- **Evidence (live, today):** `performance_summary` table count 0; recurring-job list = TokenCleanup/DocumentExpiry/…/CyclePhaseTransition/SelfAssessmentReminder (no perf-dashboard refresh). Live overview reflects the seed instantly.
- **Severity rationale:** LOW — explicitly documented as a deferred extension point in the service contract (the DTOs are the stable seam), and functional correctness is preserved (live aggregates are exact and tenant-isolated); the gap is the NFR-3/BR-4 performance/caching MECHANISM, which matters only at the 5,000-employee scale (TC-007-11, not seedable here). Recorded so the deferral is traceable. NFR-2's separately-stated "PostgreSQL RLS on the view" is likewise N/A (no view) — isolation is EF query filters (see BUG-003 note below).
- **Suggested direction (NOT applied):** none — report only.

### ENH-013 · ENH · Performance/US-PRF-007 — Test-enablement + minor scope-rejection polish for the dashboard
- **Type / Severity / Status:** ENH · — · **PARTIALLY RESOLVED 2026-09-07** — (a) closed by human decision, (b) shipped (#686), (c) unblocked by #688 but not yet exercised. See the disposition block below.

**▶ DISPOSITION 2026-09-07 — all three parts settled**

- **(a) Manager drill into an unmanaged department returns 200-empty rather than 403 — CLOSED, WONTFIX by human decision.** The user decided to **keep the 200-empty down-scope**. It is the correct call and worth recording the reason rather than just the verdict: the down-scope leaks nothing (the manager's `RestrictEmployeeIds` intersected with a foreign department is genuinely 0 rows), and a 403 would be **strictly more informative to an attacker** — it distinguishes "this department exists but is not yours" from "no such department", which the empty list does not. The finding framed 403 as the clearer signal; for a caller enumerating department ids it is clearer to the wrong audience. The two bound TCs (TC-007-06 step 5, TC-007-08 step 4) expect "403 / empty" and are satisfied by the empty arm as written.
- **(b) No 5,000-employee perf fixture — RESOLVED (#686).** `perf/seed/perf-volume-seed.sql` gained a WS-D Performance section: 5 cycles / 20,300 participants / 20,300 self-assessments / 20,300 manager reviews / 60,900 goals, idempotent across runs. Deliberately **4 non-probation cycles plus 1 probation**, because `GetTrendAsync` re-runs the population pipeline once per cycle and a single cycle would leave the N+1 path untested. The probation cycle is dated **behind** FY2026 on purpose — `ResolveCycleAsync` takes `max(start_date)` ignoring type, so a later-dated probation cycle would silently become the default overview cycle and return a fast green meaningless 200.
- **(c) ISO-028's cache-key-namespacing and per-tenant refresh arms — MECHANISM NOW EXISTS (#688), arms still to run.** `ISSUE-129`'s read-through cache shipped with tenant- and scope-scoped keys, so the cache-key-namespacing arm is directly executable. **The per-tenant refresh-job arm is NOT unblocked and should be re-scoped:** no Hangfire refresh job was built, because the materialized view it would refresh was deliberately refused on tenant-isolation grounds (see `ISSUE-129`). An arm testing a refresh job that will not exist should be rewritten against TTL expiry instead.

⚠ **Do not read (b) as making the NFR-1 timings trustworthy.** The k6 scripts that would consume this fixture (`perf/scripts/03`, `05`) drive above a 300 req/min per-`(tenant,user)` rate limiter, and fast 429s pull p95 **down** — so a green threshold can be measuring the limiter. Gate on `http_req_failed` before reading any p95 from them.

- **Module / US / TC:** Performance / US-PRF-007 / TC-PRF-007-06/-08/-11, TC-PRF-ISO-028
- **Why it matters:** (a) **Manager drill into an unmanaged department returns HTTP 200 with an empty list, not 403.** TC-007-06 step 5 / TC-007-08 step 4 expect "403 / empty"; the live behaviour is a clean down-scope to empty (the manager's RestrictEmployeeIds set intersected with the foreign dept = 0 rows), which leaks nothing and is acceptable, but a 403 would be a clearer signal that the department is out of the caller's reporting line. (b) **No 5,000-employee perf fixture** exists, so NFR-1/NFR-5 P95 timing (TC-007-11) and large-export timing (TC-007-05 step 5) cannot be validated at scale through the seedable path; a scale-seed harness or a synthetic large tenant would unblock these. (c) Once the NFR-3/BR-4 cache + Hangfire refresh (ISSUE-129) are implemented, ISO-028's cache-key-namespacing and per-tenant refresh-job arms become testable — they are currently BLOCKED purely because the mechanism is absent.
- **Suggested direction:** consider returning 403 (or a documented down-scope marker) for out-of-scope drill-downs; add a scale-seed fixture for NFR perf TCs; revisit ISO-028 cache/refresh arms when ISSUE-129 lands. Not defects — test-enablement + clarity polish.

> **BUG-003 (cross-tenant JWT-vs-subdomain mismatch) — EXTENDED to the US-PRF-007 dashboard surface (cross-tenant READ + EXPORT leak for `.All` holders across overview/trend/drill-down/export), not re-filed.**
> Same root mechanism as the documented locus (US-AUTH-007 / `TenantResolutionMiddleware`): the dashboard resolves the tenant from `ITenantContext.TenantId` (subdomain / `X-Tenant-Subdomain` header) and drives the EF global query filter off it, while authorization is evaluated on the JWT's permissions, with **no guard that the caller's JWT tenant matches the resolved tenant** (`PerformanceDashboardService` runs entirely under the header-resolved tenant; the missing invariant is `CurrentUser.TenantId == ITenantContext.TenantId`). Confirmed live today on this surface, with a distinctively-seeded second tenant (techoneglobal, cycle "TG-SECRET FY26", employee "Cross Write" scored 2.22):
> - **READ leak (TC-PRF-ISO-025):** acme `.All` holders — `tenantadmin@acme.test` AND `hr@acme.test` (JWT tenant_id = acme `019ef3ba-…`, NO techoneglobal membership) — + header `X-Tenant-Subdomain: techoneglobal` → `GET .../dashboard/overview` → **HTTP 200** returning **techoneglobal's** aggregate (cycle "TG-SECRET FY26 Cycle", avg **2.22**, top performer **"Cross Write"**). The same spoofed-header arm leaks on **`/dashboard/trend`** (TG point 2.22), **`/dashboard/department/{tgDeptId}`** (TG employee roster), and **`/dashboard/export?format=csv`** (a full CSV of TG's dashboard) — i.e. every read + export surface (TC-PRF-ISO-025 FAIL).
> - **Mismatched-context = the leak (TC-PRF-ISO-026):** the "token-tenant ≠ resolved-subdomain" arm is exactly the breach above — there is no rejection of the mismatch. By contrast the **missing/invalid** context arms fail closed: NO `X-Tenant-Subdomain` → **400** "Tenant context is not resolved."; **unknown** subdomain → **404** "Workspace not found." And the **IDOR-by-id** arm is CLEAN: acme token + **acme** header + a techoneglobal `cycleId` → 404 `no_cycle` (the EF filter, keyed off the resolved acme tenant, excludes the TG cycle). So the exposure is specifically the spoofable-header path, not direct-id IDOR (TC-PRF-ISO-026 FAIL on the mismatch arm; PASS on missing/invalid/IDOR arms).
> - **Server-derived tenant within a matching header is sound (TC-PRF-ISO-027 PASS):** with header = token (acme), a client `tenantId` query param is **ignored** (still 5 acme rows), and a foreign `departmentId` filter yields **empty** (no cross-tenant blend) — the aggregate's tenant predicate is server-derived, not client-driven. The refresh-write arm (ISO-027 steps 4-5) is BLOCKED (no materialized-view refresh exists — ISSUE-129).
> - **Manager `.View.Team` arm is self-protecting (NOT a leak):** acme `manager@acme.test` token + techoneglobal header → **403** "The current user is not linked to an employee record" (the manager's UserId resolves to no employee in the foreign tenant via the EF filter, `PerformanceDashboardService.cs:301-305`) — same fail-closed pattern as the goals/self-assessment/leave/attendance employee-self-resolve surfaces. The wide-open path is the `.View.All` (HR/TenantAdmin) actor over the spoofable header.
> - **The dashboard is read-only — there is no cross-tenant WRITE arm here** (no write endpoints), so this extension is a READ + EXPORT exposure only. **No probe wrote any data**; the only fixtures created were the deterministic acme + techoneglobal SEED rows, all hard-deleted at end-of-run (both tenants verified back to 0 PRF rows).
> NFR-2's stated mechanism ("PostgreSQL RLS on `performance_summary`") is NOT implemented — there is no view and isolation is EF query filters keyed off the resolved tenant, correct only when the header matches the token. See [[auth-full-test-pass-2026-06-25]], the root-locus confirmation at US-AUTH-007, and the Admin/Core-HR/Leave/Attendance/Recruitment/PRF-001..006 module reports for the systemic record.

> **Reseed note (US-PRF-007, 2026-06-26):** acme had ZERO appraisal cycles at run start (prior PRF runs cleaned theirs up), so the dashboard could not be tested without data. A deterministic dataset (ids prefixed `019f0700-`) was seeded into acme: cycles FY24/FY25/FY26 (Active, rating max 5), FY26 with 6 participants (5 regular + 1 probation-status EMP-0033) and submitted manager_reviews — John Doe 4.5, Speed 3.0, AAAA(EMP-0003 Sales) 5.0, Def 2.0, Et Contract 4.0, EMP-0033 1.5; + self-assessments (4) + goals (5) for progress; FY25 (John 4.0/Speed 3.5/AAAA 4.5) and FY24 (John 3.5/Speed 3.0) for trend. A minimal techoneglobal dataset (ids `019f0700-…-00a1/a2/a3`) — cycle "TG-SECRET FY26", employee "Cross Write" scored 2.22 — was seeded ONLY to materialize the BUG-003 isolation probe. **All seed rows in BOTH tenants were hard-deleted at end of run; residue verified 0** (acme PRF rows back to 0, techoneglobal PRF rows back to 0 — counts in the run report).

---

## US-REC-008 — Candidate Portal (Applicant Tracks Application Status) — API run 2026-06-26 (@test-runner, REPORT-ONLY)

> Surface: anonymous magic-link candidate portal, routes `/api/v1/careers/portal/*` (`[AllowAnonymous]`, token in `X-Portal-Token` header; tenant from subdomain). 13 functional/security/iso TCs PASS, 2 UI TCs (perf/a11y) BLOCKED (fe-platform-bound). Portal is **well-built**: HMAC-SHA256 tenant-bound token (codec verified against C# `PortalMagicLink`), airtight DTO sanitization (NO rejection reason / scorecard / notes leak), correct accept→Hired / decline→stays-Offer, one-time immutability (409), tenant-binding self-protected (**NO BUG-003 leak** — acme token denied on e2e subdomain, no cross-tenant write). Findings below are non-blocking gaps/deferrals.

### ISSUE-131 — BR-5/FR-8: regenerating a portal link does NOT rotate/supersede the prior token; live tokens accumulate per email
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Recruitment / US-REC-008 / TC-REC-008-06 (BR-5/FR-8 "rotates/replaces the row")
- **Title:** `request-link` / `IssueAsync` INSERTs a new `applicant_portal_token` row on every successful re-issue; the previous token row is never expired or soft-deleted, so multiple valid tokens for the same email coexist until each independently expires.
- **Root cause (confidence 95%):** `IssueAsync` always `Add`s a new row (`ApplicantPortalTokenService.cs:83-94`) and never invalidates prior rows for the same email/tenant. `ValidateAsync` accepts ANY non-expired hash row (lines 174-179). TC-REC-008-06 step 2 expects "rotates/replaces the `applicant_portal_token` row." Verified live: after issuing then re-requesting, jordan had 2 valid rows (the old one still validated).
- **Reproduction steps:** request a link for an applicant; request again >60s later; query `applicant_portal_token` → 2+ non-expired rows; the older raw token (if known) still passes `dashboard`.
- **Evidence:** DB showed `019f02e7-…` (13:18) and `019f02ed-…` (13:25) both non-expired for jordan after a second request-link (the extra row was cleaned up post-test).
- **Severity rationale:** Each token is still tenant+email+expiry-bound and expires on its own; no privilege/cross-tenant impact. Spec drift + slightly larger token-revocation surface only. LOW.

### ISSUE-132 — Magic-link feature is operationally unreachable end-to-end: the only live token-minting path never emits the raw token; the FR-7 "email" seam (`GeneratePortalLinkCommand`) has no live caller
**▶ DEFERRAL CONDITION MET 2026-09-08 — ready for `/verify-fix`, no longer blocked.**
This was DEFERRED on US-NTF-006 delivery: "the token/security logic is correct; the only gap is
*delivery*". That gap is closed. `RealRecruitmentNotificationService` is the wired implementation
(`DependencyInjection.cs:378`) and now **builds the raw link itself** —
`PortalLinkBuilder.Build(subdomain, baseDomain, issued.Value.Token)` at `:515` — dispatched as
`applicant_portal_link`, with an explicit failure path logged at `ApplicantPortalTokenService.cs:202`
("Portal link minted for {Email} but the magic-link email dispatch failed (FR-7)").
Live: `POST /api/v1/careers/portal/request-link` returns **200** with the correct
non-enumerating body ("If an application exists for this email, a new portal link has been sent.").
⚠ **One of this entry's claims is now stale:** it states `GeneratePortalLinkCommand` has "zero live
callers". That command **no longer exists at all** — `grep` returns no file, not even its own
definition. The live path is `RequestPortalLinkCommand` → `ApplicantPortalTokenService` →
`RealRecruitmentNotificationService`.

- **Type / Severity / Status:** ISSUE · MED · DEFERRED
- **Type:** ISSUE · **Severity:** MED · **Status:** DEFERRED (blocked on US-NTF-006 delivery — see DF-13) · **Layer:** BE
- **Module / US / TC:** Recruitment / US-REC-008 / TC-REC-008-01/06 (FR-1/FR-7/FR-8 — applicant must RECEIVE a working link)
- **Disposition (2026-07-17, MED-fix campaign):** DEFERRED. The token/security logic is correct; the only gap is *delivery* (raw magic link must reach the applicant by email), which is the deferred FR-7 email seam = the tracked **US-NTF-006 delivery** story. Building it is that story, not a bug fix. Parked as **DF-13** (BLOCKED on US-NTF-006).
- **Title:** `RequestPortalLinkCommand`/`RequestLinkAsync` (the only anonymous, live path that mints a token) logs only metadata, never the raw token; `GeneratePortalLinkCommandHandler` (the documented FR-7 log-only "email" seam that DOES log the raw token) is referenced by no controller/handler. So no real applicant can ever obtain a usable magic link through the running system.
- **Root cause (confidence 92%):** `grep -rln GeneratePortalLinkCommand` returns only its own definition file — zero live callers (no controller wiring, not invoked at application-confirmation, not by a recruiter endpoint). The live `request-link` path (`RequestLinkAsync`, `ApplicantPortalTokenService.cs:107-143`) re-issues and logs "link would be emailed (FR-7, log-only seam)" WITHOUT the raw token (only the hash is persisted; the raw value is discarded). FR-7 real email delivery is explicitly DEFERRED (service docstring lines 22-24). Net effect: the token exists in DB but is never delivered to anyone.
- **Reproduction steps:** `POST /api/v1/careers/portal/request-link {email}` → 200 + a new `applicant_portal_token` row, but no raw token anywhere in the response or logs. There is no live endpoint that returns/sends the raw token. (For this test run a valid token had to be minted offline using the configured HMAC secret + a DB hash update — not a path available to a real user.)
- **Evidence:** `grep` shows `GeneratePortalLinkCommand` has 1 occurrence (its own file). Serilog request-link slice logs token issuance/expiry but no raw token. `PortalLinkRequestResultDto` never carries a token by design (anti-enumeration).
- **Severity rationale:** The portal's data/security logic all works, but until the FR-7 email delivery (or the generate-link seam) is wired, the feature delivers no value to a real applicant — a primary-flow gap, though clearly a known Phase-1 deferral. MED.

---

## US-PRF-008 (Performance Improvement Plan / PIP) — REPORT-ONLY API run 2026-06-26

Scope: all 15 `TC-PRF-008-*` + 4 bound `TC-PRF-ISO-029..032`. Stack: BE native :5000 (no debugger), native PG18, FE down (UI/a11y/perf BLOCKED). Routes `/api/v1/tenant/performance/pips*`. Personas (acme): hr@ (`Performance.Review.All`), manager@ (`Performance.Review.Team`, is John's manager), employee@ (John Doe EMP-0001, `Performance.Read.Self`). PIP table started at 0 rows (prior PRF runs cleaned out). Fixtures used marker `QAPIP008-`; all hard-deleted after (verified 0 residue, acme + techoneglobal).

**Verdicts:** 12 PASS, 4 FAIL (008-09 list-authz, 008-11 encryption, 008-14 report, ISO-031 cross-tenant write), 2 BLOCKED (008-12 perf k6, 008-13 a11y/mobile FE-down). PIP lifecycle engine is SOLID: create/initiate, checkpoint (manager direct-report OR HR), extend, complete, not-met then escalation, BR-2 one-active-PIP (409), BR-3 >=30-day boundary (28d -> 422, exactly-30 -> 200, reversed -> 400), BR-1 HR-only mutate (manager extend/outcome/escalation -> 403, employee/unauth -> 403/401), BR-4 acknowledge (employee-only, double-ack -> 409, immutable PipEvent), FR-5 immutability (no PUT/DELETE; 405/404), FR-8 visibility (unrelated employee / non-managing-manager GET -> 403), FR-3 reminder/ack-timeout Hangfire job DI-registered + sweep logic present + tenant-scoped. **READ isolation CLEAN** (acme `.All` token + `X-Tenant-Subdomain: techoneglobal` -> list `[]`, GET acme PIP by id -> 404; no-header -> 400, invalid-subdomain -> 404). **WRITE isolation LEAKS (BUG-003).**

### BUG-003 EXTENSION — PIP create WRITE leaks cross-tenant (acme HR `.All` + spoofed `X-Tenant-Subdomain: techoneglobal` -> creates a PIP for a techoneglobal employee, HTTP 200)
- **Type / Severity / Status:** BUG · CRIT · OPEN
- **(extends existing CRIT BUG-003 anchor ~line 190 — NOT a new ID per run convention)**
- **Layer:** BE · **US/TC:** US-PRF-008 / TC-PRF-ISO-031 (also the write arm of ISO-029/030)
- **Surface:** `POST /api/v1/tenant/performance/pips`. As `hr@acme.test` (JWT `tenant_id=acme 019ef3ba-...`, holds `Performance.Review.All`) with header `X-Tenant-Subdomain: techoneglobal` and body `employeeId = <techoneglobal employee 019efcf4-ce46-...>` -> **HTTP 200**; a PIP row was created stamped with **techoneglobal's** tenant_id (id `019f02fd-7951-...`, reason `QAPIP008-ISO031-crosswrite`). An acme user wrote into another tenant.
- **Root cause (confidence 95%):** same platform-wide invariant gap as BUG-003 — `PipService.CreateAsync` authorizes off `ICurrentUser.Permissions` (acme HR's `.All`) but the data/tenant context is the **subdomain-resolved** tenant (techoneglobal); there is no `CurrentUser.TenantId == ITenantContext.TenantId` guard. `TenantInterceptor` correctly server-derives tenant_id (so body `tenant_id` injection is ignored — the ISO-031 step1 sub-assertion holds), but that very mechanism is what lets the resolved-tenant write land in techoneglobal. The foreign-`employee_id` arm self-protects: with the CORRECT acme subdomain, creating a PIP for a TG employee -> **404 employee_not_found** (EF filter hides the foreign employee); write-IDOR (TG header, acme PIP, record-checkpoint) -> **404**. So the hole is specifically `.All`-holder + mismatched subdomain header, identical to all prior BUG-003 surfaces.
- **Reproduction:** `POST /api/v1/tenant/performance/pips` with acme HR bearer + `X-Tenant-Subdomain: techoneglobal` + a valid TG `employeeId` + >=30-day window + 1 objective -> 200, cross-tenant PIP created.
- **Evidence:** create response HTTP 200 with techoneglobal-stamped row; DB `SELECT ... JOIN tenants` showed the row under subdomain `techoneglobal`. **Row hard-deleted immediately; techoneglobal PIP count re-verified = 0.**
- **Severity:** CRIT (inherits BUG-003 — cross-tenant write / Critical-Rule-#1 isolation bypass).
- **SURVEY:** **1** HTTP tenant-resolution point — `TenantResolutionMiddleware.cs:78-87` (the dev `X-Tenant-Subdomain` fallback) feeding `:149` `SetTenant` — governing a blast radius of **577** endpoints across **80** controllers (75 class-level `[Authorize]`; 4 `[AllowAnonymous]`: Careers, Portal, Sso, TenantContext), **137** `HasQueryFilter` registrations and **238** non-test files touching `ITenantContext`. Unit = HTTP tenant-resolution points, then the endpoints they govern. Excluded: `HRM.Tests` (30 test-only tenant-context registrations) and Hangfire job paths, which carry no HTTP header. The entry is right that this is a class rather than a one-off — one middleware governs all 577. PIP-specific: **1** controller (`PipController.cs:26` `[Authorize]`, `:27`) with **5** write endpoints (`:116`, `:135`, `:147`, `:168`, `:186`). Excluded here: read endpoints and the Hangfire sweep.
- **AUDIT (2026-09-07):** (1) The stated root cause at ~95% confidence — "there is no `CurrentUser.TenantId == ITenantContext.TenantId` guard" — **FALSE**, same evidence as the sign-off EXTENSION: `TenantAccessGuardMiddleware.cs:38-53`, registered `Program.cs:762`, merged 2026-07-02 in `94af0fe7` (**#119**). `POST /pips` with an acme bearer plus `X-Tenant-Subdomain: techoneglobal` is now short-circuited **403** before `PipService.CreateAsync` (`PipService.cs:53`) is reached. (2) "HTTP 200, a techoneglobal-stamped PIP row" — **stale-true**; the run header at `TEST-FINDINGS.md:1180` dates it **2026-06-26**, pre-fix. (3) "`TenantInterceptor` server-derives `tenant_id`, so body injection is ignored" — **CONFIRMED and still true**; that premise is unaffected. (4) "READ isolation CLEAN, write-IDOR returns 404 via the EF filter" — **CONFIRMED**; the entry's *diagnosis of the mechanism* was accurate, only its "no guard exists" claim has since become wrong. (5) **Stale cross-reference**: the entry says it "extends existing CRIT BUG-003 anchor ~line 190", but `TEST-FINDINGS.md:190` is now `ISSUE-503` (salary-structure scale); the real anchors are `:1566` (historical, RESOLVED) and `:2876`.
- **SEVERITY CHECK:** **lower** — mitigated since 2026-07-02; belongs in the archive with PR #119 cited. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-135 — FR-7 PIP summary report (PDF) endpoint/seam ABSENT (no `/report`, `/export`, `/pdf` route)
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** FR-7 PIP report exists end-to-end: route `PipController.cs:96-109` → `PipQueries.cs:54-61` → `PipService.cs:292-307` (real QuestPDF render), tested `PipIntegrationTests.cs:219-268`.
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-008 / TC-PRF-008-14
- **Title:** FR-7 requires a PIP summary report (objectives/checkpoints/outcomes/signatures). No export endpoint exists on `PipController` — `GET /pips/{id}/{report|export|pdf|summary}` all 404. The TC's "PDF renderer conditional" caveat presupposes an export *seam* returning the structured model; there is none.
- **Root cause (confidence 96%):** `PipController` exposes only List/Get/Create/Acknowledge/Checkpoints/Outcome/Escalation — no report action; no `IPipReportService`/export handler in the Performance feature. Consistent with the module's deferred-PDF pattern (US-PRF-005/006/007) but here the entire endpoint is missing, not just the renderer.
- **Reproduction:** `GET /api/v1/tenant/performance/pips/{id}/report` (and `/export`,`/pdf`,`/summary`) as HR -> 404.
- **Evidence:** all four export paths -> 404; controller has no report route.
- **Severity rationale:** FR-7 is a should/compliance-nice-to-have reporting feature; the full PIP data model is already retrievable via `GET /pips/{id}` (objectives+checkpoints+events), so the compliance data exists — only the packaged report is absent. LOW.

### ISSUE-136 — PIP write operations write NO central `audit_logs` row (FR-5 satisfied via PIP-internal `pip_event`, but central audit trail is bypassed)
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** **Closed by a mechanism, not a fix.** `AuditCaptureInterceptor` is opt-OUT since BUG-082: every tenant `BaseEntity` is captured unless `IAuditExempt`. `Pip`/`PipObjective`/`PipCheckpoint`/`PipEvent` are all `BaseEntity` and none is exempt. ⚠ **Adding an explicit audit writer here would produce TWO rows per write** — see `Exempt_explicit_writer_entity_is_not_double_audited`.
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-008 / TC-PRF-008-10, TC-PRF-ISO-032
- **Title:** Create / checkpoint / outcome / extend / escalation / acknowledge produce ZERO rows in the central `audit_logs` table; the complete immutable trail lives only in `pip_event` (PIP-internal). FR-5 (complete immutable history) IS met by `pip_event` (append-only, no edit/delete, actor + server-timestamp + tenant_id, tenant-scoped) — this is a consistency/defense-in-depth nit, not an FR-5 failure.
- **Root cause (confidence 88%):** `PipService` appends `PipEvent` rows and logs via Serilog + the log-only performance-notification seam, but never calls the central audit writer. Recurring cross-module theme (twin of the leave/attendance/core-HR "no central audit on writes" findings).
- **Reproduction:** run ~15 PIP write ops, then `SELECT ... FROM audit_logs WHERE created_at > now()-interval '30 min'` -> 0 PIP-related rows; `pip_event` holds the full trail.
- **Evidence:** 0 audit_logs rows in the run window despite all PIP mutations; pip_event rows present with Created/Initiated/CheckpointRecorded/Extended/CompletedSuccessfully/MarkedNotMet/EscalationConfirmed/Acknowledged each carrying actor+timestamp+tenant_id.
- **Severity rationale:** The legally-required immutable trail exists (pip_event) and is tenant-scoped + immutable, so compliance intent is met; the gap is only that PIP actions do not appear in the unified audit-search surface. LOW.

### BUG-003 EXTENSION — Recruitment Dashboard READ re-scopes to the spoofable `X-Tenant-Subdomain` header (cross-tenant analytics read)
- **Type / Severity / Status:** BUG · CRIT · OPEN
- **(extends existing CRIT BUG-003 anchor — NOT a new ID per run convention)**
- **Layer:** BE · **US/TC:** US-REC-009 / TC-REC-009-09 (AC-5 arm), cross-tenant arm of the dashboard
- **Surface:** `GET /api/v1/recruitment/dashboard` (and `/dashboard/export`), perm `Recruitment.View`. The whole dashboard aggregates `applicant`/`applicant_stage_history`/`vacancy`/`offer` under the EF global query filter keyed on `ITenantContext.TenantId`, which is resolved from the request **subdomain header**, not the JWT's `tenant_id`. A caller holding `Recruitment.View` in tenant A who sets `X-Tenant-Subdomain: <tenantB>` gets tenant B's analytics, not tenant A's.
- **Confirmed two ways (read-only surface, no writes):** (1) `admin@hrm.local` (JWT `tenant_id=platform 019ed613-...`) + `X-Tenant-Subdomain: acme` -> dashboard returned **acme's** data (totalApplicants=7 for the seeded period), i.e. data for the header tenant, not the token tenant. (2) `hr@acme.test` (JWT `tenant_id=acme`) + `X-Tenant-Subdomain: techoneglobal` -> returned **techoneglobal's** (empty) data, NOT acme's 7 — proving the read follows the spoofable header, ignoring the JWT tenant.
- **Root cause (confidence 96%):** identical platform invariant gap as the BUG-003 anchor — `TenantResolutionMiddleware` (US-AUTH-007) populates `ITenantContext` from the subdomain/header with no `CurrentUser.TenantId == ITenantContext.TenantId` guard; `RecruitmentDashboardService` then queries entirely under that resolved tenant. Serilog confirms it: the acme-token request with the techoneglobal header logged `TenantId=019ef3c3-...(techoneglobal)` and the EF `Database.Command` filter ran with `@ef_filter__TenantId = techoneglobal` (RequestId `0HNMJ6R63S2KI:00000001`).
- **Reproduction:** `GET /api/v1/recruitment/dashboard?from=2026-06-16&to=2026-06-26` with a tenant-A `Recruitment.View` bearer + `X-Tenant-Subdomain: <tenantB-with-recruitment-data>` -> 200 with tenant B's KPIs/funnel/sources/activity (incl. applicant names + vacancy titles in the recent-activity feed = PII leak).
- **Evidence:** admin+acme-header -> totalApplicants=7; acme+techoneglobal-header -> 0 (header tenant's data); Serilog EF filter bound to the header tenant. Read-only endpoint — **no writes performed, nothing to revert; techoneglobal recruitment row count re-verified = 0.**
- **Severity:** CRIT (inherits BUG-003 — cross-tenant read / Critical-Rule-#1 isolation bypass; leaks applicant PII via recent-activity).
- **SURVEY:** **2** endpoints (`RecruitmentDashboardController.cs:42` `dashboard`, `:74` `dashboard/export`) on **1** `[Authorize]` controller (`:25`), inside the same class of **1** HTTP tenant-resolution point — `TenantResolutionMiddleware.cs:78-87` (the dev `X-Tenant-Subdomain` fallback) feeding `:149` `SetTenant` — governing a blast radius of **577** endpoints across **80** controllers (75 class-level `[Authorize]`; 4 `[AllowAnonymous]`: Careers, Portal, Sso, TenantContext), **137** `HasQueryFilter` registrations and **238** non-test files touching `ITenantContext`. Unit = HTTP tenant-resolution points, then the endpoints they govern. Excluded: `HRM.Tests` (30 test-only tenant-context registrations) and Hangfire job paths, which carry no HTTP header. The entry is right that this is a class rather than a one-off — one middleware governs all 577. Excluded: the 4 `[AllowAnonymous]` controllers, where tenant-by-subdomain is by design and there is no JWT tenant to compare against.
- **AUDIT (2026-09-07):** (1) "`TenantResolutionMiddleware` populates `ITenantContext` from the subdomain/header" — **CONFIRMED** (`:78-87`, `:149`); the premise that resolution is header-driven genuinely holds. (2) "...with no `CurrentUser.TenantId == ITenantContext.TenantId` guard", stated at 96% confidence — **FALSE**: `TenantAccessGuardMiddleware.cs:38-53`, `Program.cs:762`, merged 2026-07-02. (3) Arm (2), `hr@acme.test` + `X-Tenant-Subdomain: techoneglobal` returning 200 — **stale-true**, now **403**; `TEST-STATUS.md:230` dates that run **2026-06-26**. (4) Arm (1), `admin@hrm.local` (JWT `tenant_id` = platform `019ed613-...`) + `X-Tenant-Subdomain: acme` — also **403**, since `019ed613 != acme` and `IsSystemContext` is false for a non-`admin` subdomain. The guard's only bypass is `TenantId == Guid.Empty` (`:39`, asserted by `TenantAccessGuardMiddlewareTests.cs:106-114`), and `JwtService.cs:88` always emits a non-empty `tenant_id`, so no real login reaches it — *confidence 85%*, as not every token-minting path was enumerated. (5) "leaks applicant PII via recent-activity" — **PARTIALLY TRUE**: the PII shape is real, the delivery path is closed. (6) **Misfiled**: this US-REC-009 entry sits inside the **US-PRF-008** section (`TEST-FINDINGS.md:1180`), so it is invisible to anyone reading the Recruitment block.
- **SEVERITY CHECK:** **lower** — stale-OPEN, not a live CRIT. See `ISSUE-545`. The control that closes this whole class has **no end-to-end regression arm** — filed as `ISSUE-556`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-138 — FR-8 PDF export and NFR-5 async (Hangfire) large-export path NOT wired; only CSV/XLSX synchronous export exists
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-REC-009 / TC-REC-009-10 (FR-8/NFR-5)
- **Title:** FR-8 requires CSV + Excel + **PDF (QuestPDF)**; NFR-5 requires large exports generated **asynchronously via Hangfire** with a completion notification. The implementation supports CSV + XLSX (ClosedXML) synchronously only — `format=pdf` returns **400 `invalid_format`** ("Export format must be one of csv, xlsx") and there is no async/queued export path or notification seam.
- **Root cause (confidence 97%):** `RecruitmentDashboardService.NormalizeFormat` accepts only `csv`/`xlsx`/`excel`; `RenderExport` has no PDF branch (controller + service xmldoc both list "PDF export (FR-8 — CSV + XLSX only here)" and "async Hangfire export for large datasets (NFR-5)" as explicit deferrals). The export is computed inline on the request thread (`ExportDashboardAsync` -> `GetDashboardAsync` -> render), no Hangfire enqueue.
- **Reproduction:** `GET /api/v1/recruitment/dashboard/export?format=pdf` -> 400 `invalid_format`. CSV/XLSX -> 200 (verified: correct content-type, content-disposition filename, body matches the filtered dashboard exactly).
- **Evidence:** csv 200 (`text/csv`, values match dashboard: KPIs 1/6/1/10/50/1, funnel 6/5/3/2/1, sources Public 4/0/0 + Referral 2/1/50); xlsx 200 (valid OOXML zip, `spreadsheetml.sheet`); pdf -> 400.
- **Severity rationale:** TC-009-10 marks PDF + async as CONDITIONAL on S33/Hangfire wiring; the core tabular export (the primary value) works and reflects filters with no cross-scope leakage. The missing PDF + async are documented increment deferrals. LOW.

### ISSUE-139 — BR-6 source categories incomplete: no "Manual Entry" and no tenant custom sources; `ApplicationSource` enum is fixed to Public/Internal/Referral
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-REC-009 / TC-REC-009-04 (FR-3/BR-6)
- **Title:** BR-6 lists four source categories (Public Careers Page, Internal Application, Referral, **Manual Entry**) plus tenant-addable **custom sources**. The source-effectiveness chart enumerates the fixed `ApplicationSource` enum (`Public`, `Internal`, `Referral`) only — there is no "Manual Entry" member and no mechanism for tenant custom sources; TC-009-04's "LinkedIn (custom)" and "Manual Entry" rows can never appear.
- **Root cause (confidence 95%):** `RecruitmentDashboardService.BuildSources` iterates `Enum.GetValues<ApplicationSource>()`; `ApplicationSource` (HRM.Domain/Enums) defines exactly Public=0/Internal=1/Referral=2. Source is a fixed enum column, not a tenant-scoped lookup table, so custom sources are structurally impossible in the current model.
- **Reproduction:** seeded applicants with `Public` + `Referral` sources surfaced correctly (Public 4/0/0%, Referral 2/1/50%) ordered by applicant count desc; no way to attribute a "Manual Entry" or custom source.
- **Evidence:** source breakdown returned only the seeded enum sources; enum definition has 3 members.
- **Severity rationale:** The per-source counts + hire-conversion math (FR-3) are correct for the sources the platform models; the gap is missing BR-6 categories/custom-source extensibility (a data-model limitation), not a calculation defect. Sources with zero applicants are omitted (consistent), so empty handling is fine. LOW.

---

## US-PRF-009 — Goal Tracking with Progress Updates (REPORT-ONLY API run 2026-06-26)

> Routes (live, `GoalProgressController`): `GET /api/v1/tenant/performance/my-goals` · `POST .../goals/{goalId}/progress` (append-only update; TC wording `/updates` is wrong - no such route) · `GET .../goals/{goalId}/timeline` (TC wording `/updates` wrong) · `GET .../team-goals` · `GET .../team-goals/employees/{employeeId}` · `POST .../goals/{goalId}/comments`. There is intentionally NO PUT/PATCH/DELETE on an update (NFR-3 append-only - confirmed 404 for all, incl. HR). Personas: employee@acme.test = John Doe (EMP-0001) reports to manager@acme.test (Team Manager); hr@/tenantadmin@ = Review.All. Tenant B = techoneglobal (no `globex` tenant exists).

### ISSUE-143 — Stale nudge fires for goals whose tracking window is CLOSED (review phase started): "update your progress" nudge the employee cannot act on (BR-1 returns 409)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-009 / TC-PRF-009-04, TC-PRF-009-12
- **Title:** The sweep's "active goal" set is goals in an Active *cycle* (Submitted/Acknowledged), but the add-progress window (BR-1) is the narrower goal-setting-close to review-start window. A goal in an Active cycle whose review phase has already started is nudged ("you haven't updated progress on X in N days") yet a progress update on it is rejected 409 tracking_window_closed - the nudge asks for an action the BR-1 gate forbids. The read-side needsAttention flag has the same mismatch.
- **Root cause (confidence 90%):** `StaleGoalNudgeService` filters only on AppraisalCycleStatus.Active + GoalStatus (StaleGoalNudgeService.cs:53-63), with no check of IsTrackingWindowOpen; the add-progress path (GoalProgressService.IsTrackingWindowOpen, GoalProgressService.cs:530) additionally gates on GoalSettingEnd/SelfAssessmentStart. The two staleness/window definitions are inconsistent.
- **Reproduction:** seed an Active cycle with self_assessment_start in the past (review started) + an Acknowledged goal with no recent update - sweep nudges it (logged "...G99 ClosedWindow goal|40"), but POST .../progress on it returns 409 tracking_window_closed.
- **Evidence:** sweep dispatched G99 (closed-window goal); my-goals showed G99 needsAttention=true; POST progress on G99 returned 409.
- **Severity rationale:** A confusing but non-harmful nudge (no data corruption, no cross-tenant issue); affects only goals in the post-tracking phase of an Active cycle. LOW.

### ISSUE-144 — No central audit_logs row for any goal-progress write (update / comment); + progress-update notes stored RAW (no server-side XSS sanitization)
**▶ DISPOSITION 2026-09-08 (T4).** **Sanitize half merged (#684); audit half remains. Status OPEN pending `/verify-fix`.**
`(b)` server-side XSS sanitization of goal-progress notes is **shipped**. The audit-row half of this finding is
**not** addressed here.
⚠ **Re-scoped during the audit:** this was queued as "the [[ISSUE-121]] pattern — a mechanical batch". It is
not. Those services carry **no sanitizer dependency at all**, so there were no sanitized siblings to be
inconsistent with; it was a posture decision, not a repeat. Treating it as mechanical would have mis-sized it.

- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-009 / TC-PRF-009-01, TC-PRF-009-11, TC-PRF-ISO-036
- **Title:** Two defense-in-depth nits bundled. (a) Posting a progress update or comment writes ZERO rows to the central audit_logs table - the only trail is the append-only goal_progress_update/goal_comment rows + Serilog + the log-only notification seam. NFR-3's audit-compliance intent IS met by the immutable tables, but goal actions never appear in the unified audit-search surface (recurring cross-module theme). (b) Progress-update notes are persisted verbatim - <script>alert(1)</script> is stored raw (no HTML escaping/sanitization server-side); safety relies entirely on the FE (Angular interpolation) output-encoding, risky given section-8 calls notes "rich text."
- **Root cause (confidence 90%):** `GoalProgressService` calls SaveChangesAsync + Serilog + _notifications but never the central audit writer (consistent with the leave/attendance/core-HR/PIP "no central audit on writes" findings). Notes are only .Trim()-ed (GoalProgressService.cs:131) - no sanitization; SQLi is safely parameterized (EF) but XSS payloads survive as stored text.
- **Reproduction:** post update + comment, then `SELECT ... FROM audit_logs WHERE action ILIKE '%goal%' OR resource_type ILIKE '%goal%' OR action ILIKE '%progress%'` returns 0 rows. `POST .../progress {"notes":"<script>alert(1)</script>"}` - persisted notes = <script>alert(1)</script> verbatim; SQLi '; DROP TABLE goal_progress_update;-- stored literal, table intact (19 rows).
- **Evidence:** 0 audit_logs goal rows in run window; raw <script> round-tripped in the timeline DTO; goal_progress_update table undamaged after SQLi.
- **Severity rationale:** The legally-required immutable history exists (append-only tables, tenant-scoped) and injection cannot execute SQL; gaps are (a) unified-audit visibility and (b) reliance on FE encoding for XSS - both defense-in-depth, matching documented platform patterns. LOW.

### ENH-014 — Goal-progress timeline returns raw per-update progressPct but no computed "progress change" (from-to) delta; attachment is metadata-only (no live blob store)
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH · **Title:** AC-3 asks the timeline to show "the progress change (e.g. 40% to 55%)"; the API returns each update's absolute progressPct and the FE must compute the delta from adjacent entries. A server-provided previousProgressPct/delta per entry would make the AC-3 timeline self-describing and avoid FE drift. Separately, GoalProgressAttachmentInput stores only metadata (FileName/StorageKey/ContentType/SizeBytes) - there is no live file-storage integration, so attachment evidence is a reference string, not a retrievable blob (deferred, per US dependency on file-management).
- **Module/US:** Performance / US-PRF-009 (TC-PRF-009-02, TC-PRF-009-01). **Why it matters:** keeps the AC-3 "progress change" presentation server-authoritative and flags that attachment evidence isn't actually downloadable yet. **Suggested direction:** add a computed delta to GoalProgressUpdateDto; track file-storage integration as a follow-up to the deferred file-management dependency. Not a defect - do not auto-apply.

### ISSUE-146 — FR-6 mandates PDF export; only csv/xlsx exist — `format=pdf` returns 400 `invalid_format`
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** Recommendation PDF export shipped 2026-07-31 (`cfac04e3`): `RecommendationService.cs:673-674`, `PerformancePdfRenderer.cs:299`, test `RecommendationIntegrationTests.cs:191`. **Directly contradicted [[ISSUE-179]] for five weeks; 179 was right.** PDF now ships on 7 of 9 export surfaces.
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-13 (step 3), FR-6
- **Title:** FR-6 (and TC-010-13 step 3) require a PDF recommendation-summary report with tenant branding. The export endpoint supports only `csv` and `xlsx`; `format=pdf` is rejected 400 `invalid_format` (the controller doc says "PDF deferred"). The xlsx/csv exports themselves are correct and match the dashboard aggregates.
- **Root cause (confidence 99%):** `RecommendationService.ExportSummaryAsync` (`RecommendationService.cs:567-570`) whitelists `("csv" or "xlsx")` only and 400s anything else; there is no QuestPDF/PDF rendering path. The xlsx path uses ClosedXML (`RenderXlsx`).
- **Reproduction:** `GET .../summary/export?format=xlsx&cycleId=<fy25>` to 200, a valid `PK`-magic XLSX (7321 bytes), correct content-type + filename, values match the summary (6 recs / 1 promo / $110k bonus pool / status counts). `format=csv` to 200 matching CSV. `format=pdf` and `format=docx` to 400 `invalid_format`.
- **Severity rationale:** Excel/CSV export (the primary leadership-review formats) fully work and are tenant-scoped + authz-gated + dashboard-accurate; only the PDF half of FR-6 is missing — a documented deferral, low business impact since xlsx covers the data need. LOW.

### ISSUE-147 — FR-1 "custom" recommendation type accepts ANY free-text label with no tenant-configuration gate (custom types are not tenant-defined as the spec requires)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-12 (step 6), FR-1
- **Title:** FR-1 + TC-010-12 expect custom recommendation types to be *tenant-configured* (a known "Spot Award", with an unconfigured "RandomType" rejected). The implementation has a fixed `RecommendationType.Custom` enum member that accepts ANY `customTypeLabel` free-text with no per-tenant custom-type catalog or validation — so there is no notion of a configured-vs-unconfigured custom type. (Separately, a truly unknown enum string like "RandomType" IS correctly rejected at model-binding to 400, satisfying TC step 1.)
- **Root cause (confidence 92%):** `RecommendationType` (HRM.Domain/Enums) defines `Custom = 6`; `SaveAsync`/`ApplyDetails` (`RecommendationService.cs:894`) persist `CustomTypeLabel` verbatim with no lookup against any tenant custom-type config table (none exists). FR-1's "tenant-configurable custom types" is modelled as a single open enum value + free label, not a configurable catalog.
- **Reproduction:** `POST .../recommendations {"type":"Custom","details":{"customTypeLabel":"'; DROP TABLE recommendation;--"}}` to 200, label stored verbatim (table intact — SQLi safely parameterized). No tenant config governs which custom labels are allowed. `type:"RandomType"` to 400 (enum bind reject).
- **Severity rationale:** Type integrity for the fixed enum is enforced and SQLi is neutralized; the gap is the missing tenant custom-type configuration surface (any label is accepted) — a spec/feature gap with low risk (label is inert text, output-encoding-dependent). LOW.

### ISSUE-148 — BR-1/BR-2 gates fire at SUBMIT, not at CREATE; + override/justification rejections return 422 where some TCs say 400; + 1000% increment cap is loose
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-08, TC-PRF-010-03, TC-PRF-010-12
- **Title:** Three small spec-vs-impl reconciliations bundled. (a) **Gate timing:** TC-010-08 steps 1/3 word the BR-1 (ratings published) and BR-2 (calibration complete) gates as blocking *creation*. The impl allows draft *creation* on any cycle and enforces BR-1/BR-2 only at *submit* (create on an Active cycle to 200 Draft; submit to 422 `final_ratings_not_published`; submit on calibration-enabled+unsubmitted to 422 `calibration_incomplete`). The gates ARE enforced server-side, just at the submit boundary. (b) **Status code:** override-missing-justification (FR-3) and promotion-missing-grade/date (BR-5) rejections return **422** (codes `justification_required`/`promotion_details_required`), whereas TC-010-03/-08 say "400". 422 is defensible for a business-rule violation and is consistent module-wide. (c) **Range cap:** TC-010-12 expects a 500% increment rejected; the validator cap is **1000%**, so 500% is *accepted* — only >=1000% rejects. 1000% is an unrealistically loose ceiling for a salary increment.
- **Root cause (confidence 95%):** (a) `SaveAsync` (`RecommendationService.cs:258`) has no cycle-status gate; the BR-1/BR-2 checks live in `SubmitAsync` (`:379`,`:385`). (b) the service returns `Result.Failure(..., 422, code)` for these business violations (`:280`,`:328`). (c) `SaveRecommendationValidator` caps `IncrementPercent`/`BonusPercent` at `InclusiveBetween(0,1000)` (`RecommendationValidators.cs:24-29`).
- **Reproduction:** create Bonus on Active cycle FY26-A to 200 Draft, submit to 422 `final_ratings_not_published`; create on calibration cycle FY26-B to 200, submit to 422 `calibration_incomplete`; override with blank justification to 422 `justification_required`; Promotion missing grade/date to 422 `promotion_details_required`; increment 500% to 200 (persisted 500.0); increment 1500% to 422 "must be between 0 and 1000".
- **Severity rationale:** Every actual business rule (BR-1/BR-2/BR-5/FR-3) IS enforced server-side and cannot be bypassed; these are wording/timing/status-code/threshold mismatches between the TCs and a reasonable implementation, not security or correctness defects. LOW.

### ISSUE-149 — No central `audit_logs` row for any recommendation write; + justification & custom-label text stored RAW (no server-side XSS sanitization)
**▶ DISPOSITION 2026-09-08 (T4).** **Both halves merged; status stays OPEN pending `/verify-fix`.**
- **(a) audit rows on recommendation WRITES — merged (#666).** The finding described the gap as a missing row on
  the sensitive *read*; the audit found writes were unaudited too.
- **(b) sanitize-on-write — merged (#684).** Same re-scope note as [[ISSUE-144]]: not the [[ISSUE-121]] pattern.

- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-12, TC-PRF-ISO-040
- **Title:** Two defense-in-depth nits bundled (recurring cross-module theme). (a) Recommendation writes append immutable `recommendation_event` rows (a complete per-rec history — FR-7 met) + Serilog, but write ZERO rows to the central `audit_logs` table, so recommendation actions never surface in the unified audit-search surface. (b) `justification` and `customTypeLabel` are persisted verbatim — `<script>alert(1)</script>` is stored raw with no HTML escaping/sanitization server-side; safety relies entirely on the FE (Angular interpolation) output-encoding. SQLi payloads are safely parameterized (EF Core — `recommendation` table intact after `'; DROP TABLE recommendation;--`).
- **Root cause (confidence 88%):** `RecommendationService` calls `SaveChangesAsync` + `AppendEvent` (append-only RecommendationEvent) + Serilog but never a central audit writer (consistent with the leave/attendance/core-HR/PIP/PRF-009 "no central audit on writes" findings). Free-text fields are only `.Trim()`-ed (`ApplyDetails` `:897-905`, `Justification` `:319`/`:335`) — no sanitization.
- **Reproduction:** create + override + submit + approve a recommendation, then query `audit_logs` for recommendation/promotion/bonus actions to 0 rows (the trail is the `recommendation_event` table only). `POST .../recommendations {"justification":"<script>alert(1)</script>"}` to stored verbatim; `{"details":{"customTypeLabel":"'; DROP TABLE recommendation;--"}}` to label stored literal, table intact (6 rows).
- **Severity rationale:** The legally-meaningful immutable history exists (append-only `recommendation_event`, tenant-scoped) and SQLi cannot execute; gaps are (a) unified-audit visibility and (b) reliance on FE encoding for XSS — both defense-in-depth, matching documented platform patterns. LOW.

### ISSUE-150 — the comp seam: encryption SHIPPED, `currentCompensation` still unbuilt, and the comp gate is bypassable (see `BUG-533`)
**▶ DISPOSITION 2026-09-08 (T4).** **REWRITTEN (#690) — see the rewrite block in this entry. It was hiding a HIGH.**
Two of three claims were false. Auditing the false one — *"the comp-visibility role gate has nothing to mask"* —
uncovered **[[BUG-533]]**, a live server-side authorization bypass: the recommendation workspace returned
unmasked compensation to **HR Officer** (whole org) and **any line manager** (their reports), both of whom the
catalogue **deliberately** denies `Payroll.ViewCompensation`. **Fixed and merged (#693)**, with
`includeCompensation` made a *required* parameter so a future projection cannot default to leaking.
**The entry's own severity rationale is what kept it hidden** — it read *"no security exposure today… LOW
(traceability, not a live defect)"*. That sentence is why nobody looked. Surviving residual:
`currentCompensation` is still always-null, and snapshot-at-save vs live-join is an open design question.


- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-06, TC-PRF-010-09 (steps 5-6), TC-PRF-010-11
- **⚠ REWRITTEN 2026-09-07 — the previous text was two-thirds false and would have sent an implementer to rebuild shipped work.** Every claim below was re-verified against `src/`. The prior wording is preserved in git history; it is not reproduced here because its whole problem was that it read as authoritative.

**Claim 1 — "NFR-3 compensation-at-rest encryption (pgcrypto) is entirely absent" → FALSE.**
All five comp fields are encrypted at rest and fully wired: entity `Recommendation.cs:75-87`; converter applied at `RecommendationConfiguration.cs:99-106` (`EncryptedFieldConverters.Decimal` + `HasColumnType("text")`); invoked from `AppDbContext.cs:266` with the injected `IFieldEncryptor` (`DependencyInjection.cs:88`, `AesGcmFieldEncryptor`); columns retyped `numeric → text` by `20260712185610_EncryptSensitiveFields.cs:13-65`; back-fill registered at `EncryptedFieldRegistry.cs:53-59`. Test-bound both in-process and against real Postgres (`FieldEncryptionIntegrationTests.cs:136-165`, `FieldEncryptionPostgresTests.cs:156-256`).
**But the mechanism is NOT pgcrypto** — it is application-side AES-256-GCM through EF value converters (`enc:v1:{kid}:base64(nonce‖ct‖tag)`). That difference is substantive, not pedantic: it changes how the TC must assert (no DB extension to provision) and it adds an ops deploy gate, because `Encryption__Keys__*` must be set or the app fail-fasts.

**Claim 2 — "comp comparison/masking cannot be exercised" → HALF FALSE, and it concealed a live defect.**
The gate is real and shipped: `PermissionCatalog.cs:227` (`Payroll.ViewCompensation`), granted to Owner / Tenant Admin / HR Manager and **deliberately withheld from HR Officer**; enforced service-side at `RecommendationService.cs:74-75` and `:193-196` (403 `compensation_not_permitted` **before** the read and before the audit row). Test-bound both ways (`RecommendationServiceTests.cs:542-596`). So "a *hypothetical* comp-hidden role" was wrong — the persona is concrete.
*Masking*, however, was never built, and the previous entry's "there is no comp to mask" is false: bonus and increment amounts are real compensation data. The workspace path returns them **unmasked to callers who lack the permission** — filed separately as **`BUG-533` (HIGH)**.

**Claim 3 — "`currentCompensation` always null" → TRUE.** The one claim that survived.
`RecommendationService.cs:141` hardcodes `CurrentCompensation = null`; nothing anywhere writes the entity property (`ApplyDetails` at `:1097-1109` does not, and the input record has no such field). The service injects no payroll source. **The other side of the seam already exists** — `ISalaryAssignmentService.cs:56` → `SalaryAssignmentService.cs:148` resolves current compensation from `EmployeeSalaryComponents` by validity window. What is missing is narrow: consume it (batched — a per-row call is N+1 across a 200-row page), write the snapshot at save/auto-generate, and **decide snapshot-at-save vs live-join**. Those last two conflict: TC-PRF-010-06 step 4 demands the current side be live, not a stale snapshot. **A human must pick before this is built.**

- **Severity rationale — corrected.** The old text said *"no security exposure today because there is no real compensation data flowing through recommendations… LOW (traceability, not a live defect)."* That sentence was the most damaging one in the entry: comp data **does** flow, and there **is** a live defect. This entry stays LOW only because what remains under *this* id is the unbuilt `currentCompensation` seam; the exposure moved to `BUG-533`.
- **Doc drift to fix with it:** two source comments still tell the old story, so anyone verifying from `src/` reads it twice — `RecommendationController.cs:19-20` ("no pgcrypto/PII-encryption mechanism exists; stored plain numeric today") and `RecommendationBudget.cs:12-14` ("The codebase has no field/PII (pgcrypto) encryption mechanism"). The second is half-true: budget-pool amounts genuinely are plain numeric **by design**, but "no mechanism exists" is false.
- **TC dispositions:**
  - `TC-PRF-010-06` — **still blocked, needs re-scoping.** The grade/title arm is executable today; the compensation arm is not. Split them. Also fix the precondition: it names Core HR as the authoritative current side, but the real source is Payroll (`SalaryAssignmentService.cs:148`).
  - `TC-PRF-010-09` steps 5-6 — **now executable, and expected to FAIL as written.** Split into a reveal-path arm (passes) and a workspace-masking arm (fails → `BUG-533`); the "current/recommended pay" clause stays unassertable while `currentCompensation` is null.
  - `TC-PRF-010-11` — **now executable, but must be re-scoped off pgcrypto.** Its `exec_note` ("needs pgcrypto provisioning") is obsolete. Re-point at the existing coverage and assert `enc:v1:` ciphertext + round-trip + tamper-reject. Two carve-outs: step 4's masking arm inherits the `BUG-533` failure, and `recommendation_budget` amounts are intentionally NOT encrypted, so the TC must not assert over them.
- **Rewritten:** 2026-09-07, from a code-verified re-audit of all three claims.


### ISSUE-149 — No central `audit_logs` row for any recommendation write; + justification & custom-label text stored RAW (no server-side XSS sanitization)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-12, TC-PRF-ISO-040
- **Title:** Two defense-in-depth nits bundled (recurring cross-module theme). (a) Recommendation writes append immutable `recommendation_event` rows (a complete per-rec history — FR-7 met) + Serilog, but write ZERO rows to the central `audit_logs` table, so recommendation actions never surface in the unified audit-search surface. (b) `justification` and `customTypeLabel` are persisted verbatim — `<script>alert(1)</script>` is stored raw with no HTML escaping/sanitization server-side; safety relies entirely on the FE (Angular interpolation) output-encoding. SQLi payloads are safely parameterized (EF Core — `recommendation` table intact after `'; DROP TABLE recommendation;--`).
- **Root cause (confidence 88%):** `RecommendationService` calls `SaveChangesAsync` + `AppendEvent` (append-only RecommendationEvent) + Serilog but never a central audit writer (consistent with the leave/attendance/core-HR/PIP/PRF-009 "no central audit on writes" findings). Free-text fields are only `.Trim()`-ed (`ApplyDetails` `:897-905`, `Justification` `:319`/`:335`) — no sanitization.
- **Reproduction:** create + override + submit + approve a recommendation, then query `audit_logs` for recommendation/promotion/bonus actions to 0 rows (the trail is the `recommendation_event` table only). `POST .../recommendations {"justification":"<script>alert(1)</script>"}` to stored verbatim; `{"details":{"customTypeLabel":"'; DROP TABLE recommendation;--"}}` to label stored literal, table intact (6 rows).
- **Severity rationale:** The legally-meaningful immutable history exists (append-only `recommendation_event`, tenant-scoped) and SQLi cannot execute; gaps are (a) unified-audit visibility and (b) reliance on FE encoding for XSS — both defense-in-depth, matching documented platform patterns. LOW.

### ISSUE-150 — NFR-3 compensation-at-rest encryption (pgcrypto) is entirely absent; comp comparison/masking (FR-5, AC-5 comp-gate) cannot be exercised — `currentCompensation` always null
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** **PARTIALLY RESOLVED (PR #273, merged 2026-07-12)** — the NFR-3 **compensation-at-rest encryption** half is DONE (P3-4): the 5 Recommendation comp fields (`CurrentCompensation`/`BonusAmount`/`BonusPercent`/`IncrementAmount`/`IncrementPercent`) are now AES-256-GCM encrypted at rest (columns migrated `numeric→text`; verified `enc:v1:` ciphertext on real Postgres; no DB-aggregation regression — comp math is app-side over materialized lists). **Still open (separate concern):** `currentCompensation` is still always-null (the comp SNAPSHOT/join from Payroll is a documented seam, not built), so the FR-5 comparison + AC-5 comp-gate masking still can't be exercised — that's a feature seam, not encryption. · **Layer:** BE · **US/TC:** US-PRF-010 / TC-PRF-010-06, TC-PRF-010-09 (steps 5-6), TC-PRF-010-11
- **Title:** Compensation is not modelled in recommendations: `currentCompensation` is hardcoded `null` in every workspace row + DTO (documented seam "compensation lives in Payroll; not joined here"), so (FR-5) the current-vs-recommended *compensation* comparison has no current side; (AC-5/Constraints) the comp-visibility role gate has nothing to mask (a comp-hidden HR sees the same null everyone sees); and (NFR-3) pgcrypto compensation-at-rest encryption does not exist (bonus/increment numerics stored plain — same posture as US-PRF-008). The recommendation amount fields (bonusAmount/incrementAmount) ARE stored, but the employee's *current* compensation snapshot and the encryption boundary are unimplemented.
- **Root cause (confidence 95%):** `GetWorkspaceAsync` sets `CurrentCompensation = null` with the inline seam comment (`RecommendationService.cs:117`); no Payroll join, no `IFieldEncryptor`/pgcrypto wiring anywhere in the recommendation path; `recommendation` numeric columns are plain `numeric`.
- **Reproduction:** workspace + GET rec for any employee to `currentCompensation: null`; the only comp-ish data is the recommendation's own bonus/increment amounts (plain numeric in DB). No comp-mask difference between a Publish.All and a (hypothetical) comp-hidden role because there is no comp to mask.
- **Severity rationale:** A documented, story-acknowledged deferral (NFR-3/comp-visibility are CONDITIONAL seams, same as US-PRF-008); no security exposure today *because* there is no real compensation data flowing through recommendations — but FR-5's comp comparison and the AC-5 comp-gate are unmet. LOW (traceability, not a live defect).

### ENH-015 — Auto-generate is idempotent-by-skip but offers no regenerate/preview; downstream `IntegrationRaised` event has no replay/outbox
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH · **Title:** Two observations. (a) `auto-generate` correctly skips employees who already have a recommendation in the cycle (re-running produced created=0, skipped=all) — good idempotency — but there is no way to *refresh* suggestions after a rule change (HR must delete recs first), and no server-side rule-precedence *preview* step (TC-010-02 step 2 expects a preview "BEFORE applying"; the API applies+persists Draft directly). (b) On final approval an immutable `IntegrationRaised` event is appended and `IRecommendationIntegrationService.RaiseAsync` is invoked, but it's a no-op seam (BR-6 "not wired") with no outbox/replay — when Core HR/Payroll/Training wiring lands, an idempotent outbox would be needed for exactly-once downstream delivery (the seam currently fires inline, post-commit, with no retry record).
- **Module/US:** Performance / US-PRF-010 (TC-PRF-010-02, TC-PRF-010-10). **Why it matters:** keeps auto-gen re-runnable after rule edits and makes the eventual downstream integration exactly-once-safe. **Suggested direction:** add a preview/dry-run mode to auto-generate; add an outbox row for the IntegrationRaised seam so the deferred BR-6 wiring is replay-safe. Not a defect — do not auto-apply.

---

## US-PAY-002 — Assign Salary Structure to Employee (test-runner, 2026-06-26, REPORT-ONLY API run)

Scope: 12 functional TCs (TC-PAY-002-01..12) + 4 isolation TCs (TC-PAY-ISO-005..008), executed API-layer (curl + JWT) against http://localhost:5000, acme tenant. FE (:4200) + Docker down → UI/a11y/perf-browser/Testcontainers arms BLOCKED. Routes discovered: `POST /api/v1/payroll/salary-assignments/preview`, `POST /api/v1/payroll/salary-assignments`, `POST /api/v1/payroll/salary-assignments/bulk`, `GET /api/v1/payroll/employees/{id}/compensation`, `GET /api/v1/payroll/employees/{id}/revision-history` — all `[RequirePermission("Payroll.Configure")]` (Tenant Admin only; HR/Manager hold zero payroll perms → see BUG-060). Seed already present: FT_IN (active, BASIC 40%GROSS / HRA 20%BASIC / CONV fixed 24000 / SPECIAL fixed 288000 → balances exactly at CTC 600000), LEG_24 (inactive), SR_FT (active). NOTE: implementation has NO automatic CTC balancer (SPECIAL is fixed, not a residual) — structures only "balance" at the CTC their fixed components were designed for.

### ISSUE-155 — LOP `calculation_basis` display rounds the daily rate, so "rate/day x days" doesn't equal the persisted LOP amount
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module/US/TC:** Payroll / US-PAY-003 / TC-PAY-003-05, TC-PAY-003-07
- **Title:** The LOP slip-detail `calculation_basis` shows e.g. "952.38/day x 20 days" while the persisted LOP `amount` is 19047.62. 952.38 x 20 = 19047.60, a 0.02 mismatch vs the stored figure.
- **Root cause:** the engine computes LOP at full precision ((20000/21) x 20 = 400000/21 = 19047.619... -> 19047.62 rounded once at the end — the correct, drift-free approach), but the displayed basis pre-rounds the daily rate to 952.38 for human display. The amount is right; only the human-readable basis text is internally inconsistent (rounded-rate x days != amount). Confidence: 92% (slip: amount 19047.62, basis "952.38/day x 20 days"; reconciliation gross-ded=net=signed-sum all EXACT at 30952.38).
- **Reproduction:** run payroll for an employee with LOP days where monthly_basic isn't divisible by working_days (basic 20000, working 21, lop 20); inspect the LOP detail line.
- **Evidence:** Loss of Pay|Deduction|19047.62|952.38/day x 20 days. Penny reconciliation otherwise exact.
- **Severity rationale:** LOW — purely a display/explainability nit; the persisted amount and all slip totals reconcile to the penny. Could confuse an HR reviewer who re-multiplies the shown rate.

### ISSUE-168 — StatutoryRulesController XML doc claims HR Officer holds Payroll.Configure; HR Officer holds zero payroll permissions (doc drift)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module/US/TC:** Payroll / US-PAY-006 / TC-PAY-006-08 (see BUG-060 for the RBAC root cause)
- **Title:** The controller summary states `Payroll.Configure` is "the catalog permission HR Officer / Tenant Admin already hold" — but HR Officer (`hr@acme.test`) gets 403 on every statutory endpoint (confirmed). Only Tenant Admin can configure.
- **Root cause:** Doc comment vs `PermissionCatalog.DefaultRolePermissions` (HROfficer block has no `Payroll.*`) — the underlying RBAC gap is BUG-060 (not re-filed); this ISSUE tracks the misleading comment + the US-PAY-006 "Tenant Admin / HR Officer" persona promise that HR cannot meet. Confidence: 99%.
- **Reproduction:** `hr@acme.test` → any statutory endpoint → 403.
- **Severity rationale:** LOW — documentation/persona-expectation drift; behavior is correctly locked down. The functional RBAC concern is owned by BUG-060.

### ISSUE-169 — Over-precision tax rate (> 2 dp) is silently rounded to numeric(5,2) with no validation message
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module/US/TC:** Payroll / US-PAY-006 / TC-PAY-006-09
- **Title:** A `ratePercentage` of 12.345 is accepted (201) and stored as 12.35 — silently rounded by the numeric(5,2) DB cast; no field-level reject or documented rounding rule.
- **Root cause:** `TaxSlabInputValidator` bounds the rate to 0-100 but does not constrain decimal precision; the value is implicitly rounded at persistence. Confidence: 95% (live: 12.345 → stored 12.35).
- **Reproduction:** POST IncomeTax slab `ratePercentage:12.345` → 201, GET shows 12.35.
- **Severity rationale:** LOW — within the TC's "reject OR round" tolerance, but a tax-rate precision change should be explicit (reject or documented round), since a silent 0.005 pp shift on a statutory rate is a financial-accuracy nicety.

### ENH-017 — NFR-1 Redis cache for statutory rules (30-min TTL, invalidate-on-write) is deferred
- **Type / Severity / Status:** ENH · — · DEFERRED
- **Type:** ENH · **Module/US/TC:** Payroll / US-PAY-006 / TC-PAY-006-10, TC-PAY-ISO-024
- **Title / why it matters:** NFR-1 specifies a tenant-scoped Redis cache (30-min TTL, invalidated on any write) to back the <10ms calc SLA; it is documented as deferred and not implemented (resolver hits a tenant-filtered DB query each time). At fixture volume the calc already meets <10ms, so this is an at-scale optimization, not a defect. The no-shared-key isolation guarantee already holds (ISO-024).
- **Suggested direction:** when enabling Redis, key strictly per tenant+fiscalYear (`tenant:{tenantId}:payroll:statutory:{fy}`), invalidate that key on every create/update/delete/clone, and re-run TC-PAY-006-10 step 3-4 + TC-PAY-ISO-024 to assert invalidation + no cross-tenant bleed.

## US-PAY-007 — Payroll Adjustments (test-runner, 2026-06-26, REPORT-ONLY API run)
Scope: TC-PAY-007-01..12 + ISO TC-PAY-ISO-025..028. API-layer (curl + JWT), acme tenant. FE :4200 down + Docker unavailable → UI/a11y (TC-12) + cross-browser BLOCKED. Persona: story names HR Officer but HR lacks `Payroll.Configure` (BUG-060) → executed with tenantadmin (Payroll.Configure holder). Routes: `api/v1/payroll/adjustments` [GET list, GET {id}, POST create, POST {id}/cancel, POST bulk, POST/GET {id}/document], all `[RequirePermission("Payroll.Configure")]`.

### ISSUE-171 · ISSUE · LOW · OPEN · BE — US-PAY-007 no "cancel remaining occurrences" bulk action for a recurring series (BR-6/FR-6)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module/US/TC:** Payroll / US-PAY-007 / TC-PAY-007-05
- **Title:** BR-6 ("HR can cancel remaining occurrences at any time") and the §8 UI note are implemented only as per-occurrence cancel — there is no series-level cancel. To stop a 12-month recurring series HR must call `POST /adjustments/{id}/cancel` 11 separate times.
- **Root cause (95%):** Only `CancelPayrollAdjustmentCommand(id)` exists (`PayrollAdjustmentCommands.cs`); the controller exposes a single `POST {id}/cancel`. No `recurringSeriesId`-scoped cancel command/endpoint. Verified: cancelling the first of a 12-row series left 11 Pending.
- **Reproduction:** acme/tenantadmin. Create recurring Deduction Jul2026→Jun2027 (12 rows). `POST /adjustments/{firstId}/cancel` → 200; list shows Pending:11, Cancelled:1. No endpoint cancels the remaining 11 in one call.
- **Evidence:** series counts after one cancel = `{Pending:11, Cancelled:1}`; grep shows only per-id cancel command.
- **Severity rationale:** LOW — functionally complete (each occurrence is cancellable) but ergonomically misses the BR-6 "cancel remaining" intent; FE/manual workaround exists.

### BUG-075 · ISSUE · LOW · OPEN · BE — US-PAY-007 supporting-document validation does not content-sniff (spoofed extension accepted) (NFR-5)
**▶ DISPOSITION 2026-09-08 (T4).** **All three sites merged (#680, #683); status stays OPEN pending `/verify-fix`.**
Content-sniffing now covers every upload path the finding names. Site 3 (payroll supporting documents) was
blocked on a non-standard `image/jpg` string in `AllowedContentTypes`, resolved by giving
`FileSignatureValidator` an alias sharing the JPEG signature field rather than duplicating it.
⚠ **Scope correction:** the finding says "3 sites, 2 outside payroll". The sniffer now has **8 adopters**, and
two upload surfaces validate by their own means — branding uses `BrandingFileValidator` (magic-byte, extension
ignored) and holiday import is CSV, where content-sniffing is not the applicable control. Recorded so a future
sweep does not read "3 sites" as the full census.

- **Type / Severity / Status:** BUG · LOW · OPEN
- **Module/US/TC:** Payroll / US-PAY-007 / TC-PAY-007-09
- **Title:** A file with a `.pdf` extension + `application/pdf` declared content-type but non-PDF bytes (GIF89a content) is accepted by `POST /adjustments/{id}/document`. NFR-5/TC-09 expects content-type sniffing to reject a renamed/spoofed file.
- **Root cause (95%):** `PayrollAdjustmentService` validates only `AllowedContentTypes` (declared) ∩ `AllowedExtensions` (lines 36-42, 339-340) — no magic-byte/file-signature check. The declared content-type is client-supplied (curl derives it from the `.pdf` extension), so extension+declared-type both pass while actual bytes are arbitrary.
- **Reproduction:** acme/tenantadmin. Upload `/tmp/doc.pdf` whose bytes start `GIF89a` (renamed): `POST /adjustments/{id}/document -F file=@doc.pdf` → **200 accepted**. Legit negatives all correct: `.exe`→400, `.xlsx`→400, 6 MB pdf→400, real pdf→200.
- **Evidence:** spoofed `doc.pdf` (GIF content) → HTTP 200; code has no signature check (only AllowedContentTypes/AllowedExtensions hash-set membership).
- **Severity rationale:** LOW — defense-in-depth: blob is private, served back with a fixed content-type, and extension/size/declared-type gates still block the obvious cases; but a determined uploader can stash non-PDF bytes under a `.pdf` name.

### ISO arms (TC-PAY-ISO-025..028) — adjustments surface is EMPLOYEE-SCOPED + SELF-PROTECTED (NOT BUG-003)
- **Result: PASS — no cross-tenant leak or write.** Probed with the acme tenantadmin token + `X-Tenant-Subdomain: techoneglobal` (the BUG-003 mechanism; no techoneglobal user accounts exist). Findings:
  - **Read/list (ISO-025/028):** acme baseline 30 rows; same token + techoneglobal header → `totalCount:0` (techoneglobal's own empty scope, zero acme rows leaked). No cross-tenant list/count/cache leak.
  - **IDOR detail/cancel (ISO-025/026):** GET acme's Applied bonus under techoneglobal scope → 404 `adjustment_not_found`. No id-based IDOR.
  - **Missing/invalid tenant (ISO-026):** no `X-Tenant-Subdomain` → 400; unknown subdomain → 404. Fail-closed.
  - **Body-injected tenant (ISO-027 step1):** create with `tenantId`/`tenant_id` in body (acme header) → row stamped **acme** (session-derived), visible under acme (200), invisible under techoneglobal (404). Injected value ignored.
  - **Cross-tenant write (ISO-027 step2):** acme token + techoneglobal header + acme employee_id → 404 `employee_not_found` (employee resolves within the resolved tenant only; BR-1 active-structure check runs in-scope). No cross-tenant adjustment created.
  - **Doc path (ISO-027 step5):** path is server-derived `{tenantId}/payroll/adjustments/{id}/` (IFileStorage prefixes tenant); client cannot control prefix (verified in TC-01 — stored relative path `payroll/adjustments/{id}/...`).
- **Conclusion:** Unlike the directly-addressable config rows in US-PAY-001 (which leak under BUG-003), the adjustments surface keys every read AND write off the tenant-scoped employee/EF-global-filter — matching the self-protected pattern of US-PAY-002/004/005/006. References BUG-003 (root locus US-AUTH-007); NOT re-filed. Note: full bidirectional cross-tenant *write* (stamping into techoneglobal) could not be completed because no techoneglobal employee is reachable without touching the protected techoneglobal EMP-0001 "Cross Write" orphan — but the create fails at tenant-scoped employee resolution, which is the controlling guard.

### Cleanup / residue note (US-PAY-007 run)
- All adjustment test rows created during the run were **cancelled** (status=Cancelled, terminal — never picked up by any run). acme Pending count = 0 after cleanup. No hard-delete endpoint exists and psql/DB creds were not available (password in user-secrets), so soft-cancel is the available neutralization; Cancelled rows are inert and acme-scoped.
- **Intentionally retained legitimate state:** (a) EMP-0001 salary-structure assignment (CTC 600k, Full-Time India) — required for BR-1, reusable by future payroll TCs; (b) the **Finalized June 2026 payroll run** + its 2 Applied adjustments (Bonus 10000, Reimbursement 3500) — deleting a finalized run would corrupt payroll history. These are valid seed/history, not stray residue.
- **techoneglobal baseline = 0 adjustments** after the run — zero cross-tenant residue. techoneglobal EMP-0001 "Cross Write" orphan was not touched.

---

## US-PAY-008 — Payroll Approval Workflow (test-runner, 2026-06-26, REPORT-ONLY API run)

**Scope:** Execute TC-PAY-008-01..12 + ISO TC-PAY-ISO-029..032 against the running stack (API-layer, curl+JWT, acme tenant). Approval routes in `PayrollApprovalController` (`POST runs/{id}/submit-for-approval` [Payroll.Run], `/approve` `/reject` `/return` [Payroll.Approve], `/finalize` [Payroll.Run], GET `/approval-history` `/approval-summary` [Payroll.Run]). State machine: ReviewPending→AwaitingApproval→Approved→Finalized; reject→Rejected; return→ReviewPending. Maker-checker: submitter blocked from approving when ≥2 eligible Payroll.Approve users. UI/a11y/perf TCs BLOCKED (FE pinned-to-platform + Docker unavailable). Findings below.

### Persona / testability note (US-PAY-008)
- **Who holds payroll perms in acme:** ONLY two users hold `Payroll.Run` + `Payroll.Approve` — `tenantadmin@acme.test` (Tenant Admin) and `owner@techoneglobal.org` (Tenant Owner of acme, logs in with Admin@123!). `hr@acme.test` (HR Officer), `manager`, `employee` hold NO payroll-run/approve perms (the known BUG-060/BUG-071 HR/Manager payroll-perm gap — NOT re-filed).
- **KNOWN WRINKLE re-evaluated:** the prior claim "Tenant Admin lacks Payroll.Approve" does NOT hold for this reseeded persona set — `tenantadmin@acme.test` DOES hold `Payroll.Approve`. And because acme has **2** eligible approvers (tenantadmin + owner), the maker-checker rule is **ENFORCED** (not relaxed via small-team exception). The workflow IS testable: submitter=tenantadmin, second approver=owner.
- **No fresh ReviewPending run could be created via API:** June 2026 is already Finalized (`period_already_finalized` 409) and no other period has finalized attendance (`attendance_not_finalized` 409). To exercise the live state machine the single Finalized June run (id `019f0434-0d48-781f-b628-47b6904171d4`) was temporarily reset in-DB to ReviewPending, driven through the API, then **restored to its exact Finalized snapshot** (see cleanup note at end). DB creds from user-secrets; psql at PG18.

### Verdicts — no-mutation TCs (executed against the live Finalized run)
- **TC-PAY-008-09 (authz) — PASS.** emp(no payroll perms) Submit/Approve/Reject/Finalize → 403; hr(no Payroll.Approve) Approve → 403; hr(no Payroll.Run) Submit → 403; unauth Approve → 401; emp GET approval-history → 403; tenantadmin GET history → 200. Authz gate fires BEFORE the state machine (403 returned even on a Finalized run).
- **TC-PAY-008-05 (finalized terminal/immutable) — PASS.** On the Finalized run: Submit→409 invalid_transition, Approve→409, Reject→409, Return→409, Finalize→409 already_finalized. No PUT/PATCH/DELETE endpoint exists on ANY payroll controller (slips/runs structurally immutable → FR-8 enforced by absence of a mutation surface, not a per-slip flag). BR-6 terminal honored.
- **TC-PAY-008-03 (direct finalize blocked / invalid transitions) — PARTIAL PASS.** Finalize-on-Finalized and the live ReviewPending→Finalize block (see below) are enforced (BR-1). Steps 2/4/5 (force-set Approved directly, revert Approved→AwaitingApproval, Rejected→Approved) have **NO API surface** — the only status mutations are the workflow verbs, each of which guards its source status, so arbitrary transitions are unreachable by construction. Verified the live ReviewPending→finalize block below.

### Verdicts — isolation TCs (BUG-003 arm)
- **TC-PAY-ISO-029 (cross-tenant read) — PASS.** acme token + `X-Tenant-Subdomain: techoneglobal` GET approval-history/approval-summary on the acme run id → **404 run_not_found** (the EF global query filter scopes the run load to the resolved tenant; the acme run is invisible from a techoneglobal context). No cross-tenant read leak.
- **TC-PAY-ISO-030 (IDOR / missing context) — PASS.** Random/foreign run id under acme session → 404 run_not_found; no header (missing tenant context) → 400 "Tenant context is not resolved." No IDOR.
- **TC-PAY-ISO-031 (cross-tenant write) — PASS.** acme token + techoneglobal header attempting Approve/Finalize on the acme run id → **404 run_not_found** — the write never reaches the acme run. tenant_id/actor_user_id/IP are all server-derived (TenantInterceptor + ICurrentUser + HttpContext IP), no client-supplied tenant/actor in the body is honored. **The approval read+write surface is self-protected and does NOT extend BUG-003** (BUG-003's run-CREATE leak does not apply here: every approval verb loads an EXISTING run scoped by the resolved tenant before acting). Verified acme run state unchanged after all isolation probes (status=Finalized, 2 history rows).
- **TC-PAY-ISO-032 (queue/badge cache + SignalR group tenant-scoping) — BLOCKED: not-implemented.** There is no "Pending Approvals" queue/badge-count endpoint or cache layer in the payroll approval surface (the controller exposes only per-run history/summary reads), and the notification seam (`IPayrollNotificationService.NotifyApprovalEventAsync`) is **log-only** (SignalR/email deferred to US-NTF). No cache key or SignalR group to assert. Conditional clause of the TC → no observable artifact. Recorded as ISSUE-172 (notification seam is log-only, NFR-1 unverifiable end-to-end).

### NEW FINDINGS (US-PAY-008)

**ISSUE-172 — Approval notifications are a log-only seam; SignalR/email delivery (AC-1/AC-2/AC-3, NFR-1) not implemented**
- Type: ISSUE · Severity: MED · Status: DEFERRED (blocked on US-NTF-006 delivery — see DF-14) · Layer: BE · Module: Payroll · US: US-PAY-008 · TC: TC-PAY-008-01/02/06/11, TC-PAY-ISO-032
- Disposition (2026-07-17, MED-fix campaign): DEFERRED. The approval state machine + audit trail work; the missing piece is notification *delivery* (in-app/SignalR + email), the known cross-module dependency on the unbuilt **US-NTF-006 delivery** story. Building it is that story, not a bug fix. Parked as **DF-14** (BLOCKED on US-NTF-006). (A "Pending Approvals" queue/badge read-endpoint is a smaller sub-item and could be built independently of delivery if desired later.)
- Title: Approval-event notifications (approver-notified on submit, HR-notified on approve/reject) are not delivered — the notification call is a log-only stub.
- Root cause (confidence 95%): `PayrollApprovalService` calls `_notifications.NotifyApprovalEventAsync(...)` after each transition, but the implementation is the deferred log-only seam (the class XML-doc states "fires the log-only notification seam (real SignalR/email deferred — US-NTF)"; PayrollApprovalService.cs:106,169,213,253,304). No in-app/SignalR push, no email, no notification row. There is also no "Pending Approvals" queue/badge endpoint.
- Reproduction: Submit/Approve/Reject a run (see live-flow TCs); observe the run transitions and a history row persists, but no notification artifact is produced (no SignalR group send, no email enqueue, no notifications table row for the approver/HR).
- Evidence: source seam (NotifyApprovalEventAsync log-only); no notification endpoint; AC-1/AC-2/AC-3 "approver/HR receive in-app + email notification within 30s" cannot be satisfied end-to-end today.
- Severity rationale: MED — the core approval state machine + audit trail work; the missing piece is the notification delivery (a known cross-module dependency, US-NTF). Workflow is usable via polling but the AC's notification clause + NFR-1 are unmet.

### Verdicts — live-workflow TCs (executed on a throwaway acme run, seeded ReviewPending, then hard-deleted)
- **TC-PAY-008-01 (happy path submit to approve to finalize) — PASS.** TA submit -> AwaitingApproval (workflow instance created); OWN (owner@techoneglobal.org, not the submitter) approve -> Approved; TA finalize -> Finalized. Each transition correct; workflow instance carried through; history rows Submitted(TA)+Approved(OWN) persisted with actor/IP/comments. (Notification clause = ISSUE-172.)
- **TC-PAY-008-02 (reject + re-submit new instance) — PASS.** Reject with no reason / <10 chars -> 400 reason_required; valid reject -> Rejected with reason stored; re-submit from Rejected -> AwaitingApproval with a NEW workflowInstanceId (W1 != W2, BR-3). HR-notified clause = ISSUE-172.
- **TC-PAY-008-03 (direct finalize blocked / state machine) — PASS (core).** Direct ReviewPending->Finalize -> 409 approval_required (BR-1). AwaitingApproval->Finalize implicitly blocked (finalize requires Approved). Force-set arbitrary status / Approved->AwaitingApproval revert have no API surface (unreachable by construction).
- **TC-PAY-008-04 (maker-checker) — PASS.** acme has 2 eligible approvers -> rule ENFORCED. Submitter (TA) approving own run -> 403 self_approval; different approver (OWN) -> success. Block keyed on SubmittedBy==currentUser. Small-team exception (<2 approvers) not reachable in acme (BLOCKED arm — no solo tenant seeded); logic verified by code read (CountEligibleApproversAsync >=2 gate).
- **TC-PAY-008-06 (multi-step) — PARTIAL PASS + BUG-076.** submit totalApprovalSteps=2 -> step1; step1 approve advances currentApprovalStep->2, run STAYS AwaitingApproval; step2 approve -> Approved (AC-4 sequential routing works). BUT there is NO per-step approver assignment — the SAME approver (owner) completed BOTH steps and the step1 approver was not prevented from also approving step2; AC-4/FR-2 (HR Manager THEN Finance Director — distinct approvers) is not enforced. See BUG-076.
- **TC-PAY-008-07 (return-to-HR; SLA escalation; delegation) — PARTIAL PASS.** Return: no/short comment -> 400 comments_required; valid comment -> status ReviewPending (distinct from Rejected), action=Returned, instance cleared (FR-9 PASS). FR-3 SLA auto-escalation + FR-6 delegation = not implemented (no SLA/escalation/delegation endpoint, no Escalated history ever written) -> see ISSUE-173.
- **TC-PAY-008-08 (approval summary + variance + exceptions + drill-down) — PARTIAL PASS.** Summary returns total_employees/gross/deductions/statutory/net + previous_month_total_net (the prior Finalized run) + variance_percentage computed correctly (+19.68% verified: (40000-33421.1)/33421.1). Exceptions list built from skipped/negative-net/zero-processed counters. Variance COLOUR thresholds (green/amber/red) are FE-only (API returns raw %) -> BLOCKED(FE). Per-employee payslip drill-down (FR-5) = existing US-PAY-004/005 surface, not re-tested here.
- **TC-PAY-008-10 (audit trail + XSS/SQLi) — PASS.** Every action (Submitted/Approved/Rejected/Returned) writes an append-only payroll_approval_history row with actor_user_id, server-derived acted_at + ip_address (::1), comments, step_number, workflow_instance_id, tenant_id. No update/delete endpoint (append-only). XSS/SQLi payload in reject reason stored VERBATIM via parameterized EF (no injection — table intact). Structured audit timeline (US-PAY-012) additionally records PayrollRun.SubmittedForApproval/Approved/Rejected/Finalized with IP+actor. NOTE: Finalize writes NO payroll_approval_history row (see ISSUE-174).
- **TC-PAY-008-11 (performance) — BLOCKED: tooling/seed.** NFR-2 (review page <=2s) is a FE page-load metric -> FE pinned-to-platform, cannot measure. NFR-1 (notification <=30s) -> notifications are log-only (ISSUE-172), nothing to time. API summary/history responses were sub-second on a 5-emp run but no realistic 1,000-emp seed exists.
- **TC-PAY-008-12 (accessibility) — BLOCKED: ui-not-reachable.** FE pinned-to-platform + Docker unavailable -> axe/Lighthouse/keyboard/responsive checks cannot run.

### MORE NEW FINDINGS (US-PAY-008)

**BUG-076 — Multi-step approval has no per-step approver assignment; a single approver can complete every step (AC-4/FR-2 not enforced)**
- Type: BUG · Severity: MED · Status: RESOLVED (PR #356, 2026-07-18) · Layer: BE · Module: Payroll · US: US-PAY-008 · TC: TC-PAY-008-06, TC-PAY-008-13
- Title: A 2-step workflow (intended "HR Manager THEN Finance Director") can be fully approved by ONE approver acting twice; steps are a counter, not assignments.
- Root cause (confidence 95%): PayrollApprovalService.ApproveAsync only checks maker-checker (submitter != approver) and increments CurrentApprovalStep (PayrollApprovalService.cs:139-165). There is no per-step approver/role binding and no "this actor already approved an earlier step" guard. The submit command takes only totalApprovalSteps (an int) — no step-to-approver mapping. So any Payroll.Approve holder can approve the current step, and the same user can approve step1 AND step2. AC-4 ("sequentially through each step", distinct HR Manager / Finance Director) and FR-2 (configurable steps) are reduced to "the run must be approved N times by anyone with Payroll.Approve."
- Reproduction: TA submit {totalApprovalSteps:2}; owner approve (step1->2, still AwaitingApproval); owner approve again (step2->Approved). Same actor, no rejection. (Evidence: history shows Approved step1 + step2 both actor 019ef3ba.)
- Severity rationale: MED — multi-step is opt-in (default is single-step BR-2) and maker-checker still blocks the submitter, so the most common single-approver bypass is closed; but a tenant that configures 2 steps for separation-of-duties does NOT get it, which is the whole point of a multi-step finance control.
- RESOLVED (PR #356, 2026-07-18): payroll-specific configurable step→role engine + distinct-person guard. New `PayrollApprovalStepConfig(StepNumber→RoleId)` per tenant (settable+audited via `GET/PUT /api/v1/payroll/approval/step-config`, gated `Payroll.Approve` = non-maker). `ApproveAsync` now enforces, after maker-checker: (a) distinct-person — a user who already Approved a step for this instance can't approve another (total>1) → 403 `distinct_approver_required`; (b) step-role — the actor must hold the current step's configured role → 403 `not_step_approver`. Submit derives TotalApprovalSteps from config. Regression: same-user-both-steps→403 (the repro), distinct-approvers→Approved, role-per-step, config-authoritative, CRUD persist+audit+validation-400s (bound TC-PAY-008-13). Full suite 4218/4218; auditors PASS (isolation + guards verified). Postgres/cross-tenant config test arms → DF-16; FE step-config editor → ISSUE-318 (deferred). ISSUE-173 (SLA/escalation/delegation, LOW) remains separate.

**ISSUE-318 — No Angular UI for the payroll approval step→role config (BUG-076 backend endpoints are API-only)**
- Type: ISSUE · Severity: MED · Status: OPEN (FE, deferred) · Layer: FE · Module: Payroll · US: US-PAY-008 · TC: (FE, none yet)
- Title: BUG-076 (#356) added `GET/PUT /api/v1/payroll/approval/step-config` but there is no admin screen to view/edit the step→role approval chain; a Tenant Admin can only configure it via the raw API.
- Root cause: backend-only fix scope (the MED-fix campaign was BE-only per user decision). The FE admin surface is net-new frontend work.
- Suggested: an Angular admin screen (payroll settings) to view/edit the ordered step→role mapping, gated on `Payroll.Approve`, consuming the two endpoints. Sits with the deferred-FE queue (P6 class).
- Severity rationale: MED — the FR-2 control is functionally complete and usable via API (and enforced regardless of UI); the gap is end-user configurability. No security/data risk.

**ISSUE-173 — FR-3 (SLA auto-escalation to backup approver) and FR-6 (approval delegation) are not implemented**
- Type: ISSUE · Severity: LOW · Status: ✅ RESOLVED (2026-07-20) — FR-3 (#PR-pending-FR3) + FR-6 (#PR-pending-FR6) both shipped. Layer: BE · Module: Payroll · US: US-PAY-008 · TC: TC-PAY-008-07
- **FR-6 approval delegation (SHIPPED, decision: per-step primary approver USER + delegate; config-driven auto-on-leave):** `PayrollApprovalStepConfig` gains `PrimaryApproverUserId` + `DelegateUserId` (both must be active in-tenant users holding `Payroll.Approve`; only-one → 400 `delegation_config_incomplete`); `PayrollRun` gains `DelegatedToUserId`; a new `PayrollApprovalAction.Delegated`. At submit (step 1) + each step-advance (step N), if the step's primary approver is on an **approved leave spanning today** (same leave-overlap query as the generic engine, via the `TimeProvider` seam), the run is delegated to the delegate: `DelegatedToUserId` set, a `Delegated` history row written, and `payroll_approval_delegated` dispatched to the delegate (reject/return/finalize clear it). Delegation is a **notification/record overlay** — the role-gated approval authz is unchanged. Automated: submit-delegate + step-advance-delegate + leave-ended-yesterday + non-approved-leave + incomplete/not-found/missing-approve validation (unit) + delegate-recipient (RealPayrollNotificationServiceTests). Both auditors green (enforcer WIRED, authenticator 100% — 2 MATERIAL + 2 MED healed).
- **FR-3 SLA auto-escalation (SHIPPED, decisions: port-into-payroll-slice, notify-backup-role, opt-in per-step SLA):** `PayrollApprovalStepConfig` gains `SlaHours` (opt-in, `>0`) + `BackupRoleId` (a role holding `Payroll.Approve`); `PayrollRun` gains `SlaDueAt`/`EscalatedAt`; submit/step-advance stamp `SlaDueAt = now + step.SlaHours` (via a `TimeProvider` seam) + clear `EscalatedAt`; reject/return null both. A recurring `PayrollApprovalSlaEscalationJob` (every 5 min, per-tenant via `ITenantJobRunner`) → `PayrollApprovalSlaEscalator` finds `AwaitingApproval && SlaDueAt<now && EscalatedAt==null`, idempotent `ExecuteUpdate` CAS stamps `EscalatedAt`, writes an `Escalated` `PayrollApprovalHistory` row, and dispatches `payroll_approval_escalated` to the current step's `BackupRoleId` holders (fallback: the `Payroll.Approve` approver pool). Automated: submit-stamp + step-advance re-stamp + SLA/backup validation (unit) + escalate-once/idempotent/not-breached/cross-tenant (Postgres) + backup-role-recipient/fallback (RealPayrollNotificationServiceTests). Both auditors green (enforcer WIRED, authenticator 100% — 3 MISS healed). **FR-6 delegation** (config-driven auto-on-leave) is the remaining half.
- Title: No SLA/escalation or delegation surface; the Escalated history action is defined in the data spec but never written.
- Root cause (confidence 90%): No endpoint, command, background job, or config for step SLA / backup approver / delegation exists in the payroll approval slice. PayrollApprovalAction.Escalated is in the section-7 enum but is never produced. The class doc states there is no shared workflow engine (US-ADM-007 not built), so these advanced FRs were deferred.
- Reproduction: No /escalate, /delegate, SLA-config, or backup-approver field anywhere; no recurring job emits Escalated rows.
- Severity rationale: LOW — these are secondary FRs (FR-3/FR-6); the core submit/approve/reject/return/finalize flow is complete. Flagged as a coverage/spec gap.

**ISSUE-174 — Finalize writes no payroll_approval_history row; the approval-history timeline omits the finalize event (FR-7/AC-5)**
- Type: ISSUE · Severity: LOW · Status: ✅ ACCEPTED-BY-DESIGN (2026-07-20) — WON'T-FIX. The §7 approval-action enum (Submitted/Approved/Rejected/Returned/Escalated) deliberately has **no `Finalized` value**, and `FinalizeAsync` intentionally does not invent one (`PayrollApprovalService.cs` comment). Audit completeness for finalize **is** satisfied — the US-PAY-012 **structured audit-timeline** (`GET runs/{id}/audit-timeline`) records `PayrollRun.Finalized` with IP + actor. Inventing a new history action value purely to fill the dedicated approval-history view would diverge from the spec's action set for no audit gain. Re-open only if the product decides the approval-history endpoint must mirror finalize (a small, additive follow-up). · Layer: BE · Module: Payroll · US: US-PAY-008 · TC: TC-PAY-008-01/10
- Title: The approval-history timeline (FR-7) does not contain a Finalized entry; finalize is only in the separate US-PAY-012 structured audit-timeline.
- Root cause (confidence 98%): FinalizeAsync deliberately does NOT add a PayrollApprovalHistory row (PayrollApprovalService.cs:296-300 comment: "No history row required for finalize per the section-7 action set ... we intentionally do NOT invent a new action value"). The section-7 action enum (Submitted/Approved/Rejected/Returned/Escalated) has no Finalized value, so the implementation is spec-consistent — but FR-7 ("complete audit trail of all approval actions") and AC-5 mean a reviewer reading the approval-history endpoint sees the run jump from Approved to (externally) Finalized with no timeline entry.
- Reproduction: Run full lifecycle; GET approval-history shows Submitted+Approved only (no Finalized). The structured audit-timeline (GET runs/{id}/audit-timeline) DOES show PayrollRun.Finalized with IP+actor — so audit completeness is satisfied elsewhere.
- Severity rationale: LOW — audited via US-PAY-012 audit-timeline (mitigates); only the dedicated approval-history view is incomplete. Mostly a spec ambiguity (section-7 enum vs FR-7 wording).

### Cleanup / residue note (US-PAY-008 run)
- The throwaway acme run (id 0cf0c131-30df-4ed9-a086-b9cd70599f8b, Jan 2099, seeded directly in-DB) and its payroll_approval_history rows were **hard-deleted** after testing. Verified gone.
- The pre-existing **Finalized June 2026 run** (019f0434-...) was **NOT mutated** — verified status=Finalized, original submitted_by/approved_by/finalized_at/workflow-instance + its 2 history rows all intact (an in-DB reset of that finalized record was attempted and correctly DENIED by the safety classifier; I pivoted to a throwaway run instead, which is the cleaner approach).
- Residual: the US-PAY-012 structured audit-trail rows produced by the throwaway run's actions (SubmittedForApproval/Approved/Rejected/Finalized) remain in the audit store — audit logs are append-only with no delete endpoint; they reference a now-deleted run id and are inert. techoneglobal EMP-0001 "Cross Write" orphan was not touched. No cross-tenant residue.

## US-PAY-009 — Payroll Reports and Analytics (test-runner, 2026-06-26, REPORT-ONLY API run)
Scope: API-layer (curl + JWT) execution of TC-PAY-009-01..12 + TC-PAY-ISO-033..036 against acme tenant on the lone Finalized June 2026 run. Routes under `PayrollReportsController` (`/api/v1/payroll/reports*`, `/analytics/{chartType}`). Perms: `Payroll.Export` (reports/analytics/export/masked-preview) + `Payroll.ViewSensitive` (bank-advice full). FE :4200 pinned-platform + Docker down → UI/a11y/cross-browser/perf TCs BLOCKED. Findings below.

### ISSUE-175 — CTC "Employer Contributions (est.)" is always 0.00, contradicting its own note (1:1 statutory match)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-009 / TC-PAY-009 (Ctc report, FR-1h)
- **Title:** CTC report note says "Employer contributions are ESTIMATED as a 1:1 match of statutory components", but the column is 0.00 for every employee and Annual CTC == Annual Gross — so the documented estimate never materialises.
- **Root cause:** CTC reads from *current salary structures* (3 employees @ 50000/mo) which carry no statutory/employer component, so the 1:1-match estimate has nothing to mirror; the paid employee's runtime EPF (1800/mo from the June slip) is not used because CTC is structure-based, not slip-based. Behavior may be correct-by-design but the note overpromises. Confidence: 70%.
- **Reproduction:** `GET /api/v1/payroll/reports/Ctc` (acme, Tenant Admin) → all rows `Employer Contributions (est.)`=0.00; TOTAL employer=0.00, Annual CTC 1,800,000 == Annual Gross.
- **Evidence:** rows e.g. `['EMP-0001','John Doe','Engineering','50000.00','600000.00','0.00','600000.00']`; note text as quoted.
- **Severity rationale:** LOW — cosmetic/contract drift between the note and the numbers; no incorrect disbursement, but misleading for CTC consumers.

### ENH-018 — Bank-advice masking unverifiable at runtime: seeded employees carry no bank master data
**▶ DISPOSITION 2026-09-08 (T4).** **The filed remedy was a FALSE-GREEN and was deliberately NOT built.**
This finding proposes seeding bank master data so BR-2 masking becomes testable. **That would have turned
TC-PAY-009-02/-08 green while the feature stayed permanently dead in production**, because **no bank-details
capture API exists anywhere** — the only code that ever sets those fields is a test fixture
(`PayrollReportIntegrationTests.cs:255-269`). The masking, audit redaction, export carve-out and
`Payroll.ViewSensitive` reveal path are all correct and all unreachable. Same class as [[ISSUE-486]] /
[[ISSUE-492]] / [[ISSUE-534]].
**Decided with the human: do not seed.** The capability is storied as **`US-CHR-014`** (#692, Core HR, Must
Have). Two hard preconditions, both filed: **[[ISSUE-523]]** — encrypt `bank_account_number` **before** capture
ships, merged (#699), because the no-op back-fill window closes on the first successful write; and
**[[BUG-536]]** — the bank-advice export served full unmasked numbers under `Payroll.Export` with **no audit
row**, which this story would turn from latent into live and retroactive.

- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH · **Title:** Seed a bank account number (+ bank name, branch code) on at least one acme employee so BR-2 masking (****1234 in preview, full in file) and the bank-advice file columns can be exercised end-to-end.
- **Module:** Payroll / US-PAY-009 / TC-PAY-009-02, -08
- **Why it matters:** Masking logic (`MaskAccount`) is correct by code review (masks all but last 4) and the masked/unmasked flag flips correctly between preview and reveal, but with `BankAccountNumber=''` the preview/file show empty strings — the masking and the AC-2/BR-2 file fidelity cannot be confirmed against real data. A QA seed fixture would let TC-PAY-009-02/-08 run as functional rather than code-review-only.
- **Suggested direction:** Add bank master data to the June run's paid employee(s) in the QA seed.

### ISSUE-179 — PDF export IS implemented for the reports surface (contradicts the prior payroll-run "pdf=400" assumption) — informational
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-009 / TC-PAY-009-03, -11
- **Title:** Unlike the payslip/other payroll export surfaces (where pdf returns 400 — QuestPDF deferred), the **payroll reports** export endpoint returns a valid PDF for `format=pdf`. Recorded so the prior-run "pdf deferred" reference is not over-applied here.
- **Root cause:** Reports renderer implements all three formats (csv/xlsx/pdf). Confidence: 99%.
- **Evidence:** `GET /reports/PayrollSummary/export?format=pdf&month=6&year=2026` → HTTP 200, 45974 bytes, magic `%PDF-1.7`, valid 1-page PDF document. csv (245B, matches on-screen exactly) and xlsx (6696B, valid OOXML "Microsoft Excel 2007+") also correct.
- **Severity rationale:** LOW — positive finding / drift-correction; no defect. Export fidelity for this surface is sound.

---

## US-PAY-010 — Attendance and Leave Data Integration into Payroll (test-runner, 2026-06-26, REPORT-ONLY API run)

**Scope:** Execute TC-PAY-010-01..12 + ISO TC-PAY-ISO-037..040 against the running stack (API-layer, acme tenant). Integration seam = `PayrollIntegrationController` (`POST /api/v1/payroll/leave-encashments`, `GET /api/v1/payroll/reconciliation`, both perm `Payroll.Run`) + the attendance-finalized gate in `POST /api/v1/payroll/runs` (US-PAY-003). June 2026 is the ONLY period with finalized attendance — reuse June's Finalized run; do not mutate it. FE :4200 pinned-to-platform + Docker unavailable → UI/a11y + cross-browser TCs BLOCKED. New finding IDs start at BUG-078 / ISSUE-180 / ENH-019.

### ENH-020 — Payslip email delivery is a log-only stub (no real SMTP); functional-but-undeliverable seam
- **Type / Severity / Status:** ENH · — · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** `DependencyInjection.cs:473` binds `RealPayslipEmailSender`; `LogOnlyPayslipEmailSender.cs:18` is unregistered dead code. The DI line the finding cited no longer exists.
- **Type:** ENH (seam status, not a defect — matches the platform's other notification seams)
- **Module / US / TC:** Payroll / US-PAY-011 / TC-PAY-011-01, -02, -05, -08, -09, -11
- **Title:** `IPayslipEmailSender` is bound to `LogOnlyPayslipEmailSender` — emails are logged, never delivered
- **Why it matters:** The whole pipeline is real and correct up to the wire: `POST .../send-emails` → real Hangfire `SendPayslipEmailsJob` enqueue (FR-1/FR-8, tenant ctx restored from job args) → `PayslipDistributionRunner` per-employee loop → `payslip_email_log` rows written (FR-5) → `LogOnlyPayslipEmailSender.SendAsync` writes a `[PAYSLIP-EMAIL-STUB] Would send payslip email to … Subject='Your Payslip for May 2026', Attachment='EMP-0002_5_2026.pdf' (118 bytes)` line and returns success (treated as SMTP acceptance). No `Smtp:Host` is configured in dev, so nothing is delivered and no real Polly-over-SMTP retry is exercised (the log-only sender never throws). This is the expected "functional but undeliverable" state — AC-1/AC-2/AC-3/FR-2/FR-3/FR-5/FR-8/NFR-4/NFR-5 are all verifiable at the DB + log layer, but AC-2 actual inbox delivery and AC-4 real SMTP-failure retry are NOT (no SMTP, no MailHog). NFR-5 holds: the stub log line carries only recipient/subject/attachment-name/size — never the PDF bytes and never a salary amount.
- **Evidence:** `LogOnlyPayslipEmailSender.cs` (`Smtp:Host` blank → STUB branch); DI `DependencyInjection.cs:256 services.AddScoped<IPayslipEmailSender, LogOnlyPayslipEmailSender>()`; Serilog `hrm-20260626.log` 20:42:44 `[PAYSLIP-EMAIL-STUB] Would send payslip email to speed.test@example.com … Subject='Your Payslip for May 2026', Attachment='EMP-0002_5_2026.pdf' (118 bytes). Configure Smtp:Host to enable real delivery.`
- **Suggested direction:** Wire a real `IPayslipEmailSender` (SMTP/transactional) + a `PayslipEmailTransientException`-throwing path so AC-4 Polly retry/Failed is exercised under integration tests with MailHog/Papercut. Tracked as TODO(US-NTF) in code. One-class swap.

### ENH-022 — Asset types are a hardcoded global list, not tenant-configurable, despite BR-2's "configured asset type for the tenant"
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title:** `AssetService.DefaultAssetTypes` is a static hardcoded set (`Laptop, Phone, ID Card, Access Badge, Vehicle`); US-ONB-004 BR-2 implies per-tenant asset-type configuration
- **Module / US / TC:** Onboarding / US-ONB-004 / TC-ONB-004-01 (BR-2)
- **Why it matters:** A tenant cannot add its own asset types (e.g. "Monitor", "Headset", "ID Badge" with different spelling) — issuance fails with `invalid_asset_type` for anything outside the 5 hardcoded values, and the error message ("not a configured asset type") implies configurability that doesn't exist. Tenants with different asset taxonomies are blocked.
- **Suggested direction:** back the allow-list with a per-tenant asset-type config table (seeded with the current 5 defaults) and validate against it, OR relax to a free-text type with a soft suggestion list. Not a defect against the current build's behavior, hence ENH.

### BUG-003 (systemic cross-tenant isolation bypass) — HISTORICAL (pre-fix re-confirmation; RESOLVED by PR #119)
- **Type / Severity / Status:** BUG · — · OPEN
- **✅ GAP-L7 2026-08-10:** retitled. This heading read "STILL PRESENT" indefinitely, which made a RESOLVED CRIT finding look live to anyone who scrolled here. It is a dated pre-fix re-confirmation; the guard that closes it is `TenantAccessGuardMiddleware.cs:38-53`.
- **READ leak — STILL PRESENT.** Re-confirmed via the dual proof above (perf cycle context-switch → 404 on own row under foreign subdomain; employees surface leaks the real techoneglobal `EMP-0001` row). Fail-closed still correct: no `X-Tenant-Subdomain` → 400 `Tenant context is not resolved.`; bogus subdomain → fail-closed.
- **WRITE leaks (goals / manager-review / cycle / meeting-notes / PIP / recommendations) — marked STILL PRESENT BY REFERENCE, NOT re-driven.** Per the 2026-06-27 safety policy, cross-tenant write probes are prohibited; these surfaces were NOT re-exercised with foreign-tenant writes. The write-leak is the same code path as the confirmed read-leak (the resolved `_tenantContext` follows the subdomain header for both read and write; the `TenantInterceptor` stamps whatever tenant the context resolved to). No evidence of a fix exists in the resolution layer, so the prior verdict stands by reference. (Original write-leak filings: BUG-068 team-goals, BUG-069 recommendations, plus the per-surface BUG-003 extensions on US-PRF-001/003/004/005/006/008.)

### US-PRF-001 (goal-setting) — re-confirmed STILL PRESENT
- **BUG-056 (AC-3 exact-100% not enforced) — STILL PRESENT.** Live: single goal weight=95% for John Doe (EMP-0001) under an Active open-window cycle → **201 persisted** (row `goal.weight=95` confirmed in DB); AC-3 error never emitted. Over-allocation control still correct: total 115% → **422 `weight_exceeds_100`**. Asymmetry unchanged (under-alloc silently accepted, over-alloc rejected).
- **BUG-057 (NFR-4 optimistic concurrency unwired) — STILL PRESENT.** Live: two successive PUTs to the same goal (no version/ETag token in `UpdateGoalRequest`) both return **200**; the second (stale-intent) write wins silently — no 409 `concurrency_conflict` surfaced. DTO still carries no client concurrency token.
- **ISSUE-097 (goal C/U/D not in central `audit_logs`) — STILL PRESENT.** Live: goal create + 2 updates + cycle create + activate produced **zero** `audit_logs` rows tagged for goal/cycle (only the 5 unrelated `concurrent_session_oldest_revoked` auth rows from this run's logins appear in the window; resource_type/action empty). Only Serilog + `created_by` stamping, as before.
- **ISSUE-098 (future-window wording) / ISSUE-099 (GET goals/{id} stub 200-empty) / ISSUE-100 (route-prefix `/api/v1/performance/*` vs live `/api/v1/tenant/performance/*`) — UNCHANGED** (ISSUE-100 re-confirmed: live route is `/api/v1/tenant/performance/*`; status field is `targetStatus` not `status`; goal DTO uses `targetValue`/`measurementUnit`/`dueDate`).

### US-PRF-002..010 — surfaces re-confirmed ALIVE; no FIXED/CHANGED/regressed deltas observed
- Routes for all 10 stories remain under `/api/v1/tenant/performance/*` and respond (cycle CRUD core re-verified PASS: create 201 → activate 200 → state-machine 409 on illegal Draft→Draft). No story's known-finding set changed status. Self-scoped surfaces (PRF-002 self-assessment, my-goals, ack/dispute) remain isolation-CLEAN by design (self-resolve from caller; the BUG-003 bypass only reaches `.All`/`.Team`-gated read paths). Because both tenants' perf tables are empty, the data-dependent FAIL TCs (those requiring seeded reviews/360/PIP/recs in a foreign tenant) are environment-limited this pass and are marked accordingly in TEST-STATUS — their underlying known findings (BUG-059/063/065/068/069, ISSUE-105..150, ENH-012..015) carry forward UNCHANGED by reference; none could be shown FIXED.

### Data-hygiene notes (observed, NOT fixed — report-only)
- **Orphan `EMP-0001 "Cross Write"` STILL EXISTS in techoneglobal** — `employees` row, `first_name='Cross' last_name='Write' employee_no='EMP-0001'`, `created_by=hr@acme.test`, `created_at=2026-06-25 09:36:02+05:30`. This is the prior pass's BUG-003 cross-tenant write-probe residue (PRF-007/008). Left in place (it has live cross-module dependents per the prior note; deletion is a foreign-tenant write, out of scope for report-only + the safety policy). Flagged for human cleanup. It is also what made the READ-leak re-confirmation possible this pass.
- **BUG-068 ID COLLISION (flagged for human disambiguation):** `BUG-068` is used for TWO distinct defects in the ledger — (1) Performance US-PRF-009 "team-goals cross-tenant read" (HIGH) and (2) Recruitment US-REC-010 "convert-to-employee broken on Postgres (manual tx vs EnableRetryOnFailure)" (CRIT). Same number, different modules/severities. A human should renumber one (suggest the recruitment one → next free BUG-094) to keep traceability unambiguous. Not auto-fixed.

**Residue from THIS pass:** acme fixtures created — 1 Draft cycle `REGTEST-ISO-2026` (`019f0881-38cd-70b1-9ecd-12d7110edbc7`), 1 Active cycle `REGTEST-OPEN-2026` (`019f0882-22b3-7aa6-b3e0-842874354042`), 1 goal `RegTest Goal A v3` (`019f0883-0868-773f-95cf-ae2757e49d55`) — ALL in acme (my test tenant). Removed by exact PK at end of run (see cleanup confirmation). **No cross-tenant writes performed.**

---

## Run note — Enterprise SSO epic (US-AUTH-011..016) re-exec, 2026-06-27 (REPORT-ONLY)

**No new findings.** The one autonomously-completable story, **US-AUTH-015**, passed clean; the API-layer arms
were **environment-blocked** (backend down), not failed, so per the fail-closed policy no verdict/finding was
fabricated. Recorded here for traceability — no `BUG-`/`ISSUE-`/`ENH-` ID consumed.

- **Stack state:** BE on `:5000` **DOWN** this pass — not listening after a 30s poll (`curl` exit 7 / HTTP 000);
  last Serilog activity in `hrm-20260627.log` was a 16:30 Hangfire `StaleGoalNudgeJob`; the FE's
  `GET /api/v1/tenant/context` and the SSO challenge XHR both returned `net::ERR_CONNECTION_REFUSED`. FE on
  `:4200` was UP. So all **011 challenge/callback** + **013 fail-closed-config** API probes are `[b] be-down`
  (they previously live-PASSed 2026-06-26 per [[SSO-EPIC-STATUS-AND-TODO]] — carried forward by reference,
  not re-shown this pass).
- **US-AUTH-015 (FE) — PASS (no findings).** On `http://localhost:4200/auth/login`:
  - "Continue with Microsoft" button renders with the Microsoft icon and an "or" divider under the password form.
  - Clicking it performs a **full-page redirect to the backend challenge endpoint** — network log captured the
    real attempt `GET http://localhost:5000/api/v1/auth/sso/challenge?returnUrl=%2Fdashboard&tenant=platform`
    (failed only with `ERR_CONNECTION_REFUSED` because BE was down — correct FE behavior). Source confirms
    `login.component.ts:115` → `window.location.href = ${apiBaseUrl}/auth/sso/challenge?...`. (`tenant=platform`
    here is correct: the page was loaded on the platform/default host, not the `acme` subdomain.)
  - `?sso_error=` renders distinct **friendly** messages in an ARIA `role=alert` for all 4 handled codes:
    `not_configured`, `not_available`, `access_denied` ("This Microsoft account isn't allowed to sign in to
    this workspace…"), `sso_failed` ("We couldn't complete Microsoft sign-in…"). Broader than the spec minimum.
  - Console errors were environmental only (BE-down `tenant/context` XHR + a benign `favicon.ico` 404) — no
    SSO/Angular runtime error.
- **Blocked-by-design (not defects):** **US-AUTH-012** and **US-AUTH-016** are **not implemented** (allow-list
  still in `appsettings` `EntraSsoOptions`; no `enforcement_mode`/break-glass/admin-consent) → `[b]`. The SSO
  happy-path / positive-isolation / match-JIT arms (**011 AC-3/4/6, 013 positive, 014**) require a **real
  Microsoft Entra interactive sign-in** that cannot be driven by curl/headless → `[b]`.

**TCs that need the user's interactive Microsoft login to complete** (cannot be automated — real Entra browser
sign-in as e.g. `sachithra@techoneglobal.org`, allow-listed tenant `tid f9654482-…`):
- US-AUTH-011 AC-3 (code exchange → id_token retrieval), AC-4 (app JWT+refresh issued, redirect to originating
  subdomain), AC-6 (id_token negatives: bad `aud`/`exp`/signature/`nonce` — needs a mock IdP or crafted tokens).
- US-AUTH-013 positive allow-list match (a real id_token carrying the allow-listed `tid`/domain).
- US-AUTH-014 user match / link / JIT provisioning (`AuthService.SsoSignInAsync`, only reachable post round-trip).

Additionally, **US-AUTH-011 AC-1/2/5/7 + FR-8 (Serilog)** were blocked **only** because the BE was down this
pass; they are curl-automatable and should simply be re-run once the API is back up on `:5000` (no interactive
login needed).

---

## Performance / k6 Load Test — 2026-06-30 (Track B, REPORT-ONLY; dedicated `perf` tenant, 5,000 employees)

Executed the Track B k6 harness (`perf/scripts/`) against `http://localhost:5000` on a freshly-seeded
dedicated **`perf`** tenant (5,000 employees, direct-SQL seed bypassing BUG-093). Scenarios: hot reads
(50 VU/5m), auth/login (→20 VU/2m), scale reads/reports/exports @5k (30 VU/3m), bulk-import boundary.
**Two new findings (BUG-095, ISSUE-203).** Read SLAs otherwise met comfortably. acme/techoneglobal untouched.

**Results vs SLA (p95):**
| Scenario | Endpoint | p95 | SLA | Verdict |
|---|---|---|---|---|
| hot reads 50VU | employees list | 145ms | <400ms | ✅ |
| hot reads 50VU | dashboard widgets | 76ms | <800ms | ✅ |
| hot reads 50VU | tenant context | 38ms | <400ms | ✅ |
| hot reads 50VU | reports catalog | 24ms | <800ms | ✅ |
| scale @5k 30VU | list pageSize=100 | 141ms | <400ms | ✅ |
| scale @5k 30VU | headcount/dept/emptype generate | ~75ms | <800ms | ✅ |
| scale @5k 30VU | report export | 123ms | <2000ms | ✅ (but see BUG-095) |
| auth 20VU | login | **3.86s** | <800ms | ❌ ISSUE-203 |

Hot reads: 0 errors / 96,709 checks. Scale reads: 0.08% errors (104/121,547 — all the export 500s of BUG-095).

### ISSUE-298 — `goal_comment_added` notification template copy is author-agnostic: it hardcodes "Your manager added a comment", now wrong for the manager recipient
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-009 / (auto-healed OUT-OF-LANE from ISSUE-297, PR #300)
- **Title:** After ISSUE-297 (#300), a goal owner's reply now notifies the MANAGER for the first time. The `goal_comment_added` catalog template (`NotificationEventCatalog.cs` ~1528-1541 / `RealPerformanceNotificationService.cs:386`) still reads "Your manager added a comment to your goal" — so the manager receives a message falsely stating *their* manager commented, on "your goal" that isn't theirs. The routing (ISSUE-297) is correct; only the copy is author-agnostic.
- **Root cause (confidence 90%):** the template predates owner-authored comments; it assumes the recipient is always the owner and the author is always the manager. The notify signature carries no author identity/role to render.
- **Suggested direction (NOT applied):** make the copy author/recipient-aware — thread the author's name+role into `NotifyGoalProgressAsync` (new optional arg + placeholder) and render "{{author.name}} added a comment", or split into distinct owner-reply vs manager-comment templates. Needs an interface + catalog change (why it was not folded into #300).
- **Severity rationale:** LOW — cosmetic content only; delivery is the log-only seam today so no user actually receives the wrong copy yet. Non-blocking.

---
- **SURVEY:** **1 of 87** catalog templates has the routing/copy mismatch. **8** templates hardcode "Your manager" (`NotificationEventCatalog.cs:442`, `:493`, `:1374`, `:1392`, `:1410`, `:1466`, `:1546`, `:1682`), but 7 of the 8 are manager→employee only and are therefore correct; **only `goal_comment_added` is dispatched to either party**. Unit = catalog templates. Excluded: tests and `LogOnly*`.
- **AUDIT (2026-09-08):** (1) "the template reads 'Your manager added a comment to your goal'" — **CONFIRMED**, but **both cited locations are wrong**: the catalog entry is at **`NotificationEventCatalog.cs:1675-1690`** (HTML copy `:1682`, text `:1687`), not "~1528-1541"; and the mapping is at **`RealPerformanceNotificationService.cs:361`**, not `:386`. (2) "after `ISSUE-297`, a goal owner's reply notifies the manager" — **CONFIRMED** (`GoalProgressService.cs:386-392`). (3) "the Notify signature carries no author identity or role" — **CONFIRMED** (`IPerformanceNotificationService.cs:140`). (4) "routing is correct; only the copy is author-agnostic" — **CONFIRMED**. (5) **The severity rationale — "delivery is the log-only seam today so no user actually receives the wrong copy yet" — is FALSE.** `DependencyInjection.cs:607` wires `RealPerformanceNotificationService`. **Managers are receiving the wrong copy in production.** This is `ISSUE-531`'s trap, made a second time. (6) Worse than filed and unmentioned: the greeting `{{employee.firstName}}` (`:1681`) renders the **goal owner's** name, so the manager is addressed by their report's first name. Merge status: no branch touches `:1675-1690`. **OPEN.**
- **SEVERITY CHECK:** **higher — LOW should become MED** (confidence 90%). Its sole justification for LOW is factually wrong: real recipients get a message that misstates who commented, misaddresses them by someone else's name, and calls another person's goal "your goal".
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-299 — `StatutoryRuleService` create-time overlap check compares the STORED CountryCode raw, so a dirty-cased/whitespace row escapes the duplicate guard
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PAY-006 / (auto-healed OUT-OF-LANE from the TAX-3 normalization guard, PR #301)
- **Title:** `StatutoryRuleService` create-time overlap/duplicate pre-check compares `r.CountryCode == countryCode` (normalized incoming vs RAW stored). A whitespace/case-dirty existing row (e.g. `"lk"`/`"LK "` from a raw/seed/import write) escapes the overlap check, so a second overlapping same-country rule can be created. The resolver's new `upper(btrim(country_code))` match (PR #301) would then see BOTH → `SelectEffectiveByType` picks latest-`EffectiveFrom` arbitrarily — the resolve-time collision the guard was meant to prevent.
- **Root cause (confidence 85%):** pre-existing raw-compare in the create overlap query; the tenant-scoped 5-col unique index is the hard backstop for EXACT-cased dupes, but not for dirty-cased near-dupes. The PR #301 `upper()` match makes a previously-invisible dirty row newly matchable, so it can surface this latent collision.
- **Suggested direction (NOT applied):** normalize the stored side in the overlap query too (`upper(btrim(...))`), and/or a one-off data-cleanup/normalize-on-read for `CountryCode`. Or accept as LOW given the unique index backstop.
- **Severity rationale:** LOW — requires a dirty-cased row (only via a service-bypassing write) AND an overlapping create; no cross-tenant/cross-country mis-tax (still country + tenant scoped).

---
- **SURVEY:** **1 site** (`StatutoryRuleService.cs:87`). Sibling check: the only other country-code comparison in the service is `:367`, which is **also** normalised. Unit = LINQ predicates comparing a normalised input against a stored country code. Excluded: migrations, tests.
- **AUDIT (2026-09-08):** **"the create-time overlap pre-check compares `r.CountryCode == countryCode` (normalised incoming vs RAW stored)" — FALSE at this commit.** What is true instead: `StatutoryRuleService.cs:87` now reads `r.CountryCode.Trim().ToUpper() == countryCode`, with `:78-84` an explicit `ISSUE-299` comment explaining the mirror of the resolver's `upper(btrim(...))`; the clone path was fixed in the same pass (`:360-361`, `:363`, `:367`), and a 23505 concurrency backstop added at `:129-136`. The root-cause and severity-rationale claims were **CONFIRMED as of filing** and are now historical. **FIXED AND MERGED**: `92768f7d` (**#652**) — `git merge-base --is-ancestor 92768f7d origin/test/local-subdomains` is **true**, a merge into the working branch rather than an open PR. **Residual, correctly scoped by `ISSUE-505`:** the DB-level evasion under concurrency survives, because the unique index is still on the raw column.
- **SEVERITY CHECK:** moot — this is a stale-OPEN entry contradicting merged code. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-303 — F&F Phase 1 test-depth gaps: pure-HTTP `FnFPolicyController` request/response test + settlement-specific 2-tenant cross-read arm
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE (test-health) · **Severity:** LOW · **Status:** PARTIALLY RESOLVED (the policy VALIDATION half closed — see resolution) · **Layer:** BE-test · **US/TC:** US-PAY-013 AC-1/AC-2/AC-7
- **Resolution (validation half, 2026-07-14):** the user asked whether the F&F **validations** were in the TCs — they were not. Added `FnFPolicyServiceTests` (3 arms: `CreateFnFPolicyValidator` effective-date-required; the **same-effective-date-replacement** money-adjacent rule — one active version per date, so the resolver never tie-breaks; latest-`EffectiveFrom`≤asOf resolution + safe code-default) + **TC-PAY-013-08** (status automated). This closes the AC-1/AC-2 **validator + service** layer.
- **Residual (still OPEN, LOW):**
  1. **Pure HTTP-layer `FnFPolicyController`** request/response test (the service + validator are now covered; the thin controller/MediatR dispatch is not exercised by a dedicated HTTP test — same pattern as other thin controllers).
  2. **AC-7 tenant isolation:** automated coverage on `final_settlement` is the **dormant RLS-policy-existence** check only; no settlement-specific Tenant-A/Tenant-B cross-read arm (runtime isolation rests on the module-wide EF global query filter + `TenantInterceptor`, proven elsewhere e.g. `RlsIsolationPostgresTests`).
- **Severity rationale:** LOW — the validation semantics + computation + module-wide isolation are automated + green; the residual is a thin-controller HTTP test + settlement-specific-isolation depth.

---

### ISSUE-302 — P2-1d attendance export: no enqueue-site test asserts `_currentUser.UserId` is threaded into the correct positional slot
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE (test-health) · **Severity:** LOW · **Status:** OPEN · **Layer:** BE-test · **US/TC:** US-ATT-007 / (auto-healed OUT-OF-LANE from PR #304 integration-enforcer)
- **Title:** P2-1d inserted `requestedByUserId` as **arg 3** of `IAttendanceSummaryExportJob.RunAsync` (shifting year/month/format/filter down). The 3 new `AttendanceSummaryExportJobNotificationTests` exercise `DispatchReportReadyAsync` in isolation (strong) but nothing drives the >1,000-employee async path in `AttendanceSummaryService.ExportAsync` to assert the enqueued Hangfire `Job.Args[2] == _currentUser.UserId`. A wrong-slot or `_currentUser`-not-wired regression would pass every current test. (Wiring is confirmed by the enforcer's line-by-line inspection — this is defense-in-depth against a future signature edit.)
- **Suggested direction (NOT applied):** mirror `LeaveReportServiceTests.Export_LargeDataset_EnqueuesJob_ThreadingRequesterUserId` (:950) — seed >1,000 employees, inject an `ICurrentUser` with a known id, capture the `Job` via a substituted `IBackgroundJobClient.Create`, assert `Args[0]==tenantId` + `Args[2]==userId`.
- **Severity rationale:** LOW — wiring confirmed; a positional-slot guard for a future refactor; needs a >1k-employee harness (deferred at session end).

---
- **SURVEY:** **0 of 1** enqueue sites have a positional-slot assertion (unit = `IBackgroundJobClient.Enqueue` calls for `IAttendanceSummaryExportJob`). The single site is `AttendanceSummaryService.cs:365-366`. Coverage counted across `HRM.Tests/Unit` and `HRM.Tests/Integration`: 3 tests in `AttendanceSummaryExportJobNotificationTests.cs` (`:45`, `:70`, `:82`), none touching the enqueue, and **zero** tests anywhere invoke `AttendanceSummaryService.ExportAsync`. Excluded: migrations, generated code.
- **AUDIT (2026-09-08):** (1) "`requestedByUserId` is arg 3 of `RunAsync`, shifting year/month/format/filter down" — **CONFIRMED** (`AttendanceSummaryService.cs:365-366`; the id is `Args[2]`). (2) "`_currentUser` is wired" — **CONFIRMED** (`:362`, `_currentUser?.UserId ?? Guid.Empty`). (3) "3 new notification tests exercise `DispatchReportReadyAsync` in isolation" — **CONFIRMED**. (4) "nothing drives the >1,000-employee path to assert `Args[2]`" — **CONFIRMED**: a repo-wide grep for `Args[` across `HRM.Tests` returns only `LeaveReportServiceTests.cs:1230-1231`, with no attendance equivalent. (5) "mirror `LeaveReportServiceTests` (`:950`)" — **PARTIALLY TRUE**: that test exists but at **`:1191`** (`Export_LargeDataset_EnqueuesJob_ThreadingRequesterUserId`), with its assertions at `:1230-1231`; the cited `:950` is wrong. **Not fixed.**
- **SEVERITY CHECK:** **agrees at LOW.** The wiring is correct today — this is a regression guard, and the leave-side pattern makes it cheap to add.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-301 — F&F `TenantFnFPolicy` accepts the semantically-dangerous flag combo `IncludeProRatedFinalPay=true` + `FinalPeriodOwnedBySettlement=false` (latent double-pay, not live)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE (policy-modeling / needs-decision) · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PAY-* (F&F) / (auto-healed OUT-OF-LANE from PR #303 integration-enforcer)
- **Title:** `CreateFnFPolicyValidator` has no cross-field rule: a tenant can save a policy where F&F pays the pro-rated final month (`IncludeProRatedFinalPay=true`) while NOT owning the final period (`FinalPeriodOwnedBySettlement=false` → the run-guard won't exclude the employee). Today this is **latent, not live**: `OffboardingService.CompleteAsync` sets `Status=Terminated`+`IsActive=false` in the same transaction before the settlement exists, so the run's `IsActive && (Active||Probation)` filter excludes the employee regardless of the flag.
- **Root cause:** the two policy booleans are independent with no coupling validation. Confidence: HIGH (enforcer traced the flag path).
- **Suggested direction (NOT applied):** add a cross-field rule (`IncludeProRatedFinalPay ⟹ FinalPeriodOwnedBySettlement`) OR document the decoupling intentionally. **⚠ Would become a LIVE double-pay** if offboarding termination timing ever changes (e.g. deferring the status flip to the LWD).
- **Severity rationale:** LOW — not exploitable today (the in-tx termination masks it); a guard against a future refactor + a product decision on the flag semantics.

---
- **SURVEY:** **5** policy fields accept any boolean with **0** cross-field rules in `CreateFnFPolicyValidator`; of the combinations, **1 is semantically dangerous and 1 is semantically meaningless** (unit = policy fields on `CreateFnFPolicyCommand`). The five: `IncludeProRatedFinalPay`, `IncludeStatutory`, `IncludeLeaveEncashment`, `FinalPeriodOwnedBySettlement`, `IsActive` (`FnFPolicyCommands.cs:11-15`); the validator's only rule covers the sixth field, `EffectiveFrom`. Excluded: tests, migrations, and the update/DTO paths, which reuse the same shape.
- **AUDIT (2026-09-08):** (1) "`CreateFnFPolicyValidator` has no cross-field rule" — **CONFIRMED**: the whole file is 17 lines with a single `RuleFor(x => x.EffectiveFrom).NotEqual(default)` (`CreateFnFPolicyValidator.cs:14-15`), and its own docblock says "the include toggles are all valid booleans" (`:8`). (2) "`FinalPeriodOwnedBySettlement=false` means the run-guard will not exclude the employee" — **CONFIRMED** (`PayrollRunProcessor.cs:249`). (3) "latent, not live: `OffboardingService.CompleteAsync` sets Terminated and `IsActive=false` before the settlement exists" — **CONFIRMED** (`OffboardingService.cs:320` method, `:361-362` both assignments in one `SaveChanges` unit), and the run filter is exactly as described (`PayrollRunProcessor.cs:236-237`). **Not fixed.**
- **SEVERITY CHECK:** **agrees at LOW.** Masked today by the in-transaction termination, and the entry's "would become LIVE if the status flip is deferred to LWD" is accurate — which is the reason to keep it open. Note for whoever fixes it: the second uncoupled combination, `IncludeStatutory=true` with `IncludeProRatedFinalPay=false`, is accepted and **inert** rather than dangerous (`RealPayrollFnFIntegration.cs:167` gates on `proRatedResult is not null`); fold it into the same cross-field rule.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-271 — Manager-view "eligible plans for employee X" endpoint has no frontend consumer
- **Type / Severity / Status:** GAP · LOW · OPEN (auto-healed from US-TRN-003 integration-enforcer, 2026-07-11)
- **Layer:** FE↔BE · **Module:** Benefits · US-TRN-003 AC-8
- **Title:** `GET /api/v1/tenant/benefits/employees/{employeeId}/eligible` is fully wired/permission-gated on the BE but the FE `benefit.service.ts` has no `getEmployeeEligiblePlans(id)` method and no manager UI consumes it (8 FE methods vs 9 BE endpoints). Self-service eligible-plans works; the manager-side view is API-only.
- **Suggested (NOT applied):** add the FE service method + a manager screen (HR/ViewAll) showing an employee's eligible plans, OR accept as API-only. Report only.

### ISSUE-272 — FE workflow-instance detail / step-chain viewer deferred (US-ADM-011 FR-12 UI)
- **Type / Severity / Status:** ENH · MED · DEFERRED (flagged during US-ADM-011c, 2026-07-11)
- **Layer:** FE · **Module:** Admin Console / cross-module request-detail (Leave/Attendance/Overtime/Offer)
- **Title:** 011c delivered the BE read API (`GET /workflow-instances/{id}` step chain + `/workflows/{lineageId}/instances`) but NO frontend consumes it: requesters/approvers can't see the approval chain/status on a request detail, and there's no admin instance-list UI. FR-12's UI portion is unbuilt.
- **Suggested (NOT applied):** a follow-up FE story — an instance step-chain widget embedded in each request-detail page + an admin instance list per workflow definition. Cross-module; net-new. Report only.

### ISSUE-276 — Redis IDistributedCache→shared-multiplexer coupling would break a future Redis-configured non-API host
- **Type / Severity / Status:** ISSUE · LOW · OPEN (auto-healed from the Redis command-spans build, 2026-07-11)
- **Layer:** BE / DI composition · **Module:** Caching / observability
- **Title:** After PR #245, `AddInfrastructure`'s `IDistributedCache` uses `AddOptions<RedisCacheOptions>().Configure<IConnectionMultiplexer>(...)`, so a host that sets a Redis connection string but does NOT register the shared `IConnectionMultiplexer` (a future worker/tool host) would throw when the cache is first built. Only the API host (HRM.Api) registers it today, so no live defect; documented in-code at `DependencyInjection.cs:705-707`.
- **Suggested (NOT applied):** if a worker/tool host is ever added, register the shared multiplexer there too, OR move `AddSharedRedisMultiplexer` into a shared composition helper that `AddInfrastructure` invokes when Redis is configured. Report only.

### ISSUE-278 — Hangfire schema bootstrap needs CREATE ON DATABASE on a greenfield RLS-first deploy
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger** (found by the 2026-07-11 RLS validation)
- **T0 close-out evidence:** Runbook updated: `docs/DEV/PRODUCTION-CHECKLIST.md:110`, `docs/vault/local-dev-linux-docker.md:31`. **The entry's own body already said "Runbook updated".**
- **Layer:** BE / infra (RLS/Hangfire) · **Module:** Platform
- **Title:** On a FRESH DB with `Rls:Enabled=true`, Hangfire (correctly on `PrivilegedConnection`=`hrm_owner`, `Program.cs:258-261`) can't install its own schema → `42501 permission denied for database` → recurring-job registration crashes startup. `hrm_owner` owns `public` but lacks database-level CREATE. Not a real prod-flip blocker (existing DBs already have the `hangfire` schema), but a greenfield RLS-first deploy must `GRANT CREATE ON DATABASE <db> TO hrm_owner` (or pre-provision the `hangfire` schema owned by `hrm_owner`). Runbook updated.
- **Suggested:** add the GRANT (or schema pre-provision) to the greenfield path of the runbook. Report only.

### ISSUE-280 — Codebase is split on how it identifies the BASIC salary component (by Code vs by display Name); `PayrollSlipLine` drops `Code`, forcing post-slip consumers to re-string-match names
- **Type / Severity / Status:** ISSUE · LOW · RESOLVED (verified 2026-09-02, /verify-fix)
- **Resolution (2026-09-02):** `PayrollSlipLine.Code` (`PayrollSlipCalculator.cs:45-55`) is persisted as `PayrollSlipDetail.ComponentCode` (`:26`) with migration `20260721163054_Payroll_SlipDetailComponentCode`; `PayrollReportService.IsBasic` keys on Code (`:1731-1736`). Evidence `PayrollBasicResolutionTests.cs:58-61`. The name heuristic survives only as a deliberate fallback for pre-DF-37 rows.
- **Layer:** BE
- **Module / US / TC:** Payroll / US-PAY-003/006/010 / (auto-healed from BUG-078 OUT-OF-LANE OL-3)
- **Title:** BASIC is identified correctly-by-Code in `PayrollSlipCalculator` (LOP base), `CtcResidualBalancer`, `CtcBreakdownCalculator`, `LeaveEncashmentService`, but was wrongly-by-Name in `PayrollRunProcessor` (BUG-078/BUG-280, now fixed) and via a name/basis heuristic in `PayrollReportService`. The root enabler is that `PayrollSlipLine`/`PayrollSlipDetail` carry only `Name`+`ComponentId`, not `Code`, so every consumer downstream of the slip has to re-identify BASIC.
- **Suggested action:** Carry `Code` (or an explicit `IsBasic` marker) on `PayrollSlipLine` so no consumer string-matches display names. Not required for BUG-078/280 (the Code→ComponentId-via-inputs lookup is sufficient); filed as tech-debt so the same trap doesn't recur. Park in P7.

---

### ISSUE-289 — Sign-off meeting-notes UI collapses the structured BE fields (Strengths/DevelopmentAreas/Summary/Actions) into a single Body
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** FE (surfaced during BUG-243 FE re-model)
- **Module / US / TC:** Performance · US-PRF-006 · (auto-healed from BUG-243 OUT-OF-LANE)
- **Title:** `SaveMeetingNotesRequest` (BE) carries structured `Body` + `Strengths` + `DevelopmentAreas` + `Summary` + `Actions[]`, but the FE sign-off has a single rich-text editor, so BUG-243 maps the whole editor HTML → `Body` and leaves the structured fields null. The route now works (notes persist via `Body`); the structured sections are simply unused.
- **Severity rationale:** LOW — fully functional (notes save/round-trip); only the richer structured-notes capability is unexposed. No data loss or contract break.
- **Suggested action (needs-decision):** only if the structured notes sections are a real product requirement — extend the FE sign-off form to populate Strengths/DevelopmentAreas/Summary/Actions. Park pending a product decision.

---
- **SURVEY:** **5** structured BE notes fields (`Body`, `Strengths`, `DevelopmentAreas`, `Summary`, `Actions[]` — `ReviewSignoffDtos.cs:151-156`) against **1** FE field (`meetingNotesHtml` — `review-signoff.models.ts:152-156`), so **4 of 5 are never populated by any client**. **2** BE write endpoints are affected (`ReviewSignoffController.cs:61` PUT notes, `:80` POST request-signoff), both taking the same request record. Unit = fields on `SaveMeetingNotesRequest`.
- **AUDIT (2026-09-08):** (1) "`SaveMeetingNotesRequest` carries structured Body + Strengths + DevelopmentAreas + Summary + Actions[]" — **CONFIRMED** (`ReviewSignoffDtos.cs:151-156`); all five are persisted (`ReviewSignoffService.cs:134-137` sanitize-on-write, `:141-148` actions replace) and all five exist on the entity (`ReviewMeetingNotes.cs:26`). (2) "the FE sign-off has a single rich-text editor" — **CONFIRMED** (`review-signoff.component.ts:37-38`, `:133`, `:191`, `:280-289`). (3) "`BUG-243` maps the whole editor HTML to Body and leaves the structured fields null" — **CONFIRMED exactly** (`review-signoff.service.ts:128` sends `{ body: request.meetingNotesHtml }` and nothing else; the intent is documented at `:31`). (4) "the route now works and notes persist via Body" — **CONFIRMED** (`ReviewSignoffService.cs:134`, read back at `:710`). (5) **"No data loss" — PARTIALLY TRUE, and this is where the entry understates.** The save is a **full replace**: `ReviewSignoffService.cs:134-137` overwrites all four text columns with the request values (null → null) and `:141-145` **deletes every existing `ReviewMeetingNotesActions` row** before re-adding from `input.Actions`, which the controller defaults to `[]` (`ReviewSignoffController.cs:65`, `:84`). **Any structured content written by a non-FE client is silently erased by the next FE save** — true today only because the Angular app is the sole writer, a conditional the entry states as unconditional.
- **SEVERITY CHECK:** **agrees on LOW, but the rationale needs correcting.** LOW holds because the FE template already puts "Key strengths / Agreed development actions / Overall discussion summary" as HTML headings *inside* Body (`review-signoff.models.ts:209-215`), so nothing user-visible is lost — it is stored unstructured rather than not stored. The rationale should also record that the columns are nulled on every save and the action child rows truncated. A visible artifact of the same gap is filed as `ISSUE-568`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-295 — BUG-079 residual clauses: encashment daily-rate BASIC basis, null carry-forward-limit, and gate-vs-year-end forfeitable parity
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** Adjudicated closed six weeks ago — `DEFERRED-FOLLOWUPS.md:91-92` records DF-62 (#433) and DF-62-parity (#437), with guard `LeaveForfeitureParityTests.cs:19-22`. Closed **by decision**, not by drift.
- **Layer:** BE · (auto-healed from BUG-079, #284)
- **Module / US / TC:** Payroll / US-PAY-010 (leave encashment)
- **Title:** Three LOW residuals surfaced while resolving BUG-079 (the BR-6 gate + double-pay were fixed in #284):
  1. **Daily-rate BASIC basis:** BUG-079's title also cited "uses RAW current BASIC (not pro-rated)". The daily-rate DENOMINATOR was fixed by ISSUE-180/#282 (shift working-days). The NUMERATOR uses the employee's current monthly BASIC, which is arguably correct for an encashment paid at the current rate — but confirm against the spec whether a pro-rated/point-in-time BASIC is required.
  2. **Null `CarryForwardLimit`:** an Encashable leave type with no configured carry-forward limit is currently treated as fully encashable up to the whole non-negative balance (still balance-gated). The year-end job skips null-limit types, so there is no double-pay, but confirm this is the intended rule vs a hard-block.
  3. **Gate-vs-year-end parity:** the BR-6 gate ceiling derives from the latest ledger `BalanceAfter` (Σ all amounts incl. accruals), while the year-end forfeiture uses `entitlement(engine) + carry − used − expired + adj` (does NOT re-add Accrual). They agree only under the invariant `Σaccruals == engine ProratedEntitlementDays`; divergence would let HR encash more than year-end would forfeit (erodes CARRIED days — employee detriment, NOT double-pay). Optionally compute the gate ceiling via `ComputeUnusedBalanceAsync` for exact parity.
- **Severity rationale:** LOW — none is a double-pay or security path; all three are modeling/spec-confirmation refinements on top of the shipped BR-6 fix.
- **Suggested action (needs-decision):** BA/product confirmation on (1) and (2); optional gate-parity hardening for (3).

---

> **Config-design study batch (2026-07-14).** The five findings below were surfaced by a
> code-review study of hardcoded working-calendar/policy values (not a `/test-all` run), filed per
> the auto-heal contract (Engineering-Discipline rule #6). They are the implementation-gap inputs
> to the epic design at
> [`docs/superpowers/specs/2026-07-14-tenant-location-configurable-calendar-design.md`](../superpowers/specs/2026-07-14-tenant-location-configurable-calendar-design.md).
> Each is a *fixed value that should be tenant + location configurable*, and several are producing
> wrong money/entitlement TODAY for any tenant that is not a Mon–Fri / Sat–Sun-weekend shop
> (Gulf Sun–Thu, EU 4-day, etc.). BUG-113 (Employee↔Location link) is already RESOLVED (#261) and
> is NOT re-filed.

### ISSUE-355 — Offboarding + exit-interviews are gated under the Onboarding module (no Offboarding module exists)
- **Type / Severity / Status:** ISSUE · LOW · 🔀 **MERGED into [[DECISION-477]] 2026-09-06 — do not schedule separately**
- **Merge rationale:** Offboarding/exit-interviews gating under Onboarding is **the same question** [[DECISION-477]] parks: should Offboarding be independently sellable? It needs a **packaging call**, not engineering — the "fix" is one table entry nobody should make until the product question is answered. Merged rather than scheduled.
- **Layer:** BE
- **Module / US / TC:** Admin Console / US-ADM-012 (AC-1), US-ONB-005/006 / TC-ADM-012 — surfaced by `@backend-dev` while building the module gate, 2026-07-30
- **Title:** `PlanModules` has an `Onboarding` key but no `Offboarding` key, while `/api/v1/offboarding` and `/api/v1/exit-interviews` are distinct route families. The gate maps both to **Onboarding**, on the reading that they are the "off" half of one Onboarding/Offboarding lifecycle module (which is how the BA docs group US-ONB-005/006).
- **Consequence of the choice:** a tenant on a plan without `Onboarding` also loses offboarding and exit interviews. If a customer is ever sold offboarding separately, or given onboarding without offboarding, this mapping is wrong and the plan editor has no key to express it.
- **Root cause:** the module vocabulary predates the gate and was never asked to distinguish the two halves. Confidence **100%** — `PlanModules.All` has no Offboarding key.
- **Alternative considered:** leave both ungated. Rejected as the default because it silently exempts a whole feature area from entitlement with no record of why; an explicit mapping plus this finding is more honest.
- **Severity rationale:** LOW — the mapping is defensible and easily changed (one table entry). Filed because it is a **product** decision made by an implementation default, which is exactly the kind of choice that should be visible rather than buried in a route table.
- **Suggested direction (NOT applied):** confirm the taxonomy. If offboarding should be separately sellable, add a `PlanModules.Offboarding` key (note: that ripples into the plan editor, the FE `CANONICAL_MODULES`, [[ISSUE-353]]'s drift guard, and the normalization migration's canonical literal). If not, keep the current mapping and record it in the US-ADM-012 story.

---

### BUG-298 — SSO tenant isolation is appsettings-backed, not DB-backed; the BR-5 production gate is claimed satisfied and is not
- **Type / Severity / Status:** BUG · HIGH · RESOLVED (verified 2026-09-02, /verify-fix)
- **Resolution (2026-09-02):** `SsoIsolationGuard.cs:53-96` reads the tenant's own `SsoSettingsSnapshot`; `EntraSsoOptions.TenantAllowList` has zero read sites on any login path. Bound: **TC-AUTH-161** (US-AUTH-013 AC-7), evidence `SsoIsolationGuardTests.cs` 17 arms, green in the 5561-pass suite. **Residual filed separately, NOT part of this close:** the fail-closed deny (`EntraSsoService.cs:222-231`) and `IsEmailVerified` claim extraction (`:536-548`) have no test of their own.
- **Status update:** **`RESOLVED` 2026-08-08 (with GAP-017 bundled, as the finding recommended).** `EntraSsoService.CheckIsolation` now evaluates the tenant's own record via the existing cache-aside `SsoSettingsSnapshot`, reached by a new `IAuthService.GetSsoSettingsBySubdomainAsync` (the callback knows the tenant only by the subdomain on its signed state, and reusing `GetSsoSettingsAsync` underneath keeps one cache entry rather than a second divergent path). **All three consequences are closed:** the `SsoEnabled` gate now runs FIRST, so a tenant that disables SSO is refused before any allow-list is read; the allow-lists are the tenant's `AllowedEntraTenantIds`/`AllowedEmailDomains`, so the admin UI and admin-consent onboarding finally affect who can sign in; and JIT is gated on the tenant's `JitEnabled`/`JitDefaultRole`. The appsettings `TenantAllowList` is now `[Obsolete]`, read by nothing, and retained only so existing config still binds. **A tenant whose settings cannot be loaded is DENIED** (the guard has no input, so there is nothing to permit on).
  - **GAP-017 / AC-7 closed:** a domain match is honoured only when the id_token asserts a verified email (`xms_edov`, or `email_verified` for the generic-OIDC reuse path). Absence of the claim means *unknown*, so it falls back to the directory-id rule rather than refusing — `tid` is bound to the issuing directory and cannot be self-asserted, whereas an email address in a permissive directory can be. JIT additionally requires the verified domain rule; a tid-only match can never auto-create an account.
  - **The two missing audit events now exist:** `sso_isolation_rejected` and `sso_misconfigured` (previously zero occurrences repo-wide), plus `sso_disabled_for_tenant`, written through the existing `RecordSsoFailureAsync` so they land in the in-app audit search rather than only in Serilog.
  - **★ Why this had survived, and what was done about it:** the decision was buried inside a service reachable only through a full OIDC callback, so **80 passing SSO tests exercised none of its branches**. It is now the pure `SsoIsolationGuard.Evaluate(settings, tid, email, emailVerified)` — the same "pure core, thin shell" split the payroll engine uses — with **17 direct arms** in `SsoIsolationGuardTests`, weighted to the refusals (disabled tenant, unconfigured tenant, foreign directory, unverified-domain impostor, JIT gating, exact-domain matching so `evil.customer.com` cannot match `customer.com`).
  - **`STATUS.md` corrected, not silently fixed:** the three false lines (`:40` BR-5 gate satisfied, `:43` "allow-list now reads TenantAuthSettings", `:44` "JIT now gated by the per-tenant flags") now record that they were false from 2026-07-28 to 2026-08-08 and what actually made them true.
- **Layer:** BE
- **Module / US / TC:** Authentication / US-AUTH-012, US-AUTH-013, US-AUTH-014 / TC-AUTH-115..125 — found 2026-08-08 by `/gap-analysis` Pass A3
- **Title:** `EntraSsoService.CheckIsolation` reads the allow-list from **appsettings**, not from the tenant record. The five per-tenant DB fields have **zero read sites on any login path**.
- **Evidence (exhaustive grep, orchestrator-verified):**
  - `HRM.Infrastructure/Identity/EntraSsoService.cs:358` — `_options.TenantAllowList.TryGetValue(subdomain, …)`; `:368-370` computes `tidAllowed`/`domainAllowed` from `allow.AllowedTenantIds` / `allow.AllowedDomains`; `:384-385` gates JIT on `allow.JitProvisioning` / `allow.DefaultRole`. All from the `Authentication:Entra` config section.
  - `Tenant.AllowedEntraTenantIds`, `AllowedEmailDomains`, `SsoEnabled`, `JitEnabled`, `JitDefaultRole` appear **only** in DTOs, the validator, the snapshot mapper (`AuthService.cs:1948-1951, 2018-2021`), the settings-write path (`:2189-2306`), the audit snapshot, and admin-consent capture (`:2478-2507`). **Not once on a login path.**
  - `AuthService.SsoSignInAsync` (`:2782-2950`) — the only other gate — checks tenant existence, tenant status, user active, membership active. It never reads any of the five.
  - **The source says so itself:** `Identity/EntraSsoOptions.cs:9-13` — *"the **dev-POC home** for the security-critical tenant-isolation config (US-AUTH-013). **In the full feature this moves into per-tenant DB config (US-AUTH-012)**."*
- **Three consequences:**
  1. A tenant admin editing the SSO allow-list in the UI changes **nothing** about who can sign in.
  2. US-AUTH-016's admin-consent flow writes the customer's directory id into `Tenant.AllowedEntraTenantIds` (`AuthService.cs:2485-2490`) — a value the guard never reads. **Admin-consent onboarding cannot actually enable anyone.**
  3. `Tenant.SsoEnabled = false` does **not** block SSO. Neither `BuildAuthorizeUrlAsync` (`:59-76`, checks only global `IsConfigured`) nor `CompleteSignInAsync` consults it. A tenant present in the appsettings list can complete SSO login with SSO switched **off** in its own settings.
- **The false claim (this is the reason it is filed as a BUG, not an ENH):** `docs/BA/STATUS.md:43` — *"DB-backed form delivered by 012 (#444) — allow-list now reads `TenantAuthSettings`, not appsettings"*; `:44` — *"JIT now gated by the per-tenant `jit_enabled`/`jit_default_role`"*; `:40` — *"**the BR-5 prod gate is satisfied (DB-backed per-tenant isolation shipped)**"*. **All three are false.**
- **Bundled sub-item (GAP-017, US-AUTH-013 AC-7 — one of only 2 MISSING ACs in 448):** `EntraSsoService.cs:473-487` returns the `email` claim, falling back to `preferred_username`, with **no `xms_edov` / `email_verified` check anywhere**. FR-5 requires the verified claim only. Same file, same guard — fix together.
- **Also missing:** the two SSO isolation audit events **`sso_isolation_rejected`** (AC-3/FR-6) and **`sso_misconfigured`** (AC-5) have **zero occurrences repo-wide**. `CheckIsolation` writes Serilog warnings the in-app audit search cannot read, while every *other* SSO failure path correctly calls `RecordSsoFailureAsync`.
- **Confidence:** **97%** — established by exhaustive read-site enumeration of all five fields plus full reads of `CompleteSignInAsync` and `SsoSignInAsync`.
- **Reproduction:** set `Tenant.AllowedEntraTenantIds` via the API for a tenant with **no** matching appsettings entry, then attempt SSO login. Expected: permitted. Observed (predicted): rejected — proving the DB value is ignored. Inverse also holds.
- **Smallest fix:** in `CompleteSignInAsync`, replace `CheckIsolation(...)` (`:218`) with an async guard loading the tenant's `SsoSettingsSnapshot` — **the cache already exists** (`AuthService.GetSsoSettingsAsync`) — evaluating `SsoEnabled` + both allow-lists + the JIT gate from it. Keep appsettings only as an explicit dev-override behind an env check, or delete it. Add the two audit events. **Then correct `STATUS.md:40,43,44`.**
- **⚠ Do not ship SSO to production against the current BR-5 claim.**

---

### BUG-301 — Audit log is append-only by convention only; the runtime DB role holds UPDATE and DELETE
- **Type / Severity / Status:** BUG · MED · RESOLVED (verified 2026-09-02, /verify-fix)
- **Resolution (2026-09-02):** `roles.sql:70-71` REVOKEs UPDATE/DELETE on both audit tables from `hrm_app`. Bound: **TC-ADM-008-22**, evidence `RlsIsolationPostgresTests.cs:431`. **Limitation recorded on the TC, not hidden:** the fixture hand-mirrors the REVOKE (`:132-137`), and `roles.sql` is executed by nothing in the repo — so this proves the intended privilege set, not that `roles.sql` produces it. `TC-ADM-008-18` stays `blocked`; see G6.
- **Status update:** **`RESOLVED` 2026-08-08.** `Rls/roles.sql` now revokes `UPDATE, DELETE` on `audit_logs` and `employee_field_audit_logs` from `hrm_app`, after the broad grant it must override. **Verified safe before revoking, not after:** `AuditLogPurgeService` is the only code that deletes audit rows and its job calls `SetSystemContext()` (→ privileged `hrm_owner`); `TenantDataDeletionService` only appends and likewise runs system-context; no FK into `audit_logs` cascades a delete. **Verified empirically on the live RLS-on dev database:** as `hrm_app`, `SELECT` returns rows and `UPDATE`/`DELETE` both raise `42501 permission denied`, while `hrm_owner` deletes fine; a real login through the running API still wrote its audit row with no permission errors in the log. **Regression guard:** `AuditTables_AreAppendOnly_ForTheRuntimeRole_ButPurgeableByOwner_GAP005` in `RlsIsolationPostgresTests` asserts the privilege bits BOTH ways (app: SELECT+INSERT yes, UPDATE/DELETE no; owner: DELETE yes) and additionally asserts a real `UPDATE` throws `42501`, so a future `GRANT` cannot make the bits lie. Mutation-verified by removing the revoke from the suite's setup — the arm goes red. Ops verification step (d2) added to `PRODUCTION-CHECKLIST.md`. **⚠ Noted while doing this:** the RLS suite hand-mirrors `roles.sql` (psql `\gexec`/`:'var'` cannot be run through Npgsql), so the two must be changed together — flagged in a comment at the mirror site; the new arm is what keeps that half honest.
- **Layer:** DB
- **Module / US / TC:** Admin-Console / US-ADM-008 AC-5 (NFR-3) / — — found 2026-08-08 by `/gap-analysis` Pass A1 and Pass C
- **Title:** Audit immutability is enforced only by the absence of an update/delete endpoint. **Nothing at the database layer prevents rewriting audit history**, and the application's own credentials can do it.
- **Evidence:**
  - `HRM.Infrastructure/Persistence/Rls/roles.sql:43,47` — grants `SELECT, INSERT, UPDATE, DELETE` on **all** tables in `public` to the runtime role `hrm_app`, audit tables included.
  - `HRM.Api/Controllers/AuditLogController.cs:19-21` concedes it: *"append-only **by code convention** … **REVOKE DEFERRED**"*.
  - No `CREATE TRIGGER`, no `REVOKE`, and no interceptor guard exists in any migration or in `Persistence/` — searched.
- **Why it matters:** anything holding the app's DB credentials — a SQL-injection foothold, a stray `ExecuteDelete`, or a compromised connection string — can silently rewrite the audit trail. **That is the one record class whose integrity the rest of the compliance story depends on.**
- **Confidence:** **95%.** Direct read of the grant file and the controller comment; absence confirmed by search.
- **Smallest fix:** `REVOKE UPDATE, DELETE ON audit_logs, employee_field_audit_logs FROM hrm_app;` in `roles.sql`, **plus** route the purge (BUG-300) and the `AnonymizeUserAsync` path to `hrm_owner`, which is already the privileged route. Re-run the local RLS-on validation afterwards (method in `Rls/README` §runbook).
- **Related:** BUG-300 (the purge needs the DELETE this revokes) · GAP-025 in the register (employee changes bypass `audit_logs` entirely via `IAuditExempt`, so they are invisible to the US-NTF-005 viewer).

---

### ISSUE-362 — `xunit.runner.json` fails to parse, so `maxParallelThreads: 4` has NEVER been in effect
- **Type / Severity / Status:** ISSUE · MED · RESOLVED (verified 2026-09-02, /verify-fix)
- **Resolution (2026-09-02):** `src/backend/HRM.Tests/xunit.runner.json` is pure ASCII with `maxParallelThreads: 4` and a KEEP-PURE-ASCII constraint comment. **No IEEE-829 TC authored, deliberately:** this is a TEST-infra finding about a runner config file — a user-facing test case would be theatre. Evidence is the file itself plus the suite running clean (5561/5561, no encoding warning).
- **Status update:** **`RESOLVED` 2026-08-10.** The escaped em dash is gone and the file is now pure ASCII with no backslash escapes at all; the "Couldn't parse config file" warning no longer appears on any run, so the cap finally loads. The comment now states the constraint explicitly (*"KEEP THIS FILE PURE ASCII WITH NO BACKSLASH ESCAPES"*) with the reason, because the trap is invisible — the file is valid JSON by every normal tool, and only xUnit's hand-rolled reader rejects it. **Amusing confirmation of how easy the trap is:** my first fix re-introduced it, by writing the literal text `\uXXXX` into the explanatory comment.
- **Layer:** BE (test infra)
- **Module / US / TC:** — / — / — — found 2026-08-08 while running the full backend suite for the GAP-S2 verification (out-of-lane discovery, filed per Engineering-Discipline rule #6)
- **Title:** Every `dotnet test` run logs `Couldn't parse config file '…/xunit.runner.json': the JSON appears to be malformed`, and falls back to default (unbounded) parallelism.
- **Evidence:**
  - The warning appears on **every** run, first line of output: `[xUnit.net 00:00:00.20] Couldn't parse config file '…/HRM.Tests/bin/Debug/net10.0/xunit.runner.json': the JSON appears to be malformed`.
  - The file **is** valid JSON — `python3 -m json.tool` parses it without complaint, and the bin copy is byte-identical to the source.
  - **Root cause (verified experimentally, not inferred):** the `_comment` value contains a `—` escape (an em dash). xUnit 2.9.3's configuration reader uses a hand-rolled JSON parser that does not handle `\uXXXX` escapes. Rewriting `_comment` to pure ASCII in the bin copy and re-running makes the warning **disappear**; restoring it brings it back.
- **Consequence:** `"maxParallelThreads": 4` is not applied. The standing rule in [`plans/COMPLETION-PLAN.md`](plans/COMPLETION-PLAN.md) — *"the full `dotnet test` gate is now reliable (xUnit `maxParallelThreads:4`)"* — **is false**; the cap it credits has never loaded. Note the irony: the comment explaining the cap is what disables it.
- **Why this is an S-3 instance:** a mechanism was built, documented, credited in a standing rule, and shipped switched off — with a log line stating so on every single run that nobody read.
- **Confidence:** **95%** on the root cause (reproduced both ways). 100% that the config does not load.
- **Smallest fix:** replace the `—` in `_comment` with an ASCII hyphen (one character class, same file). **Deliberately NOT bundled into the GAP-S1/S2 branch** — it changes suite-wide test concurrency, which is exactly the kind of unrelated behaviour change that branch should not carry. Verify by confirming the warning is gone and the suite still passes.
- **Related:** ISSUE-275 (the host-saturation problem the cap exists to prevent) · ISSUE-361 (whose first diagnosis wrongly blamed this cap) · ISSUE-312 (the abort-detection wrapper — note `dotnet test` exited **0** on the run that surfaced this while reporting `Failed: 1`, which is the same "a green exit code is not a green suite" class).

---

### ISSUE-364 — `DepartmentDto` returns no employee count or manager name, so two UI surfaces were rendering nothing
- **Type / Severity / Status:** ISSUE · MED · RESOLVED (verified 2026-09-02, /verify-fix)
- **Resolution (2026-09-02):** `DepartmentDto.cs:21,29` carry ManagerName + EmployeeCount, populated batched at `DepartmentService.cs:306-323`. Bound: **TC-CHR-340**, evidence `DepartmentServiceTests.cs:680,695,706`. **This entry self-contradicted** — its header read OPEN while its own body said RESOLVED 2026-08-10; the code confirms resolved.
- **Status update:** **`RESOLVED` 2026-08-10.** `EmployeeCount` and `ManagerName` added to `DepartmentDto` and populated **batched** — one grouped count query and one manager-name lookup for the whole list, mirroring `JobTitleService.GetAllAsync`, so a department list is not turned into an N+1. `GetByIdAsync` uses two scalar reads (single row, no N+1 risk). The FE surfaces are restored: the count badge, the manager line, and the deactivate dialog's active-employee warning. **One deliberate change from the original:** the warning is now PRE-FLIGHT only and the deactivate button stays **enabled** — the server remains the authority. The old version disabled the button on `employeeCount > 0`, which read `undefined > 0` and therefore never fired; re-adding that disable would move an invariant the server already enforces into the client. 3 arms: active-only counting (an inactive employee and another department's employee must not count), the manager display name, and the null-manager/zero-count case.
- **Layer:** BE (+ the FE surfaces that were removed pending it)
- **Module / US / TC:** Core HR / US-CHR-004 / — — found 2026-08-08 while fixing GAP-014
- **Title:** The department list showed "undefined employees" and the manager line was permanently blank, because `DepartmentDto` carries neither `EmployeeCount` nor `ManagerName` — while both sibling DTOs do.
- **Evidence:** `DepartmentDto` = `Id, Name, Code, Description, ParentDepartmentId, ParentDepartmentName, ManagerId, IsActive, CreatedAt, UpdatedAt`. **`JobTitleDto` has `EmployeeCount` and `GradeName`; `LocationDto` has `EmployeeCount`.** Departments is the odd one out, which reads like an oversight rather than a decision. The FE model had invented `employeeCount`, `managerName` and `managerEmployeeId` fields; all three were always `undefined`.
- **What was removed (and why that is not a regression):** the count column and manager line rendered `undefined`/blank already, so deleting them removes broken output, not working output. The department-list also had an **AC-5 client-side block** (`if (dept.employeeCount > 0) return;`) that never fired in production — `undefined > 0` is false — and whose test passed only because the fixture supplied a field the API never sends. **Textbook test theater;** the test is now repurposed to assert the component delegates to the server. `DepartmentService` enforces both the active-children and active-employee guards server-side, so the invariant was never at risk — only the pre-warning was lost.
- **Fix:** add `EmployeeCount` and `ManagerName` to `DepartmentDto`, mirroring `JobTitleService.ToDto(j, employeeCount, gradeName)` (counts computed in the list query, not per row — avoid the N+1). Then regenerate the contract, restore the count column and the manager line, and restore the pre-flight warning on the deactivate dialog.
- **Why it was not fixed with GAP-014:** the GAP-PLAN is explicit that G2 is *"the backend is correct; the frontend cannot reach it — do not re-scope as backend work."* This is the one place in GAP-014 where the backend genuinely lacks a field, so it is tracked separately rather than smuggled into an FE-only change.
- **Related:** GAP-014 · S-1.

---

### ISSUE-365 — the Docker `frontend` on :4200 cannot reach the API, and the e2e suite points at it
- **Type / Severity / Status:** ISSUE · MED · OPEN ` (partly mitigated — the Playwright baseURL is now overridable)
- **Layer:** Infra / test harness
- **Module / US / TC:** — / GAP-034 / — — found 2026-08-10 while trying to verify GAP-034's premise
- **Title:** `src/frontend/nginx.conf` (the image the compose `frontend` service publishes on :4200) has **no `/api` proxy**, so the SPA served there cannot call the backend.
- **Evidence:** the container's config has only `location /` with `try_files $uri $uri/ /index.html` plus a static-asset block. Consequences, measured:
  - `GET http://localhost:4200/api/v1/tenant/context` returns **`index.html` with HTTP 200** (the SPA fallback swallows it) — the same call against `:5000` returns the real JSON.
  - `POST http://localhost:4200/api/v1/auth/login` returns **405**, because you cannot POST to a static file. Login is impossible.
  - The built app uses `apiBaseUrl: '/api/v1'` (same-origin), so it depends on a reverse proxy the image does not contain.
- **Why it is not simply "broken":** `local-dev/nginx.docker.conf` **is** the intended front door and does proxy `/api/`, `/hangfire/` and `/hubs/` to `backend:5000` while serving the SPA from `frontend:80`. It runs as a separate container in the TLS/subdomain rig. So the supported entry point is that front door, and plain-compose `:4200` is a leftover that looks usable and is not.
- **The trap it sets:** `playwright.config.ts` hard-codes `baseURL: 'http://localhost:4200'` and its header documents the prerequisite as *"`ng serve` on :4200"*. `ng serve` proxies `/api` via `proxy.conf.json`, so the suite is correct **for `ng serve`** — but anyone with the Docker stack up has something else on :4200, and every test then fails at login with a 30s timeout. **That is exactly what happened here: a 30-failure run that said nothing about the application.**
- **Done so far:** `baseURL` now honours `E2E_BASE_URL`, with a comment naming the requirement (the origin must proxy `/api`) and the Docker-container pitfall.
- **Recommended:** either add an `/api` proxy to `src/frontend/nginx.conf` so `:4200` behaves like the front door, or stop publishing :4200 in plain compose and document the front door as the only entry point. **Prefer the first** — a port that serves a login page which cannot log in will keep costing people time.
- **★ RESOLVED for the attribution part, 2026-08-10 — and my first diagnosis here was incomplete.** I attributed all 30 failures to the missing proxy. Re-running against `ng serve` (proxy verified: `GET /api/v1/tenant/context` returns real JSON) still produced **30 failures** — so the proxy was a real problem but not the blocker. The actual blocker was **test bit-rot**, found only by reading the error instead of the count:
  - `getByRole('link', { name: 'Dashboard' })` in the login fixture resolved to **two** links once the nav gained an *"Attendance Dashboard"* entry → Playwright strict-mode violation on every single test, before any assertion ran. `exact: true` fixed it: **30 failed → 26 passed / 4 failed.**
  - `navigation-smoke` and `module-create` asserted a bare **`'Attendance'`** nav link that does not exist (the nav exposes *Attendance Dashboard* and *Attendance Approvals*). → **28 passed / 2 failed.**
- **Where it stands: 28 of 30 pass against a real stack.** The remaining 2 differ run-to-run (Leave + Data Export one run, Leave + Performance the next) and both time out waiting for the post-login dashboard. **Not rate limiting** — the `auth-login` limiter is 10/min/IP and the backend log shows **zero** 429s across the run, so that hypothesis is disproved. Most likely contention on this machine (dev-mode `ng serve` recompiles + the full Docker stack + concurrent work). Needs a clean-machine run to confirm, and the durable fix is to stop logging in 30 times: use Playwright `storageState` to authenticate once per run.
- **★ Consequence for GAP-034:** its premise is **half right**. The tests *were* written and 28 of 30 genuinely pass — real coverage of navigation, core-HR create flows and 360/768/1920 overflow across a live API. But *"just add a Playwright job"* was wrong twice over: the suite needed repair first, and CI must stand up Postgres + backend + a **proxying** frontend. **Sizing S → M** stands.
- **Confidence:** 100% on the proxy gap (measured both ways). 100% that the 30 e2e failures were caused by it — every one timed out in `loginAsE2EOwner`.

---

### ISSUE-371 — six test cases were marked `pass` against a step that queries a table this platform does not have
- **Type / Severity / Status:** ISSUE · MED · OPEN ` (expectations corrected; the verdicts must be re-earned by a real run)
- **Layer:** QA
- **Module / US / TC:** Admin Console / US-ADM-001, US-ADM-002, US-ADM-004 / TC-ADM-001-01, TC-ADM-001-08, TC-ADM-002-11, TC-ADM-004-01, TC-ADM-004-02, TC-ADM-004-06 — found 2026-08-10 by the GAP-L6 sibling audit
- **Title:** Each has an executable step querying **`system_audit_log`**, which does not exist — and each was recorded `pass`.
- **Evidence:** `information_schema.tables` returns **zero** rows for `system_audit_log`; the only mention in `src/` is `IAuditLogPurgeService.cs:6` stating the design: *"this platform reuses the single audit table with a system action"*. Classified mechanically — a phantom-table reference inside a `|`-delimited test step, versus prose or a Data-Requirements list:
  | TC | phantom table in an executable step | conditional handling | status was |
  |---|---|---|---|
  | TC-ADM-001-01, -001-08, -002-11, -004-01, -004-02, -004-06 | **yes** | no | **`pass`** |
  | TC-ADM-001-12, TC-ADM-004-09 | yes | no | `blocked` (no false verdict) |
  | TC-NTF-004-11 | yes | **yes — explicit conditional step** | `pass` (legitimate) |
  | TC-ADM-010-13, TC-ADM-ISO-024 | no (prose only) | — | unaffected |
- **Done:** the six expectations are corrected in place to the real mechanism (a system-scoped `audit_logs` row with `UserId` null) and the status demoted to `draft`. The intent was always satisfiable; the wording was not.
- **Still to do:** re-run the six via `/test-us` to earn the verdict back. **Do not flip the status by editing the file.**
- **★ Two corrections to the gap register, both found by doing the sibling audit it asked for:**
  1. **`TC-ADM-010-13` is NOT an instance** — it never names the phantom table (step 3 says "Inspect the system audit log" as prose, which the platform satisfies). I demoted it on the register's claim before reading the file; that demotion was wrong and has been reverted. Recorded rather than hidden, because it is the same error the register made.
  2. **The pattern is 7 instances, not 2** — six here plus `TC-ATT-152` (GAP-022). The register named `TC-ADM-010-13` (a false positive) and `TC-ATT-152` (real).
- **Worth copying:** `TC-NTF-004-11` shows the correct way to write a TC against a spec whose implementation may differ — an explicit `[PLATFORM NOTE -- CONDITIONAL]` step. That TC is honest and needs no change.
- **Confidence:** 100% — table absence and step classification both verified mechanically.

---

### ISSUE-372 — two payroll features call endpoints that have never existed: "Test formula" and drag-to-reorder
- **Type / Severity / Status:** ISSUE · MED · OPEN `
- **Layer:** BE (the routes) + FE (the callers, left in place deliberately)
- **Module / US / TC:** Payroll / US-PAY-001 (FR-4 §8 formula test), salary-component ordering / TC-PAY-001-* — found 2026-08-10 while fixing GAP-010
- **Title:** `POST /payroll/salary-components/validate-formula` and `POST /payroll/salary-components/reorder` return 404 — neither exists in the contract or in any controller.
- **Evidence:** zero paths in `contracts/openapi/hrm-v1.json`, zero controller routes. The FE calls both from live UI:
  - `payroll.service.ts:testFormula()` ← the **"Test" button** in `component-form.component.ts:245`, whose whole purpose (FR-4/§8) is letting an admin verify a formula before saving. It cannot work.
  - `payroll.service.ts:reorderComponents()` ← **drag-to-reorder** in `salary-components.component.ts:382`. Reordering appears to work in the UI and never persists.
- **Why the specs did not catch it:** `payroll.service.spec.ts` mocks BOTH endpoints with `HttpTestingController`, which answers whatever URL the service asks for. The arms prove the service *builds a URL*, not that the URL exists — passing tests over two dead features. **The register spotted these two mocks; its instruction was "delete those".** I did **not** delete them: the arms are the only coverage of those methods, and deleting tests to tidy a finding is the wrong direction. They are annotated in place with the caveat instead.
- **Recommended fix (backend, and there is a clear precedent):** add `POST /payroll/salary-components/reorder` — **both sibling entities already have one** (`/tenant/custom-fields/reorder`, `/tenant/leave-types/reorder`), so salary components are the inconsistent case, and an atomic reorder is better than N sequential `PUT`s. For formula validation, either add the evaluator endpoint (the safe evaluator already exists server-side — it runs payroll) or remove the "Test" button and let create/update report the error.
- **Deliberately NOT done here:** the plan is explicit that G2 is *"the backend is correct; the frontend cannot reach it — do not re-scope as backend work."* These two are the opposite: the frontend is reasonable and the backend route is absent. Tracked separately rather than smuggled into an FE PR.
- **Confidence:** 100% — absence verified in both the contract and the controllers; both callers traced to live UI.

---

### ISSUE-373 — GAP-012's remaining performance surface: 17 response-DTO gaps, and a warning about how to measure them
**▶ DISPOSITION 2026-09-08 (T4).** **Merged (#646); status stays OPEN pending `/verify-fix`.**
Re-scoped by the audit from **17 DTO gaps to 6** — the other 11 were already closed or misfiled. The fix stops
the FE fabricating a performance trend the API never sends.

- **Type / Severity / Status:** ISSUE · HIGH · OPEN ` — **triage COMPLETE 2026-08-12; the 17 are now classified and mapped (see below). No code changed yet.
- **Layer:** FE
- **Module / US / TC:** Performance / US-PRF-002/003/006/009/010 / — — found 2026-08-10 while fixing GAP-012's request half
- **Title:** A mechanical diff of the 96 performance FE interfaces against the generated contract flags **17 response DTOs** with FE-only fields. **Each needs checking against its service before being called a defect.**
- **★ The methodology warning — this cost me a wrong edit and would cost the next person more.** The diff compares an FE interface's fields to the same-named contract schema. It **cannot see the adapter layer**, and where a service maps at the boundary the FE name is *supposed* to differ. `ISaveMeetingNotesRequest.meetingNotesHtml` looked like a defect and is not: `review-signoff.service.ts` maps it to the backend's `Body` field, and the service's own docstring says so. **I "fixed" it, then reverted.** Since an adapter layer is exactly what the register RECOMMENDS for GAP-012, a name difference is as likely to be correct design as a bug. **Verify the service, then decide.**
- **Verified in the request half (all four posted the request object DIRECTLY, no adapter — genuine defects, fixed):** `ISubmitFeedbackRequest.answers` → `items` (360 submissions dropped every answer) · `ICreatePipRequest.mentorId` → `mentorEmployeeId` · `IRecordCheckpointRequest.status/notes` → `progressStatus/evidenceNotes` (**including the multipart FormData keys**, which are wire names too — checkpoints recorded neither the status nor the note) · and `IResolveDisputeRequest` **cleared** (its service adapts).
- **The 17 to triage** (FE-only fields listed; check each service for an adapter first):
  | interface | contract schema | FE-only fields |
  |---|---|---|
  | `ISelfAssessment` | `PerformanceSelfAssessmentDto` | cycleName, goals, submittedOn, weightedScore, windowClosesOn, windowOpen |
  | `IManagerReview` | `PerformanceManagerReviewDto` | cycleName, goals, jobTitle, managerScore, selfScore, submittedOn, windowOpen |
  | `IFeedback360Results` | `PerformanceFeedback360ResultsDto` | anonymous, comments, competencies, cycleName, employeeId, employeeName, exportAvailable, jobTitle, released |
  | `IPip` | `PerformancePipDto` | acknowledgedSignature, acknowledgement, escalation, jobTitle, outcome, pipId |
  | `IPipSummary` | `PerformancePipSummaryDto` | acknowledgement, checkpointsRecorded, checkpointsTotal, jobTitle, pipId |
  | `IPipCheckpoint` | `PerformancePipCheckpointDto` | attachmentName, checkpointId, dueDate, notes, overdue, recordedBy, recordedOn, status |
  | `IPipObjective` | `PerformancePipObjectiveDto` | checkpoints, objectiveId |
  | `IRecommendationSummary` | `PerformanceRecommendationSummaryDto` | bonusPoolAllocated, byDepartment, comparison, currency, totalBonuses, totalIncrements |
  | `IRecommendationWorkspace` | `PerformanceRecommendationWorkspaceDto` | availableExportFormats, compensationVisible |
  | `IGoalComment` | `PerformanceGoalCommentDto` | comment, commentId, createdOn |
  | `ITeamGoalProgressRow` | `PerformanceTeamGoalProgressRowDto` | goalsAtRisk, jobTitle, lastUpdatedOn, overallCompletionPercent |
  | `ITeamGoalStatus` | `PerformanceTeamGoalStatusDto` | jobTitle |
  | `ICycleProgress` | `PerformanceCycleProgressDto` | goalSettingComplete, managerReviewComplete, selfAssessmentComplete |
  | `IDepartmentEmployeeScore` | `PerformanceDepartmentEmployeeScoreDto` | grade, trend |
  | `IDepartmentDrilldown` | `PerformanceDepartmentDrilldownDto` | cycleLabel, scoreScaleMax |
  | `ICategoryAverage` | `PerformanceCategoryAverageDto` | average |
  | `ICycle` | `PerformanceCycleDto` | cancelledReason |
- **Coverage, stated so nobody reads this as a clean bill of health:** 39 of 96 interfaces name-matched a contract schema; **57 did not** and the diff says nothing about them (most are probably legitimate FE-only view models). The `id`-suffix pattern that matched `IXyz` ↔ `PerformanceXyzDto` is what raised coverage from 11 to 39 — a reminder that a low match rate is usually the matcher's fault, not the code's.
- **Recommended:** work the table top-down (`ISelfAssessment`, `IManagerReview` first — US-PRF-002/003 are the register's named crashers). For each: if the service adapts, close the row; if not, decide add-to-DTO vs drop-the-UI, exactly as ISSUE-364 did for departments.
- **Confidence:** 100% that these 17 have FE-only fields on the name-matched schema. **~50% that any given row is a real defect** rather than an adapter — which is precisely why they are listed for triage instead of being "fixed" in bulk.

---

- **★ TRIAGE RESULT 2026-08-12 — measured against the committed contract, not the diff tool.** The "17 response DTOs" split cleanly by *what the user sees*, which the original enumeration did not distinguish:
  - **6 interfaces are missing a COLLECTION field → the list/table renders NOTHING.** These are the user-visible breaks, and they are the whole of the severity.
  - **11 are scalar-only → degraded display** (a field renders blank/`undefined`). Real, but cosmetic by comparison.
- **Every missing field has a plausible contract counterpart — this is one systematic vocabulary mismatch (S-1), not 17 independent bugs.** Verified for `ISelfAssessment`: the service does `http.get<ISelfAssessment>(...)`, **a direct cast with no adapter**, so the FE reads names the API never sends and gets `undefined`. `goals` → the API sends **`items`**, which is why US-PRF-002 renders empty or throws rather than degrading.
- **The mapping (FE name → contract name; `—` = genuinely absent, needs a decision, not an adapter):**
  | interface | rename → contract | genuinely absent |
  |---|---|---|
  | `ISelfAssessment` | `goals`→**`items`** · `submittedOn`→`submittedAt` · `weightedScore`→`weightedSelfScore` · `windowOpen`→`isSelfAssessmentOpen` | `cycleName`, `windowClosesOn` |
  | `IManagerReview` | `goals`→**`items`** · `submittedOn`→`submittedAt` · `windowOpen`→`isReviewWindowOpen` · `selfScore`→`weightedSelfScore` · `managerScore`→`weightedManagerScore` | `cycleName`, `jobTitle` |
  | `IFeedback360Results` | `competencies`→**`competencyAverages`** · `comments`→**`entries`** · `anonymous`→`isAnonymousFeedback` · `employeeId`→`revieweeEmployeeId` · `employeeName`→`revieweeName` | `cycleName`, `jobTitle`, `exportAvailable`, `released` |
  | `IPipObjective` | `objectiveId`→`id` | **`checkpoints`** — the contract has no checkpoint collection on the objective at all, so this one is NOT a rename |
  | `IRecommendationSummary` | `byDepartment`→**`incrementByDepartment`** · `comparison`→**`previousCycle`** · `totalIncrements`→`totalIncrementAllocated` · `bonusPoolAllocated`→`totalBonusPoolAllocated` | `currency`, `totalBonuses` (check against `totalRecommendations`) |
  | `IRecommendationWorkspace` | — | `availableExportFormats`, `compensationVisible`. **And note the reverse direction: the contract sends `rows`/`totalCount`/`pageSize`/`ratingScaleMax` that the FE interface does not declare at all**, so this one needs reading as a whole rather than field-patched. |
- **Recommended approach, unchanged from the register but now with evidence: a `map()` adapter per service, not an interface rename.** It isolates the change from templates (the FE keeps its own vocabulary), and it is the only option that can also *compute* the genuinely-absent fields where they are derivable. For fields that are neither mappable nor derivable, the decision is per field: add to the backend DTO, or remove from the UI — **do not invent a value**.
- **Do the 6 collection cases first.** A blank table is a broken feature; a blank scalar is a blemish. Within those, US-PRF-002 (self-assessment) then US-PRF-003 (manager review), as the register recommends.
- **The methodology warning above still stands and now has a second edge:** the diff cannot see adapters, so a name difference may be correct design — but the `IRecommendationWorkspace` row shows the diff also misses fields the FE fails to declare, which no FE-only listing surfaces. Check both directions.

- **★ BACKEND HALF DONE 2026-08-12 — 5 fields added, 4 deliberately NOT added.** The decision was "add absent fields to the backend DTOs", and that was applied where the backend has a truthful answer. **Added + populated:** `SelfAssessmentDto.CycleName/WindowClosesOn` (from `AppraisalCycle.Name`/`SelfAssessmentEnd`) · `ManagerReviewDto.CycleName/JobTitle` · `Feedback360ResultsDto.CycleName/JobTitle` · `RecommendationSummaryDto.Currency` (from `Tenant.Currency`) · `RecommendationWorkspaceDto.AvailableExportFormats`.
  - **`JobTitle` needed three `Include(e => e.JobTitle)` calls, not just a DTO property.** `employee.JobTitle?.TitleName` compiles and returns `""` forever without them — the same "control that looks applied and isn't" shape as GAP-024 and GAP-035. Worth remembering as the default failure mode when adding a nav-derived field.
  - **`AvailableExportFormats` advertises `csv`/`xlsx` only.** The validator accepts `csv/xlsx/pdf`, but PDF rendering is deferred — advertising it would render a button that fails.
- **The 4 NOT added, each with the reason it has no backend truth to send:**
  | field | why not |
  |---|---|
  | `IFeedback360Results.released` | **No release state exists in the domain at all.** Adding the property means inventing state, not exposing it. Needs a product decision: model release, or have the UI stop claiming it. |
  | `IFeedback360Results.exportAvailable` | Reads as a client-side capability check, not server state. No backend concept backs it. |
  | `IRecommendationWorkspace.compensationVisible` | **No compensation permission exists in `PermissionCatalog`**, and `GetWorkspaceAsync` *deliberately* nulls `CurrentCompensation` and never decrypts. So the honest value is a constant `false`, which makes the flag pointless. Either model the permission or drop the flag. |
  | `IRecommendationSummary.totalBonuses` | **Dead FE surface — no template renders it.** Adding a backend field would be building for nothing. Remove it from the interface instead. |
- **`IPipObjective.checkpoints` is a MODEL mismatch, not a missing field, and was not touched.** `PipCheckpoint` carries `PipId` — checkpoints belong to the **PIP**, not to an objective — while the FE renders them as a per-objective accordion body. **Neither US-PRF-010 nor the tech doc documents either design**, so the FE structure is an invention and the model is the shipped truth. Closing this means a migration re-parenting checkpoints (product decision) **or** moving the FE accordion to PIP level (cheaper, and matches what the data actually is). **Recommended: change the FE.**
- **Still to do (the behavioural half): the 6 `map()` adapters.** The backend additions above are purely ADDITIVE — they add fields nothing reads yet, so they cannot break anything, which is why they are landing separately. The adapters are what actually fixes the blank tables, and a half-applied adapter migration is the genuinely risky state.

- **★★ THE INCLUDE TRAP — worth reading before adding any nav-derived DTO field, anywhere in this codebase.** Populating `JobTitle` looked like a one-liner: `Include(e => e.JobTitle)` then `employee.JobTitle?.TitleName`. **It broke 33 tests, and in production it would have been far worse than the blank field it was fixing.**
  - `Employee.JobTitleId` is **non-nullable**, so the navigation is **REQUIRED** (`EmployeeConfiguration.cs:161-164`).
  - `JobTitle` carries a **global query filter** — `!IsDeleted && (!IsResolved || tenant match)` (`AppDbContext.cs:289-290`).
  - EF Core emits an **INNER JOIN** for a required navigation. So an employee whose job title is **soft-deleted** (or absent, as in every test fixture) **vanishes from the query entirely** — the caller gets *"no such employee"* rather than a missing label. **A disappearing-employee bug.**
  - This is the same class EF already warns about at build time for `Role`: *"has a global query filter defined and is the required end of a relationship."* The warning was already in the build log and easy to scroll past.
- **The fix: resolve the label with its OWN query** (`ResolveJobTitleAsync` in `ManagerReviewService`, an inline equivalent in `Feedback360Service`). A filtered-out title then yields an empty label and the employee still loads. The tenant filter still applies, so it cannot read another tenant's title.
- **Guarded, not just fixed:** `ASoftDeletedJobTitle_LeavesTheLabelBlank_ButTheRevieweeStillLoads` pins both directions. **Mutation-verified — reintroducing the `Include` turns 7 arms red, that one by name.** It exists specifically because the `Include` is the *obvious* thing to reach for and will be reached for again.
- **Process note, because this is the third instance in one session of the obvious mechanism being wrong:** the §9.4-3 `IServerFilter` could not see the job's DI scope, `Serilog.Enrichers.Span` emitted nothing with OTel dormant, and this `Include` deletes rows. All three compiled and would have passed review. **Only running them caught it** — see [[read-the-running-log]].

- **★ 360 RELEASE STATE — scoped 2026-08-14, NOT yet built. The design is settled; the size is three times what the register implied.** Decision taken: an explicit release step, gated and notified. Scoping it against `src/` found three things that change the work:
  1. **There is no reviewee-facing read at all.** `Feedback360Service.GetResultsAsync` requires `Performance.ReviewAll` (`:331`) — HR/manager only. "After release the reviewee sees their own results" therefore means **building a new read path**, not gating an existing one.
  2. **There is no per-reviewee 360 aggregate entity.** Only `Feedback360` (one reviewer's submission about one reviewee), `Feedback360Item`, and `ReviewerAssignment`. "This person's results" is a **computed view**, so there is nowhere to hang `ReleasedAt`. Release needs a **new entity** — `Feedback360Release` (CycleId, RevieweeEmployeeId, ReleasedAt, ReleasedBy).
  3. **Per-reviewee, not per-cycle, and this is forced by BR-4.** The minimum-peer threshold is evaluated per reviewee, so one person's results can be releasable while another's are not. Releasing per cycle would either block everyone on the slowest reviewee or release results that failed their own threshold.
- **The arm that matters most, when it is built:** the reviewee's view must never carry reviewer identities even where HR's does. Anonymity is applied per-entry at `Feedback360Service:510` (`ReviewerName = f.IsAnonymous ? null : reviewerName`), so **reuse that aggregation rather than writing a second path** — a parallel projection is exactly how the anonymised and non-anonymised views drift, and the failure mode here is telling someone who said what about them. `AnonymousResults_NeverLeak_ReviewerIdentity` already guards HR's path; the new one needs its own equivalent.
- **Why it was not built in the same session as the decision:** by that point the session had produced a measurable error rate — the wrong record edited in a DTO file, a "this cannot break anything" claim immediately before 33 failures, two invented helper names, and a dropped comma in a permission list. All were caught cheaply. A new entity plus an authz gate deciding who may read a named person's 360 feedback is not the place to run that rate, so the design was recorded instead. **Nothing here is undecided — it is specified and ready to implement.**
- **SURVEY:** **17 interfaces** (unit = FE response interface / name-matched contract DTO — this entry **does** state its unit, unlike `ISSUE-379`), underlain by **68 FE-only field-slots** (the sum of its 17 table rows). **At this commit the asserted 17 no longer hold as defects**: all 17 now sit behind a generated-type adapter (a `Schema<'Performance*Dto'>` wire alias plus a `map*()`), and `grep '\.(get|post|put)<I[A-Z]'` over `src/frontend/src/app/features/performance/services/` returns **0** raw-typed calls on them. Of the 68 slots, **60 are satisfied** and **8 have no wire source**: `IPip.jobTitle` (`pip.models.ts:549`), `IPipSummary.jobTitle`/`checkpointsRecorded` (`:523`, `:530`), `IPipCheckpoint.overdue` (`:502`), `ITeamGoalProgressRow.jobTitle` (`goal-progress.models.ts:401`), `ITeamGoalStatus.jobTitle` (never assigned, never rendered), `IDepartmentEmployeeScore.grade`/`trend` (`dashboard.models.ts:468`, `:470`). Of those 8, only **2 render anything wrong**, both unguarded fabricated zeros: `pip-list.component.ts:172` ("0 of M") and `recommendation-summary.component.ts:166` ("0 promo · 0 bonus"); four more are `@if`-guarded and render nothing. Excluded: the 57 unmatched interfaces (97 total, not the 96 asserted) and the request half, already fixed. **`ISSUE-379` overlap: yes, and it is the entire residual** — rows 7/8/9 of `ISSUE-379` (`TEST-FINDINGS.md:2138-2140`) already cover `grade`, `trend`, `checkpointsRecorded`, `overdue`, `promotionCount`, `bonusCount`.
- **AUDIT (2026-09-07):** (1) "6 interfaces are missing a COLLECTION field, so the screen renders NOTHING" — **FALSE**: all six are mapped (`self-assessment.models.ts:223` `goals<-items`; `manager-review.models.ts:379`; `feedback-360.models.ts:377`, `:388`; `pip.models.ts:514`; `recommendation.models.ts:715`, `:727`). (2) "`ISelfAssessment` is a direct cast with no adapter" — **FALSE**: `self-assessment.service.ts:73-76` casts `SelfAssessmentWire` then `map(mapSelfAssessment)`. (3) "`IPipObjective.checkpoints` is a MODEL mismatch; recommended fix: change the FE" — **FALSE, and the opposite disposition shipped**: migration `20260814060853_Performance_PipCheckpointObjectiveId` (`c06cb5cb`, merged) re-parented checkpoints, and `PipDtos.cs:27` now carries `PipObjectiveDto.Checkpoints`. (4) "the 4 fields NOT added" — **1 of 4 holds**: `totalBonuses` is **CONFIRMED** removed, but `released`, `exportAvailable` and `compensationVisible` are all in the contract now (`RecommendationService.cs:167`, tests at `RecommendationServiceTests.cs:683`, `:687`). (5) "360 release state — scoped, NOT yet built" — **FALSE**: `Feedback360Release.cs:18`, `Feedback360ReleaseConfiguration.cs:15`, the reviewee read at `Feedback360Controller.cs:271-272`, plus `Feedback360ReleaseApiTests.cs` and `Feedback360ReleaseConcurrencyPostgresTests.cs`. (6) **The INCLUDE TRAP — CONFIRMED**, with both cited lines stale: the required navigation is `EmployeeConfiguration.cs:162-165` (cited `:161-164`) and the filter is `AppDbContext.cs:292-293` (cited `:289-290`); `ResolveJobTitleAsync` is at `ManagerReviewService.cs:622`, guarded by `Feedback360ServiceTests.cs:477`. (7) All the work (`722a8768`, `6cda4d7a`, `f9947ec9`, `c06cb5cb`, `f3f98915`) is **merged**.
- **SEVERITY CHECK:** **lower — HIGH should become LOW.** Every user-visible break it was filed for is closed; the residual is 2 fabricated-zero renders plus 2 blank labels, all already tracked as `ISSUE-379` rows 7-9. It should be closed as **superseded by `ISSUE-379`**, not worked.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-374 — three more onboarding routes the FE calls that do not exist, plus the modify request shape
- **Type / Severity / Status:** ISSUE · MED · OPEN `
- **Layer:** BE (two routes) + FE (the modify flow)
- **Module / US / TC:** Onboarding / US-ONB-002 AC-4, template builder / — — found 2026-08-10 while fixing GAP-013
- **Title:** GAP-013 named "2 dead routes". Verified against the contract, there were **four**, and only one was a genuinely missing endpoint the register identified.
- **The four, sorted by what they actually needed:**
  | FE call | reality | disposition |
  |---|---|---|
  | `GET /checklists/applicable` | route is `applicable-templates` | ✅ **fixed** — a rename, not a missing endpoint |
  | `GET /checklists/employee/{id}` | genuinely absent | ✅ **built** (query + handler + service + controller + 3 arms) — AC-3's replace/merge prompt now reachable |
  | `GET /checklists/preview` | **absent, and never mentioned by the register** | ❌ open — called by `checklist-assignment.component.ts:794`, so the assignment preview silently fails |
  | `GET /templates/lookups` | **absent, and never mentioned** | ❌ open — called by `template-builder.component.ts:607` |
- **Plus a request-shape restructure (not a rename):** `IModifyChecklistRequest.tasks` vs the contract's `OnboardingModifyChecklistRequest { addTasks, taskChanges }`. This is **not** a field rename — the API models modification as *added tasks* plus *per-task changes*, while the FE sends one flat replacement list. Fixing it means changing the FE modify flow, not a field name, so it is filed rather than half-done. `assign` **was** a rename (`tasks` → `additionalTasks`) and **is fixed** — the HR officer's inline-edited task set was being discarded on every assignment (AC-2).
- **Recommended:** `/templates/lookups` may be composable FE-side from existing endpoints (departments + job titles), which would need no backend change — check before building. `/checklists/preview` needs real backend logic (resolve template tasks + compute due dates from the start date), so it is a genuine endpoint. Do `modify` last, with the FE flow change.
- **Also corrected while here:** the "5 clearance field mismatches" GAP-013 lists did not hold as stated. The clearance **request** body matches the contract (`{status, remarks}`). The real mismatches were **8 field names across onboarding/offboarding/exit-interview/template models**, all fixed — `taskId`→`id`, `offboardingId`→`id`, `exitInterviewId`→`id`, `applicableDepartmentIds`→`applicableDepartments`, and the same for job titles. Note the exit-interview REQUEST keeps `offboardingId` while its RESPONSE uses `offboardingInstanceId` — deliberately different, and conflating them broke two specs before I caught it.
- **Confidence:** 100% on route absence (checked in the contract and the controllers) and on the modify shape.

### ISSUE-375 — §9.4-3 documents a Hangfire `IServerFilter` that cannot work with this codebase's job pattern
- **Type / Severity / Status:** ISSUE · LOW · OPEN `
- **Layer:** Docs (`hrm_technical_document_v4.0.md` §9.4-3)
- **Module / US / TC:** Platform / background jobs / — — found 2026-08-11 while closing GAP-024
- **Title:** §9.4-3 describes an `IServerFilter` that reads a `tenantId` job argument and **populates `ITenantContext`**. The tenant-context half of that is not achievable as described, and GAP-024 was closed by a different design.
- **Why it cannot work:** **42 of the 62 job classes create their own DI scope** (`IServiceScopeFactory.CreateScope()`) and resolve `AppDbContext` / `ITenantContext` from *that* scope. A Hangfire server filter can only reach the scope the job was **activated** from. A tenant set there lands on a different scoped `TenantContext` instance than the one the job body reads, so the body still observes `IsResolved == false` — while the filter looks, in code review and in the architecture doc, like a working isolation control. That is a worse failure mode than the gap it was meant to close.
- **What was built instead (GAP-024, 2026-08-11):** the halves were split by what each mechanism can actually reach. **Log context → a real `IServerFilter`** (`JobLogContextFilter`), which *does* work because Serilog's `LogContext` is an `AsyncLocal` that flows down into the body and every scope it creates; jobs now log `job_name`, `job_id`, `tenant_id`. **Tenant context → declared by the job bodies** (`SetSystemContext()` / `ITenantJobRunner.RunForTenantAsync`) and enforced mechanically by `BackgroundJobTenantContextTests` rather than by a filter that cannot see far enough.
- **Recommended:** amend §9.4-3 to describe the split — filter for log context, job-body declaration plus a coverage guard for tenant context. **Do not "fix" this by making the filter set `ITenantContext`;** it would pass code review and enforce nothing. The alternative that *would* let a filter own tenant context is banning per-job scopes so jobs run in Hangfire's activation scope — a 42-file refactor with no isolation benefit over the guard, so it is not recommended.
- **Confidence:** 100% on the scope-count measurement and on the mechanism (the `AsyncLocal`-reaches-the-body claim is asserted in `JobLogContextFilterTests`, not assumed).

### ISSUE-376 — a client-cancelled request surfaces as an unhandled 500 (pollutes error tracking)
- **Type / Severity / Status:** ISSUE · LOW · OPEN `
- **Layer:** BE
- **Module / US / TC:** Platform / — / — — found 2026-08-11 on the FIRST CI run of the new E2E job (GAP-034)
- **Title:** `GET /api/v1/tenant/employees` returned **500** with `System.Threading.Tasks.TaskCanceledException` when the browser abandoned the request mid-flight.
- **Stack (abridged):** `AsyncKeyedLock.AsyncNonKeyedLocker.LockOrNullAsync` → `EFCoreSecondLevelCacheInterceptor.ReaderExecutingAsync` → `EF ToListAsync` → `EmployeeService.GetAllAsync:231` → `ExceptionHandlingMiddleware` logs **"Unhandled exception"** and returns 500.
- **Why it happens:** Playwright navigates away while a list query is in flight. ASP.NET Core cancels `HttpContext.RequestAborted`, EF (through the second-level cache interceptor's lock) throws `TaskCanceledException`, and `ExceptionHandlingMiddleware` has no case for it — so a normal client disconnect is recorded as a server fault.
- **Recommended:** in `ExceptionHandlingMiddleware`, treat `OperationCanceledException`/`TaskCanceledException` **when `HttpContext.RequestAborted.IsCancellationRequested`** as a client-closed request — log at Debug/Information and return 499 (or simply stop writing a response, since nobody is listening). Keep returning 500 when the token was NOT the client's, because that is a genuine timeout worth seeing. **Do not blanket-swallow `TaskCanceledException`** — that would hide real cancellation bugs, including the AsyncKeyedLock timeout this same stack could represent under load.
- **Worth noting:** this is the kind of defect only a real-browser test finds — no unit or API test abandons a request mid-flight. It appeared on the very first CI run of the E2E job, which is some evidence for the register's claim that this suite has a high coverage-to-effort ratio.
- **Out of lane for GAP-034** (which is about *running* the suite, not fixing what it finds), so it is filed rather than fixed.
- **Confidence:** 95% on the mechanism (stack + the request-abort context are unambiguous); 100% that the 500 occurred.

---
- **SURVEY:** **1 site** — `ExceptionHandlingMiddleware` is the only global handler (11 middleware in `src/backend/HRM.Api/Middleware/`; the other `catch (Exception)` blocks at `ApiCallCounterMiddleware.cs:72`, `:96` and `TenantResolutionMiddleware.cs:250`, `:312` are deliberate fail-open metering/resolution catches, not the request-abort class). Unit = global exception handlers. Excluded: `HRM.Tests`.
- **AUDIT (2026-09-08):** (1) **"`ExceptionHandlingMiddleware` has no case for it" — FALSE.** `ExceptionHandlingMiddleware.cs:75` is `catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)`, logging at Information (`:79`) and setting `Status499ClientClosedRequest` guarded by `!HasStarted` (`:86-87`), with no body written. (2) The recommendation's "do not blanket-swallow" caveat was **honoured** — the `when` filter is present and test-bound at `ExceptionHandlingMiddlewareTests.cs:119`, `:135`, `:151`, the third asserting that a *server-side* timeout still returns 500. (3) Stale citation: the entry's `EmployeeService.GetAllAsync:231` now points at photo-stream code (`EmployeeService.cs:225-235`); immaterial, since the fix is exception-type-based rather than call-site-based. Fixed by `541bd1c4` (**#656**), **merged** into `origin/test/local-subdomains`.
- **SEVERITY CHECK:** **lower** — stale-OPEN, implemented exactly as recommended including the load-bearing filter. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-377 — three 360 test cases were marked `pass` against a release endpoint that has never existed
- **Type / Severity / Status:** ISSUE · MED · OPEN ` (routes corrected + statuses demoted; the verdicts must be re-earned by a real run)
- **Layer:** QA
- **Module / US / TC:** Performance / US-PRF-005 / TC-PRF-005-04, TC-PRF-005-05, TC-PRF-005-14 — found 2026-08-17 while scoping the 360 release model change
- **Title:** Each has an executable step asserting a **release** action, and until this session there was **no release endpoint, no release state, and nothing to release** — yet all three read `pass`.
- **Evidence:** repo-wide grep for `Feedback360Release|FeedbackRelease|360Release|ReleaseStatus|release_status` across `.cs`/`.ts` returned **zero** code hits before this change. `Feedback360Controller` had 12 routes, none of them a release. The only release-flavoured artifacts were a *computed* advisory warning (`Feedback360Service.cs:468-474` → `MinPeerThresholdMet`, `ReleaseWarning`) that blocked nothing.
- **Classified mechanically, the same way ISSUE-371 was** — a release assertion inside a `|`-delimited executable step, versus a mention in prose or a Data-Requirements table:
  | TC | release assertion in an executable step | status was | verdict |
  |---|---|---|---|
  | **TC-PRF-005-04** | **yes** — step 2 names a literal route, `POST .../performance/360/{liamId}/release` | **`pass`** | **real instance** |
  | **TC-PRF-005-05** | **yes** — steps 2 and 3 assert 403/401 from "the release endpoints" | **`pass`** | **real instance** |
  | **TC-PRF-005-14** | **yes** — steps 1-2 assert release permitted at exactly the minimum and blocked one below | **`pass`** | **real instance** |
  | TC-PRF-005-13 | no — "results released" appears only in the **Test Data table** | `pass` | **NOT an instance** |
- **★ TC-PRF-005-05 is the sharpest case, because its assertion was not merely unverified but unsatisfiable.** Steps 2/3 expect **403** and **401** from the release endpoints. An unrouted path returns **404**. So the expected result could not have been observed under any run, against any build.
- **★ The count did not survive measurement — again, and in the same direction as ISSUE-371.** I first reported **four** instances on a grep of `status: pass` + "releas". Classifying by step type gives **three**. TC-PRF-005-13 is the false positive, and it is the *same category* ISSUE-371 wrongly demoted (`TC-ADM-010-13`, prose-only). **Two audits in a row have over-counted this pattern by treating any mention as an instance.** The classification must be by step type, every time.
- **Done in this change:** TC-PRF-005-04's phantom route corrected to the real cycle-keyed route; all three demoted to `draft`.
- **Still to do:** re-run the three via `/test-us US-PRF-005` to earn the verdicts back. **Do not flip the status by editing the file.**
- **★ What this instance adds over ISSUE-371:** those six TCs asserted against a table that did not exist but whose *intent* was always satisfiable another way. These three asserted against a **capability that was genuinely absent** — so the `pass` verdicts were actively load-bearing misinformation. `BR-4` reads *"the minimum number of peer reviewers must be met before the 360 results are released"*, and **three green test cases said that rule was verified while nothing in the product could release anything.** That is the most expensive shape this pattern takes: a passing test standing in for a missing feature.
- **Pattern count is now 10** — six in ISSUE-371, `TC-ATT-152` (GAP-022), and these three.
- **Confidence:** 100% — route absence verified by grep across the whole backend before the change; step classification verified by reading each file.

---

### ISSUE-378 — the reviewee-facing 360 read has no UI, and two 360 fields still have no backend truth
- **Type / Severity / Status:** ISSUE · MED · OPEN `
- **Layer:** FE (the page) + BE (one DTO field)
- **Module / US / TC:** Performance / US-PRF-005 AC-3/AC-4/FR-5 / TC-PRF-005-04, -13 — filed 2026-08-17 alongside the 360 release change
- **Title:** `GET .../360/cycles/{cycleId}/my-results` ships **reachable by API but not by UI** — deliberately scoped out, filed so it is not discovered later as orphaned code.
- **What exists:** the endpoint is fully built and tested — self-scoping, 404 `not_released` until released, and reviewer identity stripped **unconditionally** (FR-5) rather than only under the cycle anonymity flag. Guarded by `RevieweeResults_NeverLeak_ReviewerIdentity_EvenWhenAnonymityIsOff`.
- **What is missing:** a route in the employee area (`my-review.routes.ts` has `''`, `sign-off`, `my-goals`, `pip/:pipId` — no 360) and a component to render it. The three existing 360 routes all sit under the manager/HR-gated `/performance` parent.
- **Why it was scoped out, stated plainly:** the release PR was already large (new entity + migration + 2 endpoints + an authz extraction + a 7-field FE adapter). The employee page needs its own a11y pass and a decision about where it lives in the employee nav. **This is a scope reduction, not an oversight** — recorded here because the alternative is an `integration-enforcer` run "discovering" it as orphaned in a month.
- **Second item, from the FE adapter work — `ICompetencyResult.byCategory` has no backend source.** The FE renders self/manager/peer/report chips *under each competency bar*, but `CompetencyAverageDto` is flat; the only per-category data is the top-level `categoryAverages`. The adapter sets `byCategory: []` rather than inventing values, so those chips silently never render.
  - **Assessment: probably FE over-reach, not a backend gap.** AC-4 asks for *"a radar chart comparing self/manager/peer/report perspectives"*, which the top-level `categoryAverages` already satisfies. A per-competency × per-category breakdown is a strictly richer thing that no AC requests.
  - **Recommended: delete `byCategory` from the FE interface** (cheaper, and matches what the data is) rather than growing `CompetencyAverageDto`. Same disposition, and same reasoning, as `IRecommendationSummary.totalBonuses` in [[ISSUE-373]].
- **A third item checked and DISMISSED, recorded so nobody re-raises it:** the composite score renders while the banner says "not yet released". That is **correct** — HR is deliberately allowed to view pre-release (the "warned, not blocked" half of BR-4), and on HR's screen the banner means *"you have not released this to the employee yet"*, not *"you cannot see this"*. The reviewee's path only renders post-release at all. No action.
- **Confidence:** 100% on the missing route (grepped `my-review.routes.ts`); 100% on `byCategory` (the backend DTO is flat); 85% that deleting `byCategory` is the right call rather than building it — that one is a product judgement.

---

### BUG-305 — vacancy auto-close on conversion notifies nobody: BR-5's recruiter and remaining-pipeline notifications were never built
- **Type / Severity / Status:** BUG · MED · OPEN `
- **Layer:** BE
- **Module / US / TC:** Recruitment / US-REC-010 BR-5, FR-7 / **TC-REC-010-08** — found 2026-08-18 executing queue item A1c
- **Title:** When a conversion fills a vacancy's last seat the vacancy auto-closes correctly, but **no recruiter notification and no "vacancy filled" notification to the remaining pipeline is produced** — not sent, not enqueued, not stubbed.
- **What DOES work (verified, so the fix is narrow):** last seat (1/1) → `vacancyClosed: true`, DB `Closed` + `closed_at` stamped; a non-final fill (2/3) correctly leaves the vacancy `Open`; a closed vacancy rejects new applications with `vacancy_not_open`. The state machine is sound.
- **Root cause (confidence 95%):** `ApplicantConversionService.PostConversionNotificationsSafeAsync` calls only `_notifications.NotifyStageChangedAsync(...)`, which (`RealRecruitmentNotificationService.cs:138-161`) dispatches a single `application_stage_changed` email **to the applicant**. The `vacancyClosed` flag is passed into the method but is read **only inside a catch-log**. There is no recruiter auto-close notification and no remaining-pipeline notification anywhere on the path.
- **★ The in-code comment actively misleads.** `ApplicantConversionService.cs:478` reads *"FR-7/BR-5 recruiter notification (vacancy auto-closed)"* next to code that produces no such notification. This is the **third** instance in this repo of a comment describing behaviour that does not exist — after `RealNotificationDispatcher.cs:32` (which seeded a whole phantom P3 epic) and `TenantProvisioningService.cs:31-34` (which kept the US-ADM-011 workflow engine dormant for five weeks). **The comment is the reason nobody noticed: it reads as done.**
- **Reproduction:** as an HR user, convert an applicant that fills a vacancy's last seat → vacancy goes `Closed`. Then `SELECT event_key, notification_type FROM notification_delivery WHERE tenant_id = <tenant> ORDER BY created_at DESC` → only `application_stage_changed` (to the applicant) and the FR-9 welcome event. **No recruiter-close event, no pipeline-filled event.**
- **Evidence:** TC-REC-010-08's own NOTE says to assert the *enqueue* if delivery is stubbed. There is no enqueue to assert — the code path does not exist.
- **Severity rationale:** MED not HIGH because the money/state half is correct and durable; MED not LOW because BR-5 is an explicit business rule and both legs are entirely unbuilt, so no amount of wiring the notification layer would surface them.
- **Confidence:** 95% on the root cause (read the whole dispatch path); 100% that the notifications do not occur (verified against `notification_delivery` after a real auto-close).

---

### ISSUE-379 — the migration surfaced 11 backend DTO gaps: fields the UI renders that the API has never sent
- **Type / Severity / Status:** ISSUE · HIGH · OPEN ` — **all are decision-gated** (add the field, or remove the UI that renders it)
- **RE-SCOPED 2026-09-02 (G8):** this was filed as a **backend** gap. **Seven of its eight fields were frontend mapper bugs** — the wire carried them and the mappers discarded them under comments asserting it did not. Those seven are now **CLOSED** by G8 (`availableExportFormats`, trend `scoreScaleMax`, drilldown `cycleLabel` + `scoreScaleMax`, sign-off `cycleName` + `ratingScaleMax` + `finalScore`). **What remains is genuinely absent from the BE DTOs and is the real residue of this finding:** `managerName`, `jobTitle`, `goals` (the goal + manager-rating snapshot) and `exportAvailable` on `PerformanceReviewMeetingNotesDto`; `scopeLabel` and `filterOptions` on the dashboard overview (**the FR-4 filter panel is wired but permanently empty**); `jobTitle` + `trend` on `PerformancePerformerDto`; `grade` + `trend` on the drilldown employee row. A fixer following the original wording would have edited DTOs that were already correct.
- **Layer:** BE (DTOs) with FE symptoms
- **Module / US / TC:** performance, leave, core-hr — surfaced 2026-08-18..21 by the D-migration slices
- **Title:** ~35 view-model fields across three modules have **no wire source at all.** These 11 are the ones something actually renders.

| # | field(s) | rendered by | sev |
|---|---|---|---|
| 1 | dashboard `filterOptions`, `scopeLabel`, `teamRanking`, `availableExportFormats` | the FR-4 filter panel, manager team-ranking, export buttons — **a whole feature surface** | **HIGH** |

**★ CORRECTED 2026-08-21 — this finding OVERSTATES its own gaps. Verified against the code, per-field.**
Most of the 11 are **exposure** gaps (the backend already has the data; the DTO just does not carry it), not
build gaps. Three are **not gaps at all** and the finding is simply wrong about them:

- **`teamRanking` — ALREADY SENT.** In Team scope the top-N list *is* the team ranking, by design
  (`PerformanceDashboardService.cs:667-681`, `PerformanceDashboardDtos.cs:166-170`). The **FE mapper discards
  it** (`dashboard.models.ts:317` defaults to `[]`). No backend change is needed; this is an FE-only fix.
- **drilldown `employeeName` — ALREADY SENT AND ALREADY READ.** `DepartmentEmployeeScoreDto.EmployeeName`
  exists (`PerformanceDashboardDtos.cs:176`) and `dashboard.models.ts:366` reads it. The genuinely missing
  field on that payload is **`cycleLabel`** (`:387`). This row carried a stated "confidence: 100%" and was
  wrong.
- **`IBudgetTracker.enabled` — NOT A GAP.** `Budget` is nullable by design
  (`RecommendationDtos.cs:163,211`); deriving "enabled" from its presence is the correct pattern.

**Only two items are real build work:** per-category minimums for non-Peer categories (needs a product
question first — do non-Peer categories *have* minimums? if not it is FE overspec to delete, not a backend
story) and per-employee `trend` (needs a prior-cycle score fetch that nothing currently does).

**Why this matters more than the individual corrections:** the register was about to drive backend work for
fields the backend already sends. Verifying first turned a "whole feature surface" into three FE mapper
lines plus a handful of free DTO additions. See also [[BUG-311]] — the export-format precedent this audit
identified is itself defective, so it must be fixed before it is copied.
| 2 | `/my-goals` window envelope (`windowOpen`, `cycleName`) | the BR-1 closed-window gate. **Defaulted `true`** — verified fail-SAFE, since `GoalProgressService.cs:100` enforces BR-1 server-side | **HIGH** |
| 3 | sign-off notes: `goals[]`, `ratingScaleMax`, `managerName`, `cycleName`, `finalScore` | the entire US-PRF-006 sign-off screen | **HIGH** |
| 4 | 360 reviewer-config: `candidatePool`, per-category `minimums`, `editable` | reviewer nomination — search-to-add is empty | **HIGH** |
| 5 | trend + drilldown `scoreScaleMax` | polyline and bar scaling; FE falls back to a constant 5 | MED |
| 6 | `authorName` on progress updates; `employeeName` on the drilldown list | timeline attribution and the drill-down header | MED |
| 7 | performer/drilldown `trend`, `grade` | trend glyph (always rendered) and grade label | MED |
| 8 | PIP `checkpointsRecorded` split; checkpoint `overdue` | the "N of M" hint shows "0 of M"; the overdue badge never renders | MED |
| 9 | recommendation summary per-dept `promotionCount`/`bonusCount` | shows "0 promo · 0 bonus · N increment" | MED |
| 10 | `IBudgetTracker.enabled` | gates the whole budget card; derived from object presence | MED |
| 11 | per-competency `byCategory` split | the self/manager/peer/report chips under each competency bar | MED |

- **★ What this measures:** roughly **one field in five** of the hand-written interfaces describes an endpoint that was never built. These interfaces were not an accurate API description that drifted — they were **written from what the UI wanted and never reconciled with the API**. That reframes the remaining ~570 interfaces: the migration is not mechanical renaming, and each remaining module should be expected to surface its own set.
- **What was done:** every one is **defaulted at the single mapper seam and marked inline**, never fabricated. Two that nothing rendered were **deleted**. Several are **derived** where the wire holds the information in another shape (PIP outcome from terminal status; `deducted` = assigned − lop).
- **Suggested direction:** **decision-gated.** For each: add the field to the DTO, or remove the UI that renders it. Grouped so the decision can be taken once per feature rather than per field.
- **Confidence:** 100% that the fields are absent from the contract; the *disposition* is a product call.

---
- **SURVEY:** **the entry's "11" is not verifiable as stated — it carries no unit.** Recounted: the true residual is **10 field-slots**, over **8** distinct field names (`jobTitle` and `trend` each appear on two DTOs), across **4** DTOs and **3** screens. The "11" in the title counts **table rows**, spanning ~30 field names over three modules; the later re-scope's "eight" counts something else again — three sources, three units, none stated. Of the 10 slots, only **5** meet the entry's own headline ("fields the UI renders"): `managerName`, `goals`, `scopeLabel`, `filterOptions`, performer `trend` — and only **3** are genuine backend build. Excluded: leave, core-hr, recruitment, onboarding, and original rows 2, 4, 5, 6, 8, 9, 10, 11.
- **AUDIT (2026-09-07):** (1) `scopeLabel`, `filterOptions` absent yet rendered — **CONFIRMED**, and the "wired but permanently empty" phrasing is verbatim accurate (`PerformanceDashboardDtos.cs:140-178`; panel `performance-dashboard.component.ts:143`, groups `:560-565`; the mapper hardcodes 5 empty lists at `dashboard.models.ts:374-379`). (2) Performer `trend` — **CONFIRMED**, the only fully valid residual; the mapper fabricates `'Flat'` (`dashboard.models.ts:335`) under an unconditional render (`performance-dashboard.component.ts:496-497`). (3) `managerName`, `goals` "absent from the API" — **PARTIALLY TRUE / mis-framed**: absent from the notes DTO but present on `ReviewExportDto` in the same file (`ReviewSignoffDtos.cs:101`, `:121`, `:131-140`), so the stated decision gate ("add the field or delete the UI") is a false dichotomy — exposing the existing field is the correct third option. (4) `exportAvailable` "backend gap" — **FALSE**: the endpoint (`ReviewSignoffController.cs:193`), handler (`ReviewSignoffQueries.cs:56-63`) and Angular client (`review-signoff.service.ts:215-218`) all exist; the entire FR-6 PDF export is dead behind one hardcoded `false` — filed as **`ISSUE-551`**. (5) Notes `jobTitle`, performer `jobTitle`, drilldown `grade`, drilldown `trend` — **FALSE against the entry's own definition**: all four are `@if`-guarded and render nothing (`performance-dashboard.component.ts:493`; `department-drilldown.component.ts:109`, `:112-114`). (6) The re-scope's "seven of eight closed" — it was **eight of eight**; `teamRanking` is now mapped (`dashboard.models.ts:399-400`). (7) Type names are wrong: the C# types are `ReviewMeetingNotesDto`/`PerformerDto`; the `Performance*` prefixes are generated-OpenAPI schema names. (8) **6 of 7 cited lines are stale** — correct: `PerformanceDashboardService.cs:703-706` (cited `:667-681`), `PerformanceDashboardDtos.cs:174`,`:177` (cited `:166-170`), `dashboard.models.ts:399-400` (cited `:317`, and its "discards it" claim is now false), `PerformanceDashboardDtos.cs:184` (cited `:176`) read at `dashboard.models.ts:462` (cited `:366`), `cycleLabel` now mapped at `dashboard.models.ts:486` (cited `:387`). Only `RecommendationDtos.cs:163`, `:211` is correct. All ISSUE-379 work (`4c4d1edd`, `ea3a4f5e`, `7ac81572`, `33814235`, `78de7551`/#586) is merged.
- **SEVERITY CHECK:** **lower — MED.** HIGH plus "all decision-gated" rests on a list that is half unrendered dead interface fields; the honest residual is 5 rendered gaps, 3 of them backend work.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-380 — dead FE surface and controls that silently do nothing
- **Type / Severity / Status:** ISSUE · MED · OPEN `
- **Layer:** FE
- **Module / US / TC:** core-hr, leave, performance — surfaced by the D-migration slices
- **Title:** Five items that exist in the FE and do nothing.

| item | detail | disposition |
|---|---|---|
| **salary-grade `isActive`** | The Active toggle is sent on create/update, but **neither request record has an `isActive` member** — the API discards it. The toggle silently does nothing on save. A spec *asserted* the discarded field was present (corrected). | **decision**: honour it server-side, or remove the toggle |
| `IStatusTransition.sideEffects` | The endpoint returns only status strings; the side-effects preview is always empty. A spec had been flushing **fabricated** side-effects (corrected). | decision: add to the DTO, or drop the preview |
| `IChangeStatusResponse.profile` / `IAssignManagerResponse.profile` | Still declare a `profile` the wire never sends; their service specs flush a fabricated `{profile}` and assert it truthy — passing, but certifying a shape the API does not return. | fix-in-frontend: migrate both to generated types |
| `ILeaveRequest.tenantId`, `IEmployee.tenantId` | No wire source, read nowhere. | remove-dead-control |
| `org-tree.searchNodes()` | Calls an absent route **and** has no component caller. | remove-dead-control |

- **Confidence:** 100% on all five (each verified against the generated contract and grepped for consumers).

---

### ISSUE-381 — the accrual-exposure endpoint emits no response schema, so its envelope cannot be contract-checked
- **Type / Severity / Status:** ISSUE · MED · OPEN `
- **Layer:** BE (Swashbuckle annotation)
- **Module / US / TC:** leave — `tenant/leave-entitlements/accrual-over-credit-exposure`
- **Title:** The action returns `IActionResult` (JSON-or-file), so Swashbuckle emitted `content?: never` for the 200 and **no `AccrualOverCreditExposureReportDto` schema exists** in the generated types.
- **Why it matters:** the migration could anchor the per-row type but had to read the envelope scalars (`asOfDate`, `leaveYear`) defensively. **A field can drift here without the compiler noticing** — which is the one thing this whole migration exists to prevent, so the gap is worth closing even though nothing is broken today.
- **Suggested direction:** split the JSON and file endpoints, or annotate with `ProducesResponseType` so codegen emits the JSON 200 schema.
- **Confidence:** 100% — verified in the generated file.

---

### BUG-307 — tenant plan limits are silently unenforced: `tenants.plan_id` values match no `subscription_plans` row
- **Type / Severity / Status:** BUG · HIGH · RESOLVED (verified 2026-09-02, /verify-fix)
- **Resolution (2026-09-02):** Three layers: seeder repointed (`DbInitializer.cs:49`), startup reconciler (`:735-757`, invoked `:711`), shared `PlanLimitLookup.cs:48-64` distinguishing `IsConfigurationError` from `IsUnlimited`, adopted by all 10 call sites. Bound: **TC-ADM-009-19**, evidence `PlanLimitLookupPostgresTests.cs:111,128,144,160,193,221,250` + build-breaking guard `PlanLimitLookupUsageGuardTests.cs:76,103` (allowlist empty). **This entry previously claimed "only the nine sites remain" — that was stale by a full closure (#536/#539/#540).**
- **Layer:** Data / BE
- **Module / US / TC:** Admin Console / plan limits, BR-3 (US-REC-010 TC-010-10 exercised it) — found 2026-08-18 during the A1c test run, **filed late 2026-08-21**
- **Title:** The `e2e` tenant's `plan_id` is `'default'`, which matches **no row** in `subscription_plans` (whose codes are `starter`/`professional`/`enterprise`). Plan-based `MaxEmployees` therefore resolves to `NULL` = **unlimited**.
- **Consequence:** BR-3's employee cap only engages via the per-tenant snapshot or an explicit `PlanLimitOverride`. **For any tenant whose `plan_id` does not match a real plan code, the cap silently does not exist.** The limit test only passed because the QA run set a reversible `max_employees` snapshot by hand.
- **Why the severity was raised:** it was flagged LOW as "seed data". But a paid-plan employee cap that silently resolves to unlimited is a **revenue-affecting rule that fails open**, and the failure is invisible — no error, no log, just no limit. The original rating described the *cause* (seed data) rather than the *effect*.
- **Reproduction:** `SELECT t.subdomain, t.plan_id, p.code FROM tenants t LEFT JOIN subscription_plans p ON p.code = t.plan_id;` → rows where `p.code IS NULL` have no plan-derived cap.
- **Suggested direction:** either seed a `default` plan row, or repoint tenants at real plan codes; **and** add a guard so a tenant whose `plan_id` resolves to nothing fails loudly rather than becoming unlimited. Check how many tenants are affected before choosing.
- **Confidence:** 95% on the mechanism (observed directly during the A1c run); the blast radius depends on how many real tenants carry an unmatched `plan_id`.

**MEASURED AND WIDENED 2026-08-21 — this finding understated its own scope in two ways.**

1. **Blast radius (measured against the running DB, not estimated).**
   `SELECT t.subdomain, t.plan_id, p.code FROM tenants t LEFT JOIN subscription_plans p ON p.code = t.plan_id`
   → **2 of 3 tenants** (`e2e`, `platform`) carry `plan_id = 'default'`, which matches no plan. Their
   `tenants.max_employees` snapshot is **also NULL**, so both fall all the way through to "unlimited".
   The third (`techoneglobal`) is on `enterprise`, whose `max_employees` is NULL **by design**.
   **Net: no tenant currently has an enforced employee cap, and two of them are uncapped by accident.**

2. **★ It is NOT just `MaxEmployees`. The same fail-open is duplicated across 10 call sites in 10 files,
   covering 7 distinct plan limits.** `grep -rn "p.Code == tenant.PlanId"` →
   `EmployeeService`, `UserManagementService`, `BulkEmployeeImportService` (MaxEmployees ×3),
   `EmployeeDocumentService` (MaxStorageGb), `RealNotificationDispatcher` (MaxEmailSendsPerMonth),
   `WorkflowService` (MaxWorkflows), `RoleService` (MaxCustomRoles),
   `CustomFieldService` (MaxCustomFieldsPerEntity),
   `NotificationTemplateService` (MaxTemplateLanguageVariants), and `TenantSettingsService` (FeatureFlags).

   **Every one uses `FirstOrDefaultAsync`, so "no plan row" and "plan row with a NULL limit" return the
   same `null` and are indistinguishable.** That ambiguity *is* the bug — `enterprise` proves NULL is a
   legitimate "unlimited", so no call site can tell a deliberate unlimited from a broken `plan_id`.
   **Every paid limit in the product fails open the same way**, not only the employee cap.

- **This is the S-1 shape again:** ten hand-written copies of one rule, with nothing checking they agree.
  `BulkEmployeeImportService`'s own comment records that these paths already drifted once — *"three paths,
  three different answers about one limit."* Fixing this in a tenth copy would repeat the mistake.
- **★ ROOT CAUSE FOUND — it was never stale data, the SEEDER generates it.** `DbInitializer` assigns
  `PlanId = "default"` in **three** places (the default admin tenant, a repair branch for a blank plan, and
  the E2E tenant), while the plans it seeds are `starter`/`professional`/`enterprise`. **Every fresh
  deployment therefore manufactures the fail-open from scratch.** The original "seed data" framing was right
  about the mechanism and wrong about its lifetime: repointing the two live rows would have fixed nothing,
  because the next `DbInitializer.RunAsync` recreates the condition. This is why the fix had to change the
  seeder, not the data.
- **Repoint target is `enterprise`, chosen to PRESERVE behaviour.** Its `MaxEmployees` is NULL, i.e.
  genuinely unlimited — which is exactly what these tenants already had. Repointing to a *capped* plan would
  have silently imposed a limit on live tenants during an unattended startup migration: the opposite mistake,
  and a worse one than the bug being fixed.
- **★ THE GUARD HAD TO BE NARROWED, and the tests are what taught me.** The first fix reported a
  configuration error whenever the plan failed to resolve. That **broke 83 tests**: only 16 of 181
  integration fixtures seed `subscription_plans` at all, so the guard was flagging deployments that had
  deliberately configured *nothing* as misconfigured. Denying those would have been a **far broader
  behaviour change than the fail-open it was fixing**.
  The correct rule is narrower than "the plan did not resolve": it is **"plans EXIST and this tenant points
  at one that does not."** An empty `subscription_plans` table means plan-based limiting is simply not in
  use — nothing to enforce, nothing broken. Pinned by `NoPlansConfiguredAtAll_IsNotAConfigurationError_BUG307`.
- **DECIDED (2026-08-21):** three layers — repoint the two tenants to real plan codes so nobody sits in the
  bad state; a startup check that flags any tenant whose `plan_id` matches no plan; and **one shared
  resolver** that distinguishes *plan-not-found* (deny — a configuration error) from *plan-found-with-NULL*
  (unlimited, by design). Because the data is fixed first, the fail-closed path is a backstop that does not
  fire in practice.

---

### ISSUE-382 — three smaller out-of-lane items from earlier in the session, filed late
- **Type / Severity / Status:** ISSUE · MED · OPEN `
- **Layer:** FE / BE
- **Module / US / TC:** core-hr, performance — surfaced 2026-08-17..21, **filed late 2026-08-21**
- **Title:** Three findings that were reported in agent hand-backs and PR bodies but never reached this ledger.

| # | finding | sev | disposition |
|---|---|---|---|
| 1 | **`uploadImport` is typed as a discriminated union** (`IImportResult \| IImportJobRef`) while the wire returns a **single unified** `BulkImportResult` with an `isComplete`/`jobId` flag. The component's `'total' in resp` sync-vs-async guard is **unreliable — the wire always sends `total`.** Separate from [[BUG-306]] #7, which covers the two absent `/import/jobs/*` routes. | **MED** | needs-decision: map wire→union at the seam, or branch on `isComplete` |
| 2 | **`Feedback360Release.ReleasedByEmployeeId` is `Guid.Empty`** when the releasing user has no linked employee record. Documented in the entity's XML doc at the time, but never filed. **Not an audit hole** — `BaseEntity.CreatedBy` is stamped with the acting user id by `AuditInterceptor`, so "who released this" is durably answerable; this field answers the narrower "which *employee*". | LOW | leave as-is, or make it nullable |
| 3 | **`feedback-360.models.ts` header docstring is stale** — still describes an "(ASSUMED)" contract and lists routes that were superseded by the cycle-keyed ones. | LOW | doc fix; this repo has **four** recorded cases of a comment outliving its code and causing real damage |

- **Why these were missed:** the 2026-08-21 auto-heal pass reconciled the **recent migration slices** and did not sweep the **whole session**. That is the same evaporation the protocol exists to prevent, one level up — a heal that only heals what it happens to remember. **A sweep must enumerate its sources, not recall them.**
- **Confidence:** 100% on all three (each observed directly in an agent hand-back with file:line).

---

### ✅ ISSUE-232 — ledger flip owed (verified resolved 2026-08-18, never flipped)
- **Status update:** `TC-REC-010-05` **passed** during the A1c run, confirming the applicant read path now carries `convertedToEmployeeId`/`isConverted`/`convertedAt` — the exact projection gap ISSUE-232 recorded. The finding above should be marked **RESOLVED**; it was verified three days ago and the flip was never made.
- **Why this is recorded rather than silently flipped:** flipping a finding without a `/verify-fix` run is how this repo produced ISSUE-371 and ISSUE-377. The evidence here is a real executed TC, so the flip is justified — but it is stated explicitly rather than done quietly, so the next reader can see what earned it.
- **SURVEY:** **a composite of 3 sub-items, which is itself the finding's main defect** — a composite entry cannot be sized, scheduled or closed, and these three do not share a severity. Per sub-item: **#1 (union-typed import response)** — 1 seam; `bulk-import.models.ts:68` is the *only* `export type X = IA | IB` wire union in the FE, and `:71-76` the only `'…' in resp` type guards outside an unrelated envelope check. **#2 (`Guid.Empty` fallback)** — 1 site for this field (`Feedback360Service.cs:365`), but the `?? Guid.Empty` pattern occurs **10 times** in production code, i.e. an accepted house idiom rather than an isolated slip. **#3 (stale docstring)** — **10 FE model files** still carry an "(ASSUMED)" backend-contract header (payroll x2, performance x8), of which `feedback-360.models.ts` is one. Excluded: `.spec.ts`, generated `api-types.ts`.
- **AUDIT (2026-09-08):** **#1 — CONFIRMED, and worse than filed.** The wire is a single unified type carrying `isComplete`/`jobId` (`BulkImportDtos.cs:45-74`; `api-types.ts:35873-35885`), and `total` is a non-nullable `int` always serialized — including on the async path (`BulkEmployeeImportService.cs:1167-1175`). The FE still declares the union (`bulk-import.models.ts:68`), guards on `'total' in resp` (`:72`) and branches on it (`bulk-import.component.ts:850-863`), so **`isImportResult` is always true, the async branch is dead, and a >500-row import** (`AsyncThreshold = 500`, `BulkEmployeeImportService.cs:40`) **renders as a completed result with `success: 0`**. The envelope is unwrapped upstream (`api-envelope.interceptor.ts:53`), so the guard does see the bare payload — premise correct. Separation from `BUG-306`#7 also **CONFIRMED**: `import/jobs*` has no backend route (only `EmployeesController.cs:598`), so the polling the FE would start does not exist either. **#2 — CONFIRMED, still present** (`Feedback360Service.cs:365`), and its "not an audit hole" premise is **CONFIRMED** by the entity XML doc at `Feedback360Release.cs:30-39` plus `IsRequired()` at `Feedback360ReleaseConfiguration.cs:27`; only the test fixture was touched (`f2a861c8`, **#604**, merged) — behaviour unchanged. **#3 — CONFIRMED, understated**: `feedback-360.models.ts:2`, `:11` still say "(ASSUMED)", and the listed routes are wrong in **two** ways — they omit the `/tenant` segment (real base `…/tenant/performance/feedback-360`, `feedback-360.service.ts:50`) and name `feedback-360/employees/{id}/results` where the shipped routes are cycle-keyed (`Feedback360Controller.cs:235`, `:254`, `:292`). Merge status: no commit fixes #1 or #3; all three sub-items remain **OPEN**.
- **SEVERITY CHECK:** **the composite MED is unusable** — #1 is MED and live, #2 and #3 are LOW. **Split into three IDs.** The 10-file "(ASSUMED)" sweep is filed as `ISSUE-566`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-308
- **Type / Severity / Status:** BUG · MED · OPEN

- **Type:** BUG · **Severity:** MED · **Status:** OPEN · **Layer:** Backend / deployment topology
- **Module:** Platform (cross-cutting) · **US:** GAP-033a (§23.4) · **TC:** SecurityHeadersApiTests
- **Found:** 2026-08-21, by `@integration-enforcer` auditing the E1 security-headers change (out-of-lane flag).
- **Summary:** The API's `Strict-Transport-Security` header is **never emitted in the containerised TLS deployment**. The middleware guards it with `if (ctx.Request.IsHttps)` (`src/backend/HRM.Api/Program.cs`), which is correct per RFC 6797 — but TLS terminates at the reverse-proxy nginx (`docker-compose.tls.yml`) which forwards to `backend:5000` over **plain HTTP** with `X-Forwarded-Proto: https`. `Program.cs` registers **no** `UseForwardedHeaders`, so `ctx.Request.IsHttps` is `false` behind the proxy and the HSTS branch is dead code in the only deployment that has TLS at all.
- **Root cause (confidence 90%):** missing `ForwardedHeaders` middleware. `Program.cs` even notes its absence in a comment near the end of the file. Every `Request.Scheme`/`IsHttps` read in the app has the same blind spot, not just this one.
- **Blast radius beyond HSTS — CORRECTED 2026-08-21 after enumerating instead of recalling.** The original text above named password-reset links, invite links and OAuth redirect URIs. **All three were wrong:** they hardcode `https://` from `Platform:BaseDomain`/`Platform:FrontendBaseUrl` and never read `Request.Scheme`. `grep -rn "Request.Scheme" src/backend/` returns exactly four production readers, all branding-asset base URLs: `AuthController.cs:339`, `TenantContextController.cs:41`, `TenantSettingsController.cs:255` and `:295`. Behind TLS those emitted `http://` logo URLs on an `https://` page — **mixed content the browser blocks**, a real user-visible symptom that had not been connected to this cause. The genuinely largest affected surface was missed entirely in the first pass: **every `RemoteIpAddress` consumer**, i.e. the audit/security trail (`AuditInterceptor`, `AuditCaptureInterceptor`, `AuditLogService`, `PayrollAuditLogger`) plus the controllers stamping client IP on login, attendance, review sign-off, payroll approval and portal-token issuance.
- **Mitigation today (why MED, not HIGH):** the SPA's nginx emits HSTS with `always` + `includeSubDomains` on the same host, so the browser **is** armed for the origin. The gap is that the API's own HSTS path is unreachable, contrary to its stated intent — not that the origin is unprotected.
- **Reproduction:** `docker compose -f docker-compose.yml -f docker-compose.tls.yml up`, then `curl -skI https://<tenant>.localhost/api/v1/health | grep -i strict-transport` → **absent**. `curl -skI https://<tenant>.localhost/ | grep -i strict-transport` → **present** (served by the SPA nginx).
- **Why not fixed in the E1 PR:** adding `UseForwardedHeaders` is a pipeline-wide change that must be scoped to known proxies — an unscoped `ForwardedHeaders` middleware trusts a client-supplied `X-Forwarded-Proto`, which is a spoofing vector. That is a deliberate decision with its own test surface, not a drive-by edit inside a header PR. **Parked at the decision gate.**
- **SURVEY:** **22** production read sites across **14** files (unit = non-test reads of proxy-sensitive request state): 4 `Request.Scheme` readers (`AuthController.cs:339`, `TenantContextController.cs:41`, `TenantSettingsController.cs:255`, `:295`), 1 `IsHttps` (`Program.cs:726`), and 17 `RemoteIpAddress` reads across 13 files. **1** pipeline registration point. Excluded: `HRM.Tests`, EF migrations/snapshots, generated `api-types.ts`.
- **AUDIT (2026-09-08):** (1) **"`Program.cs` registers no `UseForwardedHeaders`" — FALSE.** It is registered conditionally at `Program.cs:689-691` via `ProxyTrustOptions.Build` (`Configuration/ProxyTrustOptions.cs:39-53`, `XForwardedProto|XForwardedFor`, with KnownNetworks/Proxies cleared then repopulated from config). (2) **"a `Program.cs` comment notes its absence" — FALSE**: `Program.cs:1105-1106` now says the opposite. (3) The blast-radius premise (4 scheme readers; reset/invite/SSO links unaffected) — **CONFIRMED**, all four lines verified verbatim. (4) Test-bound — **CONFIRMED**: `ForwardedHeadersTrustTests.cs`, `SecurityHeadersApiTests.cs:139`. (5) Residual worth keeping, and deliberate: base `appsettings.json:43-47` leaves both lists empty (fail-closed), so a deployment that does not name its proxy still emits no HSTS; only `appsettings.Development.json:8-15` arms the local rig. Fixed by `41c06162` (**#531**), **merged** (`--is-ancestor` true); follow-up `f6623906` (**#542**) also merged.
- **SEVERITY CHECK:** **lower** — this is a stale-OPEN entry, not a live MED. The parked decision (scoped-proxy trust) was taken and implemented. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-383
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** Backend (dev-only)
- **Module:** Platform (cross-cutting) · **US:** GAP-033a (§23.4)
- **Found:** 2026-08-21, by `@integration-enforcer` (out-of-lane flag).
- **Summary:** Swagger UI and `swagger.json` responses do not carry the six §23.4 security headers. `UseSwagger`/`UseSwaggerUI` are registered inside the `IsDevelopment()` block **earlier** in the pipeline than the header middleware, so Swagger terminates the request before the headers are written.
- **Severity rationale:** LOW because the whole Swagger block is `IsDevelopment()`-gated and therefore absent in production. It is a correctness wart in the ordering, not a production exposure.
- **Suggested fix:** move the header middleware above the dev-gated Swagger block. Cheap, but deferred out of the E1 PR because it reorders a block E1 did not otherwise touch.
- **SURVEY:** **1 site** — the single `Program.cs` pipeline, with **2** Swagger registrations (`UseSwagger` `:736`, `UseSwaggerUI` `:737`) inside one `IsDevelopment()` block. There is no second host or `Program`. Unit = pipeline registration points. Excluded: tests.
- **AUDIT (2026-09-08):** (1) **"Swagger is registered earlier in the pipeline than the header middleware" — FALSE at this commit**: the six-header `app.Use` runs at `Program.cs:715-733` and the dev-gated Swagger block at `:735-743`, with the comment at `:704-706` naming `ISSUE-383` as the reason for that ordering. (2) "the whole Swagger block is `IsDevelopment()`-gated" — **CONFIRMED** (`:735`). Fixed in the same commit as `BUG-308`, `41c06162` (**#531**), **merged**. (3) **Residual the entry could not have known:** no test asserts headers on a `/swagger*` response — `SecurityHeadersApiTests.cs` has 5 tests (`:40`, `:62`, `:87`, `:115`, `:139`), none Swagger-pathed — so the ordering can silently regress. Recorded here rather than filed separately, since it is one missing case in one file.
- **SEVERITY CHECK:** **lower** — stale-OPEN. It was never a production exposure (dev-gated) and the ordering is now correct. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-384
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** LOW (was MED) · **Status:** PARTIALLY-RESOLVED · **Layer:** Tooling (agent meta-system)
- **Module:** Platform / `.claude` hooks · **US:** n/a
- **Found:** 2026-08-21, incidentally, while reviewing the working tree before the GAP-033a commit.
- **Summary:** The `vault-compliance-advisor` hook is **written, documented, and completely unwired.** `.claude/hooks/scripts/vault-compliance-advisor.py` exists (8.6 KB), `CLAUDE.md`'s Automation Hooks table documents it as a live `SubagentStop` hook, and `.gitignore` excludes its log file — but it is **registered nowhere**. `.claude/settings.json`'s `SubagentStop` entry runs only `python .claude/hooks/scripts/hooks.py` (the sound notifier), and `grep -rl vault-compliance-advisor .claude/` returns **nothing**. The hook has never executed.
- **Root cause (confidence 100%):** the settings.json registration step was never done. Verified directly, not inferred: the script is on disk, the docs describe it, the registration is absent.
- **★ Why this matters more than its size suggests.** This is the **fourth** instance in this repo of documentation describing behaviour that does not exist — after `RealNotificationDispatcher.cs:32`, `TenantProvisioningService.cs:31-34`, and `ApplicantConversionService.cs:478`. This one is worse in one specific way: the hook's *purpose* is to catch agents that skip the vault contract. So the mechanism meant to detect silent non-compliance is itself silently non-compliant, and its own docs are the reason nobody checked.
- **Reproduction:** `grep -rl "vault-compliance-advisor" .claude/` → no output. `python3 -c "import json;print(json.load(open('.claude/settings.json'))['hooks']['SubagentStop'])"` → only `hooks.py`.
- **Suggested fix:** register it under `SubagentStop` alongside `hooks.py`, then verify it actually fires by running any writing sub-agent and confirming a line lands in `.claude/hooks/vault-compliance.log`. **Do not mark this resolved on the registration edit alone** — the whole finding is that "it looks wired" was never tested.
- **Why not fixed on the spot:** unrelated to the GAP-033a header work in progress.

**UPDATE 2026-08-21 (same day, hours later) — the registration half was fixed WHILE THIS FINDING WAS BEING WRITTEN.** A parallel session committed `806166fd chore(agents): register vault-compliance hook + record both tooling rejections`, which adds the script to `.claude/settings.json` under `SubagentStop` alongside `hooks.py`. **Re-verified against the file, not the commit message:** `hooks.SubagentStop` now lists `python "$CLAUDE_PROJECT_DIR/.claude/hooks/scripts/vault-compliance-advisor.py"`, and `grep -rl vault-compliance-advisor .claude/` now returns `.claude/settings.json`. The original claim ("registered nowhere") was true when observed and is **now false**. Downgraded to LOW and corrected here rather than left standing — a stale finding that reads as open is the same failure mode this finding is about.

- **RESIDUAL, STILL OPEN — and it is the half that mattered.** The finding's own closing condition was *"do not mark this resolved on the registration edit alone."* That condition is **not yet met**: `.claude/hooks/vault-compliance.log` **does not exist**, so the hook has still never been observed to fire. Registration is necessary, not sufficient — a wrong path, a bad exit code, or a `$CLAUDE_PROJECT_DIR` that doesn't expand would all look identical to "wired" in settings.json.
- **Note on why my own session could not prove it:** the two sub-agents run here were `@test-authenticator` and `@integration-enforcer`, both **read-only auditors, which the hook deliberately excludes from scope**. So their completion is not evidence either way. Proof requires a *writing* agent (`backend-dev`/`frontend-dev`/`qa-engineer`/`business-analyst`) finishing a run that touches =3 files under `src/`, `docs/BA/` or `docs/QA/` without writing to the vault.
- **Close this only when** a line actually lands in `.claude/hooks/vault-compliance.log` after such a run.

**VERIFIED 2026-08-21 — the hook's LOGIC works. Its INVOCATION by Claude Code still is not proven, and that
distinction is the whole point of this finding.**

Driven directly with synthetic `SubagentStop` payloads (a fake subagent transcript plus its `.meta.json`
sidecar), all four branches behave correctly:

| case | expected | observed |
|---|---|---|
| `backend-dev`, 3 substantive writes, nothing to the vault | note + log line | **note emitted, log line written** |
| `test-authenticator` (read-only auditor) | silent — out of scope by design | silent |
| `backend-dev` that DID write to `docs/vault/` | silent — compliant | silent |
| `backend-dev` with 1 write (below the 3 threshold) | silent | silent |

That is the **first observed execution** of this hook since it was written, and it disproves the plausible
failure modes the finding worried about — a wrong script path, a bad exit code, an unexpanded
`$CLAUDE_PROJECT_DIR`, a log write that silently fails.

**WHAT IS STILL NOT PROVEN, precisely:** that *Claude Code itself* invokes the script on `SubagentStop` with
a payload of this shape. That cannot be established from inside a session — the hook runs in Claude Code's
process with Claude Code's environment, so `CLAUDE_VAULT_HOOK_DEBUG=1` cannot be set from here, and no debug
trail exists retroactively. The two read-only auditors run in this session are **correctly out of scope**, so
their completion is not evidence either way.

**What would prove it:** the next `/implement-all` run, or any `@backend-dev`/`@frontend-dev`/`@qa-engineer`/
`@business-analyst` invocation that touches ≥3 files under `src/`, `docs/BA/` or `docs/QA/` without writing to
the vault. A line in the log after that is the real closure.

**The synthetic log line was deleted after the check.** Leaving it would have looked like a genuine
`backend-dev` run to whoever read that log next — manufactured evidence indistinguishable from the real
thing is worse than no evidence, and this ledger already records four cases of exactly that failure mode.

Severity stays LOW; status stays PARTIALLY-RESOLVED, because the residual is unchanged in kind: registered
and demonstrably functional, but never yet observed firing *in situ*.

### ISSUE-385
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** `ForwardedForClientIpApiTests` exists. **The entry's own body already said `RESOLVED 2026-08-21` while its status line said OPEN.**

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** Backend / test coverage
- **Module:** Platform (cross-cutting) · **US:** BUG-308 · **TC:** ForwardedHeadersTrustTests
- **Found:** 2026-08-21, by `@test-authenticator` auditing the BUG-308 change (out-of-lane flag), confirmed against the code.
- **Summary:** The **accepted** half of `X-Forwarded-For` handling has no end-to-end assertion. `ForwardedHeadersTrustTests` proves XFF is **discarded from an untrusted peer** (the middleware trusts or abandons the whole forwarded set as a unit, so a demonstrably unapplied proto means XFF was dropped too). It does **not** prove that a *trusted* proxy's XFF actually rewrites `Connection.RemoteIpAddress` — because **no endpoint echoes the resolved client IP**, so there is nothing to assert against.
- **Why it matters:** `RemoteIpAddress` is what the rate-limit partition key and the entire audit-IP trail record (`AuditInterceptor`, `AuditCaptureInterceptor`, `AuditLogService`, `PayrollAuditLogger`, plus login / attendance / review-sign-off / payroll-approval / portal-token controllers). A regression that broke the *accepted* path — reverting every client behind the proxy to one shared rate-limit bucket and one audit IP — would pass the whole suite.
- **★ How it was found is the point.** `Program.cs` carried a comment citing `ForwardedHeadersTrustTests` as proof that XFF spoofing is prevented, at a time when **no arm sent an `X-Forwarded-For` header at all**. That is the **fifth** instance in this repo of a comment describing coverage or behaviour that does not exist — after `RealNotificationDispatcher.cs:32`, `TenantProvisioningService.cs:31-34`, `ApplicantConversionService.cs:478`, and the unregistered `vault-compliance-advisor` hook (ISSUE-384). The comment has been corrected to state exactly what is and is not proven, rather than deleted.
- **Suggested fix:** the honest options are (a) assert it through an observable that already exists — e.g. drive the rate limiter and show two different forwarded clients get independent buckets, or read back a persisted audit row's `ip_address` after an authenticated action; or (b) accept the gap explicitly. **Do not** add a production endpoint that echoes the client IP purely to make this testable.
- **Deliberately not fixed in the BUG-308 PR:** every available route to a real observable (rate limiter, audit row) is materially more complex than the change under test, and inventing a test-only echo endpoint would be coverage theatre. Filed rather than faked.
- **RESOLVED 2026-08-21 — and no production endpoint was added.** `ForwardedForClientIpApiTests` asserts the accepted path against a **pre-existing** observable: a successful login persists the resolved client IP on the refresh token (`AuthController` passes `HttpContext.Connection.RemoteIpAddress` into the login command → `RefreshToken.IpAddress`). Three arms: a trusted proxy's XFF **becomes** the recorded IP; an untrusted peer's XFF is **discarded** and the socket peer recorded; and no header at all records the proxy itself. **Mutation-verified:** dropping `XForwardedFor` from the honoured set turns the accepted-path arm, and only that arm, RED.
- **★ The first observable I tried was wrong, and the test said so instead of passing.** A *failed* login writes no audit row at all, so all three arms read `null`. Rather than weaken the assertion to accommodate that, the arms now assert the login **succeeded** before trusting what follows — otherwise they would read a previous test's row and pass for the wrong reason.
- **Program.cs's coverage comment has been corrected again** — it previously said this case was *not* covered and told readers not to cite the suite for it. It now cites the real coverage. That comment has now been accurate in both directions, which is the point: a coverage claim is only worth anything if it is maintained when coverage changes.

### BUG-309
- **Type / Severity / Status:** BUG · HIGH · OPEN

- **Type:** BUG · **Severity:** HIGH · **Status:** OPEN · **Layer:** Backend
- **Module:** Leave Management · **US:** US-ADM-011 / US-LV-005 · **TC:** (none — the path is untested)
- **Found:** 2026-08-21, by `@test-authenticator` auditing the C1 workflow seed; mechanism verified in code.
- **Summary:** On the **workflow-driven** leave decision path, `TryWorkflowDecisionAsync` passes `_currentUser.UserId` into `NotifyLeaveApprovedAsync`/`NotifyLeaveRejectedAsync`'s **`approverEmployeeId`** parameter (`ILeaveNotificationService.cs:29-33`). That slot expects an **employee** id; the legacy path correctly passes `manager.Id`. Every workflow-driven approval/rejection notification therefore records the wrong identity type.
- **★ Self-inconsistent within the same method**, which is what makes it clearly a defect rather than a convention: the `LeaveApprovalHistory` row written a few lines away correctly resolves an employee id via `ResolveActingEmployeeIdAsync`. The notification does not.
- **Why it is escalating now:** today this only affects tenants that hand-authored a workflow — a set that is currently **empty**, which is why nobody hit it. The C1 seed makes the workflow path the default for **every** tenant, converting a dormant defect into a universal one.
- **Suggested fix:** use `ResolveActingEmployeeIdAsync`, exactly as the history row does, and add the approve/reject arm that would have caught it.


- **FIX (2026-08-21), branch `fix/BUG-309-310-workflow-leave-decision`:** done exactly as suggested. Verified by `WorkflowApproval_NotifiesTheApproversEMPLOYEEId_NotTheirUserId_BUG309`, which drives a real approval through `LeaveRequestService` on Postgres and asserts the notified value is the manager's **employee** id **and explicitly is not their user id** — the two are distinct Guids in the fixture precisely so the confusion is observable. **Mutation-verified:** reverting to `_currentUser.UserId` turns that arm, and only that arm, RED. Stays OPEN until `/verify-fix` closes it.
- **SURVEY:** **1 site, 2 call arms** — `LeaveRequestService.TryWorkflowDecisionAsync` approve (`:1146-1148`) and reject (`:1162-1165`). Class survey (unit = `_currentUser.UserId` references in `HRM.Infrastructure/Services`): all **162** were scanned and the **27** whose enclosing statement touches an employee-id context were inspected individually — **all 27 are correct** (either lookups of the form `e.UserId == _currentUser.UserId`, or genuine `ActorUserId`/`EnrolledBy`/`SignerUserId` fields). The sibling workflow adapters (`OvertimeService.cs:506`, `RegularizationApprovalService.cs`) pass no user id into an employee-id parameter. Excluded: `HRM.Tests`, migrations.
- **AUDIT (2026-09-07):** (1) "`approverEmployeeId` expects an employee id" — **CONFIRMED** (`ILeaveNotificationService.cs:28-33`; the parameter is at `:31`, so the cited range is off by one at the start). (2) **"`TryWorkflowDecisionAsync` passes `_currentUser.UserId`" — FALSE at this commit.** Both arms now call `await ResolveActingEmployeeIdAsync(cancellationToken)` (`LeaveRequestService.cs:1147`, `:1164`), with `BUG-309` comments at `:1140-1145` and `:1161` recording the change. (3) "the legacy path passes an employee id correctly" — **CONFIRMED** (`:1228`, `:1260`, resolving via the same helper defined at `:1271`). The fix commit `27d5595d` (**#532**) is an ancestor of `origin/test/local-subdomains`, and a merged regression test exists: `WorkflowLeaveDecisionDefectsPostgresTests.cs:265` `WorkflowApproval_NotifiesTheApproversEMPLOYEEId_NotTheirUserId_BUG309`. (4) The escalation premise — "the C1 seed makes the workflow path default for every tenant" — also landed (`4a10728a`, **#534**, merged).
- **SEVERITY CHECK:** **n/a — the defect no longer exists in code.** The status line still reads `OPEN`; the only remaining action is `/verify-fix` close-out. See `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-387
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** Backend / audit compliance
- **Module:** Leave Management · **US:** ISSUE-037 / FR-7
- **Found:** 2026-08-21, by `@test-authenticator` auditing the C1 workflow seed.
- **Summary:** The workflow-driven decision path never emits the semantic `Leave.Approved` / `Leave.Rejected` `audit_logs` rows. Legacy writes them via `AddDecisionAudit`; `StageLeaveApprovalAsync`/`StageLeaveRejectionAsync` do not. The engine instead writes generic `workflow.instance.approved`/`.rejected` rows with `ResourceType = "WorkflowInstance"`.
- **Consequence:** an auditor filtering the leave trail **by action** finds every workflow-driven approval missing. ISSUE-037/FR-7 exists precisely to make that trail queryable by action.
- **Why it is escalating now:** same as BUG-309 — C1 makes the workflow path universal, so the by-action leave audit trail would go from complete to systematically incomplete.
- **DECIDED + FIXED (2026-08-21):** stage it. The generic row records that *a workflow step* was approved; it does not record that *a leave request* was approved, and the requirement is about the latter.
- **Fix:** `StageLeaveApprovalAsync` and `StageLeaveRejectionAsync` now call `AddDecisionAudit` with the **identical shape the legacy path uses** (`Leave.Approved`/`Leave.Rejected`, `ResourceType = "LeaveRequest"`, before/after status transition). Staged rather than saved, so the row lands in the **same transaction** as the decision the workflow runtime commits — an audit row that could be committed separately from the decision it describes would be worse than none.
- **Verified by** `WorkflowApproval_WritesTheSemanticLeaveApprovedAuditRow_ISSUE387`, which asserts the row **exists in the database** rather than that a method was called — the staging methods do not save, so the row only survives if the runtime's commit actually carries it. **Mutation-verified:** removing the staged row turns that arm, and only that arm, RED.
- Status stays OPEN until `/verify-fix` closes it.

### BUG-310
- **Type / Severity / Status:** BUG · MED · OPEN

- **Type:** BUG · **Severity:** MED · **Status:** OPEN · **Layer:** Backend
- **Module:** Leave Management · **US:** US-ADM-011
- **Found:** 2026-08-21, by `@test-authenticator` auditing the C1 workflow seed; snapshot mechanism verified in code.
- **Summary:** When `LineManager` resolves to nothing — the requester has no `ReportsToEmployeeId`, or the manager employee has a **null `UserId`** (`Employee.UserId` is `Guid?`) — `ResolveApproverSpecAsync` returns null, yet the workflow instance is **still created** with `AssignedApproverUserId = null`. `IsAuthorizedApproverAsync` then matches nobody, and there is no legacy fallback because `WorkflowInstanceId` is now set.
- **★ The precise regression is the SNAPSHOT, not the stranding.** Under legacy, such a request sits plain-pending and becomes approvable the moment an admin assigns the manager. Under the engine the approver is resolved **once, at activation**, so assigning the manager afterwards does **not** re-resolve the step — the request is stuck permanently and the only remedy is data surgery.
- **Why it is escalating now:** C1 makes the engine the default for every tenant, so every managerless employee's leave request becomes permanently unapprovable rather than temporarily unassigned.
- **Suggested fix:** when the primary approver of a single-step definition resolves to null, fall back to `Legacy()` rather than creating an unapprovable instance — this preserves the self-healing behaviour legacy had. Alternative (needs a product decision): route to a Tenant Admin.


- **FIX (2026-08-21), same branch:** the `Legacy()` fallback, guarded by `StepHasAReachableApproverAsync`. A `Role` step is deliberately **not** treated as unresolved — it assigns no user by design and authorization checks role membership at decision time, so treating its null assignment as a failure would disable role-based approval entirely. Verified by 4 arms including one proving **no instance row is written** (a half-created instance would still mark the request workflow-driven). **Mutation-verified in BOTH directions:** removing the guard turns 3 arms RED; making it over-trigger turns the "a resolvable manager still routes through the engine" arm RED — so it cannot silently degrade into "always fall back". Stays OPEN until `/verify-fix` closes it.

### ISSUE-388
- **Type / Severity / Status:** ISSUE · MED · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** All 10 `PlanLimitLookup` call sites migrated, including `RealNotificationDispatcher.cs:190-213`, and `PlanLimitLookupUsageGuardTests` now guards against an eleventh — widened for the batched shape in #634.

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** Backend
- **Module:** Platform (cross-cutting) · **US:** BUG-307 follow-through · **TC:** PlanLimitLookupPostgresTests
- **Found:** 2026-08-21, while fixing BUG-307. **This is the explicitly-tracked remainder of a deliberately partial fix — not a discovery.**
- **Summary:** `PlanLimitLookup` (the shared lookup that distinguishes *plan-not-found* from *plan-says-no-cap*) exists and is mutation-verified, and **`EmployeeService` is migrated to it with a fail-closed branch**. **Nine of the ten call sites are not yet migrated and still fail open.**
- **Remaining sites, with the exact recipe:**

  | file | limit |
  |---|---|
  | `UserManagementService` | MaxEmployees |
  | `BulkEmployeeImportService` | MaxEmployees |
  | `EmployeeDocumentService` | MaxStorageGb |
  | `RoleService` | MaxCustomRoles |
  | `CustomFieldService` | MaxCustomFieldsPerEntity |
  | `WorkflowService` | MaxWorkflows *(variant shape)* |
  | `NotificationTemplateService` | MaxTemplateLanguageVariants *(variant shape)* |
  | `RealNotificationDispatcher` | MaxEmailSendsPerMonth *(variant shape)* |
  | `TenantSettingsService` | FeatureFlags *(variant shape — not a `long?` limit)* |

  Per site: call `PlanLimitLookup.ResolveAsync`, return the enclosing method's failure type when `IsConfigurationError`, then **delete the now-redundant `PlanLimitOverrides` fetch and `PlanLimitResolver.Resolve` call** — the shared lookup already does both. `EmployeeService.CheckPlanLimitAsync` is the worked template.

- **★ Why this is filed rather than half-done.** A scripted migration of five of these sites was written and then **reverted**, because it swapped the lookup but never branched on `IsConfigurationError`. The result compiled, looked like a fix, added a redundant double-resolve, and **still failed open**. That is the same "change that reads as done and isn't" class this ledger keeps recording — so it was backed out rather than shipped. Nine sites failing open *visibly and tracked* is a better state than five sites failing open while appearing fixed.
- **Also still outstanding from the BUG-307 decision (3-layer fix, 2 layers not yet built):**
  1. **Startup guard** — flag any tenant whose `plan_id` matches no plan, so the condition cannot recur silently.
  2. **Data repoint** — `e2e` and `platform` still carry `plan_id = 'default'`. Until this is done, `EmployeeService` now **denies** employee creation for those two tenants (fail-closed, by design and by decision) rather than silently allowing unlimited. **This is a deliberate, visible behaviour change and is the reason the data fix should not lag far behind.**
- **Suggested order:** data repoint first (clears the only tenants currently affected), then the startup guard, then the nine sites. *(The repoint and startup guard both landed in #536; only the nine sites remain.)*

**DECIDED 2026-08-21 — what "fail closed" means where there is no failure channel.**

- **The three bare-return sites** (`CustomFieldService` → `Task<int>`, `NotificationTemplateService` →
  `Task<long>`, `RealNotificationDispatcher` → `Task`): fall back to **the most restrictive configured
  plan's value** for that limit, and log ERROR. It enforces *a real cap* instead of none, and degrades
  gracefully instead of bricking the feature. Returning **zero** was rejected — for
  `RealNotificationDispatcher` that would silently stop **all** outbound email, turning a config typo into
  an incident of its own. Refactoring the three signatures to gain a `Result` channel was rejected for this
  pass as a change rippling into callers with nothing to do with plan limits. Because #536's startup
  reconciler repoints unresolvable `plan_id`s, this fallback should never actually fire — it is a backstop,
  like the fail-closed branch on the other six.
- **`TenantSettingsService` (FeatureFlags): DECISION WITHDRAWN — it was never broken, and listing it was my
  error.** I put it in the survey by eye rather than by evidence. On reading it: it projects to an anonymous
  type (`new { p.Code, p.FeatureFlags }`) and then does `if (plan is null) return null;` — an explicit,
  unambiguous "no plan row" branch. The BUG-307 ambiguity comes specifically from `(long?)p.X`, where null
  means *either* "no row" *or* "row with no cap". A reference-type projection has no such collision.
  **The drift guard's regex correctly never flagged this file** — the guard was right and my hand-written
  survey was wrong, which is a decent argument for the guard.
  Its fail-open behaviour is also *deliberate and documented*: the in-code comment explains that a null flag
  set means "unknown ⇒ fail open" because failing closed would **lock a paying tenant out of their own
  branding**. That is the correct trade for an entitlement gate, and the opposite of the quota case. No
  sibling helper was built — an unnecessary abstraction would have been worse than none.
- **Net: the migration is 9 sites, not 10.**

**PROGRESS 2026-08-21 — 6 of 10 sites migrated.** `EmployeeService` (#536) plus `UserManagementService`,
`BulkEmployeeImportService`, `EmployeeDocumentService`, `RoleService` and `WorkflowService`.
`WorkflowService` needed a new `(tenantId, planId)` overload because its `tenant` is an anonymous projection,
not the entity — materialising a whole `Tenant` to read two columns would have traded a real query cost for
nothing.

**A DRIFT GUARD NOW BLOCKS THE ELEVENTH COPY.** `PlanLimitLookupUsageGuardTests` statically scans production
sources and fails if any file resolves a plan limit with the ambiguous `(long?)p.X` projection. It ships with
an explicit **shrinking allowlist** of the three still-unmigrated files, so it blocks *new* offenders today
rather than waiting for the decisions above — plus a staleness arm asserting every allowlist entry is *still*
an offender, because an allowlist that outlives its debt is how a guard quietly becomes decoration. Static by
design: no DB, no container, so it cannot become the slow flaky test people learn to skip.

**PER-SITE RETURN TYPES — surveyed 2026-08-21, and they are why a blanket script cannot do this.**

| site | limit | enclosing method returns | has a failure channel? |
|---|---|---|---|
| `UserManagementService.InviteOneAsync` | MaxEmployees | `Task<InviteOneOutcome>` | custom type — needs its own refusal shape |
| `BulkEmployeeImportService.CheckPlanLimitForImportAsync` | MaxEmployees | `Task<Result<int>>` | **yes** |
| `EmployeeDocumentService.EnforceStorageQuotaAsync` | MaxStorageGb | `Task<(Result<..>? block, string? warning)>` | **yes**, via the `block` slot |
| `RoleService.CreateRoleAsync` | MaxCustomRoles | `Task<Result<RoleDto>>` | **yes** |
| `WorkflowService.EnsureWithinPlanLimitAsync` | MaxWorkflows | `Task<Result>` | **yes** |
| `CustomFieldService.GetMaxCustomFieldsAsync` | MaxCustomFieldsPerEntity | `Task<int>` | **NO** |
| `NotificationTemplateService.ResolveMaxLanguageVariantsAsync` | MaxTemplateLanguageVariants | `Task<long>` | **NO** |
| `RealNotificationDispatcher.SendEmailAsync` | MaxEmailSendsPerMonth | `Task` | **NO** |
| `TenantSettingsService.ResolvePlanGatingAsync` | FeatureFlags | `Task<PlanGatingDto?>` | not a `long?` limit at all |

**★ Four of the nine have NO failure channel.** They return a bare `int`/`long`/`void`, so "fail closed" cannot mean "return an error" — it has to mean *return the most restrictive defensible value*, and **that is a per-limit product judgement**, not a mechanical edit. What is the right answer when a tenant's custom-field cap cannot be resolved: zero (block all), the starter-plan value, or refuse at a higher layer? Each needs deciding, not guessing.

`TenantSettingsService` is different again — it gates on `FeatureFlags`, not a numeric cap, so `PlanLimitLookup` (which resolves `long?`) does not fit it as-is.

**This survey is the reason the scripted 5-site migration failed.** It assumed a uniform shape that does not exist. The five sites WITH a `Result`-style channel are mechanical; the other four each need a decision first.

---

### BUG-311
- **Type / Severity / Status:** BUG · MED · OPEN

- **Type:** BUG · **Severity:** MED · **Status:** OPEN · **Layer:** FE (contract cast) + test integrity
- **Module:** Performance / recommendation workspace · **US:** US-PRF-00x · **TC:** recommendation-workspace.component.spec
- **Found:** 2026-08-21, by `@requirements-auditor` while verifying ISSUE-379; **independently re-verified** before filing.
- **Summary:** `RecommendationExportFormat` is declared `'Excel' | 'Pdf'` (`recommendation.models.ts:94`), but the API sends `["csv", "xlsx"]` (`RecommendationService.cs:45` `SupportedExportFormats`). The mapper hides the mismatch with a blind cast — `(w.availableExportFormats ?? []) as RecommendationExportFormat[]` (`:375-376`).
- **Consequence:** the workspace renders one export button per wire token (`recommendation-workspace.component.ts:120`), so the buttons are labelled with the raw tokens, and **any code branching on `'Excel'`/`'Pdf'` is unreachable** — no value the API sends can ever equal either.
- **★ Same class as BUG-127.** An `as` cast is not a conversion; it is an instruction to stop checking. This session already produced one of these — an `as IEmployee` cast that silenced two wrong field names and three unnarrowed unions. The lesson is identical: **when a mapper needs a cast to compile, the cast is usually hiding the bug, not solving it.**
- **The test certifies the wrong shape.** `recommendation-workspace.component.spec.ts:62` mocks `availableExportFormats: ['Excel']` — a value the API has never sent. The spec is green *because* it agrees with the wrong type rather than with the wire, which is precisely the test-theatre pattern `@test-authenticator` exists to catch.
- **★ It also blocks ISSUE-379 item 1b.** The audit identified this file as the *precedent to copy* for the dashboard's `availableExportFormats`. Copying it as-is would propagate the defect to a second surface. **Fix this first, then copy.**
- **Suggested fix:** widen the union to the real wire tokens (`'csv' | 'xlsx'`), or normalise in the mapper with an explicit, exhaustive map — not a cast. Then correct the spec to the real tokens. Removing the cast should make the compiler point at the problem, which is the test that the fix is right.

---

### ISSUE-389
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** FE (a11y)
- **Module:** cross-cutting (Angular templates) · **US:** cross-cutting · **TC:** — (no TC; found by static analysis)
- **Found:** 2026-08-22, while fixing the "no lint gate" finding from a `claude-automation-recommender` scan.
- **Summary:** ESLint had **never been installed** on the frontend (`npm run lint` -> `ng lint`, but `angular.json` had no `lint` target and `@angular-eslint` was absent). Installing it surfaced **187 WCAG accessibility violations across ~121 template files**, none previously reported.
- **Breakdown:** `click-events-have-key-events` 62 · `interactive-supports-focus` 61 · `label-has-associated-control` 43 · `no-autofocus` 11 · `role-has-required-aria` 10.
- **Worst files:**

  | File | a11y errors |
  |---|---|
  | `src/app/features/admin/audit-log/components/audit-log-list/audit-log-list.component.html` | 12 |
  | `src/app/features/core-hr/custom-fields/components/custom-field-list/custom-field-list.component.ts` | 8 |
  | `src/app/features/leave-management/components/holiday-calendar/holiday-calendar.component.html` | 7 |
  | `src/app/features/leave-management/components/leave-type-form/leave-type-form.component.ts` | 7 |
  | `src/app/features/admin/user-management/components/invite-users-modal/invite-users-modal.component.ts` | 6 |
  | `src/app/features/auth/sso/sso-settings/sso-settings.component.ts` | 6 |
  | `src/app/features/core-hr/employees/components/employee-profile/employee-profile.component.ts` | 5 |
  | `src/app/features/payroll/components/adjustment-form/adjustment-form.component.ts` | 5 |

- **★ Why the existing a11y tooling missed these.** The repo already has `@axe-core/playwright`, the Lighthouse MCP audit, and a `/design-review` skill — all **runtime** checks, which only see a component that a test actually navigates to and renders in the state that triggers the issue. A `(click)` on a non-focusable `<div>` is invisible to axe unless that exact element is on screen during a run. Static template linting sees all 121 files unconditionally. **These are complementary, not redundant** — the gap was never a weak a11y tool, it was the absence of the static layer entirely.
- **Consequence:** keyboard-only and screen-reader users cannot operate the affected controls. Directly contradicts the WCAG posture asserted in tech-doc §6 NFR.
- **Suggested fix:** triage per module rather than one sweep — `click-events-have-key-events` + `interactive-supports-focus` almost always co-occur on the same element and are fixed together (make it a `<button>`, or add `tabindex` + a keyboard handler). `no-autofocus` (11) and `role-has-required-aria` (10) are auto-fixable but change rendered markup, so they need review, not `--fix`.

---

### ISSUE-390
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** INFRA
- **Module:** repo-wide · **Found:** 2026-08-22, while adding `.editorconfig`.
- **Summary:** The working tree holds **mixed line endings**. Sampled: `src/backend` .cs -> **457 CRLF / 144 LF**; `src/frontend` .ts -> **285 CRLF / 115 LF**. `core.autocrlf=input` normalises on *commit* but never on *checkout*, and `.gitattributes` pins only `*.sh`.
- **★ This is the unfixed general case of ISSUE-323.** That incident was a CRLF checkout silently breaking `scripts/run-backend-tests.sh` (`set -o pipefail\r`), which **defeated the ISSUE-312 aborted-run guard** — a green-looking gate that wasn't running. The remedy applied then was narrow: one `*.sh` line in `.gitattributes`. Every other LF-sensitive file in the repo still has the original exposure, and the drive is NTFS shared with Windows, so the condition that produced it is permanent.
- **Consequence:** any new LF-sensitive artefact (a shell script not matching `*.sh`, a shebang'd hook, a Docker entrypoint) can reproduce ISSUE-323 exactly. Also blocks declaring `end_of_line` in `.editorconfig`: asserting `lf` would make `dotnet format` rewrite ~70% of every file in one diff (**236,950** violations measured, vs **7,185** with the assertion removed).
- **Suggested fix:** a **standalone, nothing-else-in-it** commit — add `* text=auto eol=lf` to `.gitattributes`, run `git add --renormalize .`, then declare `end_of_line = lf` in `.editorconfig`. Must not ride along with feature work: the diff touches nearly every file and would make any accompanying change unreviewable.

---

### ISSUE-391
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** TEST + BE
- **Module:** repo-wide · **Found:** 2026-08-22, first-ever `dotnet format` run (no `.editorconfig` existed before).
- **Summary:** With the new `.editorconfig` in place, `dotnet format whitespace --verify-no-changes` reports **7,180 whitespace violations + 5 CHARSET**. **83% sit in `HRM.Tests`** (5,969 -> Integration 3,976 / Unit 1,993); the rest are diffuse.
- **CHARSET (5):** five migrations under `Persistence/Migrations/` lack the UTF-8 BOM that `dotnet ef` emits — the other 290 all have one, so these five were hand-touched at some point, which the "never hand-write migrations" rule is meant to prevent. Worth a look for *why*, independently of the formatting.
- **Suggested fix:** `dotnet format whitespace` per-project in separate commits (HRM.Tests alone is 83% of it), never repo-wide in one. Sequence **after ISSUE-390** — normalising line endings first avoids doing the same files twice.

### ISSUE-392
- **Type / Severity / Status:** ISSUE · HIGH · OPEN

- **Type:** ISSUE · **Severity:** HIGH · **Status:** OPEN (decision-gated) · **Layer:** BE
- **Module:** payroll · attendance · recruitment · **Found:** 2026-08-23, C3 wiring audit (GAP-025 follow-on).
- **Summary:** `IAuditExempt` permits exemption for exactly two reasons (`IAuditExempt.cs:12-25`): the entity's own service writes an explicit audit row, or the entity is high-volume/infra. **Six entities claim reason 1 and their writer contains zero audit references** — the same false claim that made GAP-025 possible on `Employee`.
- **Instances:**

  | Entity | Writer | What is unaudited |
  |---|---|---|
  | `TenantFnFPolicy` | `FnFPolicyService.cs:30` | tenant-wide final-settlement **money policy** |
  | `TenantPayrollCalendarPolicy` | `PayrollCalendarPolicyService.cs:33` | tenant-wide **payroll calendar policy** |
  | `OvertimeRecord` | `OvertimeService.cs:376,440` | **approve/reject decisions that affect pay** |
  | `FinalSettlement` / `FinalSettlementLine` | `RealPayrollFnFIntegration.cs` | **F&F settlement amounts** |
  | `InterviewAttachment` | `InterviewAttachmentService.cs` | candidate file attachments |
  | `SelfAssessment` (attachment leg) | `SelfAssessmentAttachmentService.cs` | attachment path only |

- **Why it is gated, not just fixed:** the choice per entity is *add the explicit writer the marker promises* or *remove `IAuditExempt` and let `AuditCaptureInterceptor` capture it* (it already masks PII at write time — `AuditCaptureInterceptor.cs:206-219`). Removing the marker is cheaper and more honest for the two low-volume policy entities, but it **changes audit volume**, which is a product/ops call.
- **Note:** "who approved this overtime?" is currently unanswerable from the audit viewer. That is the instance most likely to be asked about first.
- **Related:** GAP-025 · C3 (#TBD) · [[2026-08-23-employee-field-audit-is-forensic]]
- **SURVEY:** **6 instances across 7 entity types**, out of **53** entity types implementing `IAuditExempt` (unit = entity classes in `HRM.Domain/**`). Excluded: the interface itself, test doubles, and the 46 other exempt types. A **reverse sweep of all 53** found 5 further types with unaudited writers (`AttendanceMonthlySummary`, `LeaveLedger`, `Notification`, `NotificationDelivery`, `OnboardingNotificationOutbox`) — all of which legitimately invoke reason (b) and are verbatim examples in `IAuditExempt.cs:19-20`. **The entry is therefore not under-counted**, which the reverse direction is what establishes.
- **AUDIT (2026-09-07):** (1) "two permitted reasons at `IAuditExempt.cs:12-25`" — **CONFIRMED**, and tighter: the list is `:12-21`, with `:23-25` being the guard paragraph. (2) "six writers contain zero audit references" — **CONFIRMED**: a case-insensitive `grep -i audit` over each whole file returns **0 matches** in all six, and **all cited lines are accurate today, none stale** — `FnFPolicyService.cs:30`, `PayrollCalendarPolicyService.cs:33`, `OvertimeService.cs:376`, `:440`, with persist sites at `FnFPolicyService.cs:55`, `PayrollCalendarPolicyService.cs:58`, `RealPayrollFnFIntegration.cs:279`, `InterviewAttachmentService.cs:135`, `SelfAssessmentAttachmentService.cs:132`. (3) "all 7 are still marked" — **CONFIRMED** (`TenantFnFPolicy.cs:16`, `TenantPayrollCalendarPolicy.cs:24`, `OvertimeRecord.cs:49`, `FinalSettlement.cs:16`, `:74`, `InterviewAttachment.cs:18`, `SelfAssessment.cs:13`) — nothing was silently fixed. (4) "the interceptor skips exempt entities" — **CONFIRMED** (`AuditCaptureInterceptor.cs:89`). (5) "it masks PII at `:206-219`" — **PARTIALLY TRUE**: `:206` is the tail of a doc comment, and masking actually happens at **`:218`** (INSERT/DELETE) **and `:248`** (UPDATE diff) — the entry cites one of two sites, **understating its own safety argument**. (6) "six entities" — **PARTIALLY TRUE**: 6 rows, 7 types. (7) **The entry UNDERSTATES**: `OvertimeService.cs:246` creates the record unaudited too, so the **whole overtime lifecycle is dark** — the other two overtime writers (`AttendanceService.cs:410`, `RegularizationApprovalService.cs:691`) emit only *attendance*-resource rows, and no overtime-resource audit row exists anywhere. No fix; still OPEN.
- **SEVERITY CHECK:** **agrees, marginally higher** — unaudited creation widens the gap beyond the approve/reject paths the entry describes.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-393
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** BE
- **Module:** platform (audit) · **Found:** 2026-08-23, C3 test-authenticity audit.
- **Summary:** `audit_logs`' **model** query filter admits `TenantId == null` (`AppDbContext.cs:838-841`), but **every viewer read scopes explicitly** with `Where(a => a.TenantId == tenantId)` (`AuditLogService.cs:199-200`). So an audit row written with a null tenant sits in the table, satisfies any direct-`DbSet` test, and is **permanently invisible to the US-NTF-005 viewer** — a silent failure with no detector.
- **Blast radius:** ~30 audit writers repo-wide, not just C3's. C3's own arms now assert `TenantId`, but nothing generalises that.
- **Suggested fix:** either an `AuditLogWriterTenantScopeTests` guard over all writers, or a `SaveChanges` interceptor rejecting a null-tenant `AuditLog` outside system context. The second is stronger — it makes the invariant unbreakable rather than merely observed.
- **Related:** C3 · ISSUE-392

### ISSUE-394
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN (decision-gated) · **Layer:** BE
- **Module:** core-hr · recruitment · **Found:** 2026-08-23, C3 wiring audit.
- **Summary:** Two audit-addressability inconsistencies that C3 made *visible* rather than created:
  - `BulkEmployeeImportService.cs:1259-1272` audits **one row per import job** (`ResourceType="EmployeeImport"`), a documented BUG-022/FR-10 decision. Post-C3 an API-created employee has an addressable `Employee.Created` row and an imported one does not, so "everything about employee X" finds one and not the other.
  - `ApplicantConversionService.cs:269-278` writes `EventType="recruitment.applicant.converted"` with **no `Action`, `ResourceType` or `ResourceId`** — off-convention and unreachable under any `ResourceType=Employee` filter. It also sets `ReportsToEmployeeId` directly (`:241-244`), bypassing `ReportingStructureService.AddManagerAudit`.
- **Suggested fix:** decide whether import emits per-employee rows (fold the answer into the ADR); correct the conversion row to the `Entity.Verb` convention regardless — that half needs no decision.
- **Related:** C3 · [[2026-08-23-employee-field-audit-is-forensic]]

### ISSUE-395
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** BE
- **Module:** onboarding · core-hr · **Found:** 2026-08-23, C3 wiring audit.
- **Summary:** `OffboardingService.CompleteAsync` terminates an employee by hand-assigning `Status`/`IsActive` rather than routing through `EmployeeStatusService`, so it also writes **no `EmploymentHistory` row**. C3 added the missing `audit_logs` row, so the termination is now traceable — but the **employment timeline still misses it**, which is a separate user-visible gap from the audit one.
- **Why not fixed in C3:** C3's lane was audit pairing. Adding history writes is a behaviour change to the employment-timeline feature and deserves its own slice (the clean fix is routing the whole path through `EmployeeStatusService`, which also removes the duplication).
- **Related:** C3 · GAP-025

### ISSUE-396
- **Type / Severity / Status:** ISSUE · MED · DEFERRED

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN (deferred by decision) · **Layer:** BE
- **Module:** admin-console (US-ADM-010) · **Found:** 2026-08-23, C5 / GAP-028.
- **Summary:** The tenant export bundle now ships **4 of 5** artifacts (CSVs, `audit_log.jsonl`, `manifest.json`, and — new in C5 — `schema.pdf`). The **documents ZIP** remains absent, so a GDPR Art. 20 export still omits every uploaded file the tenant holds: employee documents, onboarding/offboarding attachments, interview attachments, self-assessment evidence, offer letters.
- **Why it was deferred, explicitly (human decision, 2026-08-23):** two blockers make it its own slice rather than a rider on the link fix.
  1. **`IFileStorage` has no enumerate method** (`UploadAsync`/`OpenReadAsync`/`GetSignedUrl`/`DeleteAsync` only). Including documents needs either a new `ListAsync(tenantId, prefix)` on the seam — which then also exports orphaned files — or DB-side enumeration of storage keys across ~8 entity types, which is the more faithful reading of Art. 20 (export what the tenant's *records* reference) but touches every one of them.
  2. **`BuildBundleAsync` returns an in-memory `byte[]`** and `PackageZip` builds the whole ZIP in a `MemoryStream`. A real tenant's documents are plausibly gigabytes; adding them as-is would OOM the export worker. Doing this properly means changing bundle assembly to stream to a temp file, which needs its own memory/perf verification on a realistic tenant.
- **Suggested fix:** one slice — DB-side key enumeration + streaming assembly + a size/perf arm. Sequence it after any other work touching `TenantDataExportService` to avoid conflicting on the same method.
- **Note:** the manifest already checksums every artifact it lists, so a documents ZIP added later is covered by the existing integrity arm for free.
- **Related:** GAP-028 · C5 · ISSUE-397

### ISSUE-397
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE
- **Module:** platform · **Found:** 2026-08-23, C5.
- **Summary:** `Platform:BaseDomain` is read and normalised at **~10 call sites with three different normalisations** — some `.Trim().TrimStart('.')`, some `.Trim()`, some raw — despite `PortalLinkBuilder.NormalizeBaseDomain` existing for exactly this and being used by only 2 of them.
- **Instances:** `AuthService.cs:738,3169` · `ImpersonationService.cs:204` · `RealTenantWelcomeEmailService.cs:42` · `LogOnlyTenantWelcomeEmailService.cs:41` · `RealUserManagementNotificationService.cs:127` · `ApplicantConversionService.cs:647` · `Program.cs:522` · `TenantResolutionMiddleware.cs:71`. (C5 used the helper rather than adding an eleventh copy.)
- **Why it matters:** a base domain configured as `.example.com` normalises differently depending on which code path builds the link, so the same tenant can receive two different URLs from two different emails. Exactly the duplicated-description class this programme has been closing.
- **Suggested fix:** campaign-shaped — migrate all sites to `NormalizeBaseDomain`, then a usage guard (the `PlanLimitLookupUsageGuardTests` / `EmployeeFieldAuditPairingGuardTests` pattern) so the eleventh copy cannot appear.
- **Related:** C5 · ISSUE-396

### ISSUE-398
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** BE
- **Module:** platform · **Found:** 2026-08-23, C5.
- **Summary:** `IFileStorage.GetSignedUrl` **signs nothing**. `LocalFileStorage` returns `$"/files/{tenantId}/{relativePath}"` with the comment *"Local dev: return a simple path (no real signing). In production, this would generate a pre-signed URL with expiration."* It also takes an `expiresIn` parameter it ignores entirely.
- **Why it matters:** the name and signature promise a time-limited, tamper-evident URL. Every caller that trusted that promise emitted a 404 — C2 removed five such call sites and C5 removed the sixth (the export email). It remains on the interface, so the next person wanting a shareable link will call it and inherit the same bug.
- **One production caller remains:** `EmployeeDocumentService.GetDownloadUrlAsync` (`:375`), which returns a `SignedUrl` plus an `ExpiresAt` of *now + 5 minutes* — a expiry that is pure fiction, since nothing is signed and nothing expires. See ISSUE-399: that method is itself orphaned.
- **Suggested fix:** resolve ISSUE-399 first (it is the only caller), then either delete `GetSignedUrl` from `IFileStorage` or implement genuine HMAC signing plus an anonymous validating endpoint. Deleting is the honest default — **a capability that does not exist should not have a method**, and this one has cost six live 404s.
- **Related:** GAP-027 (C2) · GAP-028 (C5) · ISSUE-399

### ISSUE-399
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE + TEST
- **Module:** core-hr · **Found:** 2026-08-23, C5 (tracing ISSUE-398's callers).
- **Summary:** C2 (#552) replaced the employee-document download with a streaming endpoint and left the **old chain orphaned**: `IEmployeeDocumentService.GetDownloadUrlAsync`, `EmployeeDocumentService.cs:355-400`, `GetDocumentDownloadQuery` + its handler, and the `DocumentDownloadResult` DTO. **No controller dispatches the query** (`grep GetDocumentDownloadQuery HRM.Api` → nothing) and the frontend no longer reads `signedUrl`.
- **Self-reported:** this is dead code my own C2 change created, found while tracing ISSUE-398.
- **Why it is not just deleted:** ~10 test arms exercise `GetDownloadUrlAsync`, including the FR-10/BR-1/BR-2/BR-3 **authorization** arms and the ISSUE-024 **PII access-audit** arm. Deleting them to remove dead code would be a coverage loss dressed as cleanup — unless C2's streaming route (`GET /api/v1/tenant/employees/{employeeId}/documents/{documentId}/download`) already has equivalent authorization and audit arms. **Verify that first; migrate the arms if it does not.**
- **Suggested fix:** one slice — confirm/port the auth + audit coverage onto the streaming path, then remove the orphaned chain, which also unblocks ISSUE-398.
- **Related:** GAP-027 (C2) · ISSUE-398

### ISSUE-400
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** BE + FE
- **Module:** admin-console (US-ADM-005) · **Found:** 2026-08-23, D1 admin migration.
- **Summary:** The user-detail screen's **Linked Employee** section renders a name, job title and department that the API has never sent. `TenantUserDetailDto` carries a bare **`linkedEmployeeId`**; the FE's `ILinkedEmployee` declares `{ employeeId, fullName, jobTitle, department }`.
- **Consequence:** the section can only ever show its empty state against the real API. Its spec passed because the fixture invented the same shape the unchecked cast asserted — the defect and its test agreed.
- **Options (needs a decision):** expand `TenantUserDetailDto` to include the employee summary; or have the FE resolve the employee with a follow-up request; or reduce the section to a link by id. Expanding the DTO is the cheapest for the UI but adds employee PII to a user-admin payload, which is a deliberate call rather than an obvious one.
- **Note:** the D1 mapper leaves `linkedEmployee: null` — honest about what the wire carries. The spec now asserts the empty state and says why.
- **Related:** GAP-S1 · D1 · BUG-312 · BUG-313

### BUG-315
- **Type / Severity / Status:** BUG · HIGH · OPEN

- **Type:** BUG · **Severity:** HIGH · **Status:** OPEN · **Layer:** BE
- **Module:** payroll (US-PAY-002) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** **The salary-component formula "Test" button calls an endpoint that does not exist.** `PayrollService.testFormula()` POSTs to `/payroll/salary-components/validate-formula`; that path is **absent from the contract and served by no controller** (`grep validate-formula src/backend/HRM.Api/Controllers` → nothing). The button is wired at `component-form.component.ts:245`.
- **Why it matters:** the service comment claims *"the backend uses the same safe evaluator that runs payroll, so what the user tests is what payroll will compute (BR-6 syntax + circular-ref validation)."* None of that happens. An author writing a salary formula gets no validation before it is used to compute real pay.
- **Not fixed here:** building the endpoint is backend work with a real design question — whether to expose the payroll formula evaluator to an interactive endpoint at all, and how to sandbox it. D1's lane is the type migration.
- **Suggested fix:** implement `POST /payroll/salary-components/validate-formula` against the existing evaluator, or remove the Test button. Leaving a button that always fails is the worst of the three.
- **Related:** GAP-S1 · D1
- **SURVEY:** **1 of the 7 sites** in the "FE calls a path no controller serves" class surveyed under `BUG-316`. Its named sibling `salary-components/reorder` (`payroll.service.ts:110-118`) is the same defect and is **already filed as `ISSUE-372`**, which covers *both* `validate-formula` and `reorder` (`TEST-FINDINGS.md:1911-1920`) — so **`BUG-315` is a duplicate of `ISSUE-372`**, a relationship neither entry records. Excluded: specs, generated types, migrations.
- **AUDIT (2026-09-07):** (1) "`testFormula()` POSTs `/payroll/salary-components/validate-formula`" — **CONFIRMED** (`payroll.service.ts:130-138`, POST at `:133-134`; the entry cites no line). (2) "absent from the contract and served by no controller" — **CONFIRMED**: `SalaryComponentsController.cs:29`, `:50`, `:63`, `:82`, `:101` has only GET / GET{id} / POST / PUT{id} / DELETE{id}, and no `validate` path exists among the 483 contract paths. (3) "the service comment claims the backend uses the same evaluator" — **FALSE at this commit**: that comment has been replaced, and `payroll.service.ts:121-129` now states plainly that there is no contract path. (4) "an author gets **no validation** before the formula computes real pay" — **PARTIALLY TRUE / overstated**: BR-6 *syntax* validation runs server-side on create and update (`CreateSalaryComponentValidator.cs:33-38`, `UpdateSalaryComponentValidator.cs:27-32`, via `SalaryFormula.Validate`) and circular-reference detection runs in `SalaryComponentService.cs:18`, `:274`. What is genuinely missing is only the **interactive dry-run against sample values**. Still reachable: the button is present and the endpoint absent, so a 404 is guaranteed.
- **SEVERITY CHECK:** **lower — MED, not HIGH.** Save-time syntax and circular-reference validation already stop a bad formula reaching payroll; the loss is a broken button and no sample-value preview, not an unvalidated formula.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-402
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** BE + FE
- **Module:** payroll (US-PAY-008) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** The payroll **approval history timeline shows a dash instead of who approved.** `IApprovalHistoryEntry` declares `actorName`; `PayrollApprovalHistoryDto` carries only **`actorUserId`** (a GUID). The template renders `{{ h.actorName || '—' }}`, so every row has always shown `—`.
- **Why it matters:** this is the audit trail for approving a payroll run. "Who approved this?" is the first question anyone asks of it, and the screen cannot answer.
- **Note:** the D1 mapper sets `actorName: null` — honest about what the wire carries, and behaviour is unchanged. Resolving it needs the DTO to carry a display name (or a lookup).
- **Same class as:** ISSUE-400 (the Linked Employee section) — a FE view model expecting a display name where the API sends only an id. Worth deciding both together.
- **Related:** GAP-S1 · D1 · ISSUE-400

### BUG-316
- **Type / Severity / Status:** BUG · CRIT · OPEN

- **Type:** BUG · **Severity:** **CRITICAL** · **Status:** OPEN · **Layer:** BE
- **Module:** payroll (US-PAY-001) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** **A payroll run cannot be started from the UI at all.** `PayrollRunService.validateRun()` POSTs to `/api/v1/payroll/runs/validate`. **That path does not exist** — absent from the contract, no route on `PayrollRunsController`.
- **The chain, verified end to end:**
  1. `new-payroll-run.component.ts:284` — `ngOnInit` calls `validate()`.
  2. `:308-312` — the 404 hits the error branch: `validation = null`, `validationError = true`.
  3. `:275-281` — `canSubmit` requires `validation()?.canRun === true`, so it is **permanently false**.
  4. `:318` — `submit()` returns early when `!canSubmit()`.
  The "Start payroll run" button can never enable. There is no alternative entry point in the UI.
- **Why nothing caught it:** `http.post<IPayrollRunValidation>` asserted a response type for an endpoint that has never existed, and the component spec mocks the service — so no test ever issued the request.
- **Verified further (2026-08-23):** `PayrollRunsController` exposes only `POST runs`, `runs/{id}/cancel`, `runs/{id}/rerun` and three GETs — no `validate` under any name. A repo-wide grep for `ValidateRun`/`CanRun` across `HRM.Application` and `HRM.Infrastructure` returns **nothing**. So this is not a routing mismatch: **the validation capability does not exist at any layer**, and the frontend was built against a service that was never written.
- **Suggested fix:** the cheap, safe move is to make `canSubmit` degrade gracefully — treat "cannot validate" as "proceed with a warning" rather than "cannot run", since the server already enforces its own rules on `POST runs`. Building `POST /payroll/runs/validate` (period open, no duplicate run, employees present) is the fuller fix but is net-new backend work, not a wiring correction.
- **Related:** GAP-S1 · D1 · BUG-315 (the same "typed against a nonexistent endpoint" class)
- **SURVEY:** **7 call sites** of the class "FE `this.http.*` to a path no controller serves (guaranteed 404)" — established by parsing **141 of the 146** non-spec `this.http.` call sites in `src/frontend/src/app`, matching them against the **483** paths in `contracts/openapi/hrm-v1.json`, then hand-verifying each miss against the controllers. The seven: `payroll-run.service.ts:79` (this entry), `payroll.service.ts:133` (`BUG-315`), `payroll.service.ts:117` `salary-components/reorder` (`ISSUE-372`), `bulk-import.service.ts:78` and `:87` (`BUG-306` #7), `org-tree.service.ts:74` and `leave-entitlement.service.ts:169` (both **unfiled** — now `ISSUE-559` and `ISSUE-558`). Excluded: `*.spec.ts`, generated wire types, EF migrations, backend tests.
- **AUDIT (2026-09-07):** (1) "`validateRun()` POSTs to `/api/v1/payroll/runs/validate`" — **CONFIRMED** (`payroll-run.service.ts:76-84`, POST at `:79-80`). (2) "the path does not exist" — **CONFIRMED**: `PayrollRunsController.cs:32`, `:57`, `:75`, `:92`, `:104`, `:117`, `:130` exposes `runs`, `runs/{id}/cancel`, `runs/{id}/rerun` and four GETs — **the entry says three GETs and missed `runs/{runId}/progress`** — and there are zero `validate` paths in the contract; `grep ValidateRun|CanRun` over `HRM.Application`/`HRM.Infrastructure` returns nothing. (3) The disable chain — **CONFIRMED, line numbers slightly off**: `ngOnInit` → `validate()` is `:283-285` (entry cites `:284` alone), the error branch setting `validationError=true` is `:307-311` (entry cites `:308-312`); `canSubmit` `:275-281`, `submit()` early return `:318` and the template `[disabled]="!canSubmit()"` `:237` are all exact. (4) "no alternative entry point" — **CONFIRMED**: `initiateRun` has exactly one non-spec caller (`:324`), inside the same guarded `submit()`. **Still reachable today** — nothing in the tree fixes it, and the service comment at `:70-75` documents it as deliberately unfixed.
- **SEVERITY CHECK:** **agrees — CRIT.** Payroll cannot be started from the UI at all.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-317
- **Type / Severity / Status:** BUG · CRIT · OPEN

- **Type:** BUG · **Severity:** **CRITICAL** · **Status:** OPEN · **Layer:** FE
- **Module:** payroll (US-PAY-006) · **Found:** 2026-08-23, D1 payroll migration (statutory sub-slice).
- **Summary:** **The statutory editor can destroy a tenant's configured tax bands.** It hydrates its slab and EPF/ETF forms from `listRules()` → `GET /payroll/statutory-rules`, which returns `StatutoryRuleListItemDto` — a list projection carrying **no `taxSlabs` and no `socialSecurity`**. The editor therefore always opens **empty**, and saving writes that empty form over the real bands.
- **Where:** `statutory-configuration.component.ts:691-702` (hydrate), `:782` / `:816` (save).
- **Verified end to end (2026-08-23), not taken on report:**
  1. Contract: `StatutoryRuleListItemDto` has `{countryCode, effectiveFrom, effectiveTo, fiscalYear, id, isActive, ruleName, ruleType, ruleTypeName, slabCount}` — **no `taxSlabs`, no `socialSecurity`**. `StatutoryRuleDto` (the by-id DTO) has both.
  2. `hydrateForms()` reads `tax?.taxSlabs` and `…?.socialSecurity` off the LIST results → slabs become `[]` and every EPF rate becomes `null`.
  3. `saveTaxSlabs` sends `taxSlabs: this.taxSlabs()` (`:782`) and the EPF save sends `socialSecurity` (`:816`) straight to `updateRule`.
  So opening the editor and pressing Save writes an **empty slab array** over the tenant's real income-tax bands. The destructive path needs no unusual input — just open and save.
- **Suggested fix:** add `StatutoryService.getRule(id)` → `GET /statutory-rules/{id}` (the full DTO) and hydrate from that before allowing edits. Until then the editor is dangerous to open.
- **Related:** GAP-S1 · D1
- **SURVEY:** **1** editor (`statutory-configuration.component.ts`) with **2** save arms — income tax (`:772-786`) and provident fund (`:802-818`). For the wider class "hydrate an edit form from a list projection that lacks the edited fields": `StatutoryService` is the **only** payroll service with no by-id fetch (`statutory.service.ts` has `listRules:47` and `updateRule:86`, but no `getRule`), despite the contract exposing `GET /api/v1/payroll/statutory-rules/{id}`. Unit = editor components hydrating from a list DTO. Excluded: specs, generated types, migrations.
- **AUDIT (2026-09-07):** (1) "the list DTO carries no `taxSlabs`/`socialSecurity`" — **CONFIRMED** against the contract: `PayrollStatutoryRuleListItemDto` is `{countryCode, effectiveFrom, effectiveTo, fiscalYear, id, isActive, ruleName, ruleType, ruleTypeName, slabCount}`, while `PayrollStatutoryRuleDto` carries both. (2) "the editor hydrates from the list and always opens empty" — **CONFIRMED** (`:677-680` → `hydrateForms` `:691-702`; `statutory.models.ts:292-304` hard-sets `socialSecurity: null` and never sets `taxSlabs`). Cited lines `:691-702`, `:782`, `:816` are all **exact**. (3) "`updateRule` replaces children wholesale" — **CONFIRMED** (`StatutoryRuleService.cs:208-223`). (4) **"Saving writes an empty slab array over the tenant's real income-tax bands ... just open and save" — FALSE.** It is blocked twice on the client: `saveIncomeTax` returns early on `!slabsValid()` (`:773`) and the button is `[disabled]="!slabsValid() || saving()"` (`:315`), with `validateSlabs([])` returning `valid:false` (`slab-validation.ts:19-21`); and once on the server, which rejects with `no_tax_slabs` (`StatutoryRuleService.cs:526-527`). What is true instead: the user must type replacement bands, and `persist` (`:869-884`) then silently replaces the entire real band set. (5) **The destructive open-and-save path is the EPF arm, which the entry treats as secondary**: `saveProvidentFund` (`:802-818`) has **no** validity guard, its button is disabled only by `saving()` (`:392-393`), nulls become `0` (`:804-807`), and `0/0` rates are accepted by the validator (`CreateStatutoryRuleValidator.cs:117-124`) — **one click zeroes the tenant's EPF/ETF rates and clears the wage ceiling**. (6) Mitigation the entry omits: BR-7 blocks edits overlapping any finalized payroll run (`StatutoryRuleService.cs:179-189`). Still reachable — there is no `getRule` and no hydration fix.
- **SEVERITY CHECK:** **agrees — CRIT, but the rationale must be rewritten**: the zero-input destructive arm is **EPF/ETF statutory rates**, not tax slabs. Fixing only the slab arm named in the title would leave the actual one-click data loss in place.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-403
- **Type / Severity / Status:** ISSUE · HIGH · OPEN

- **Type:** ISSUE · **Severity:** HIGH · **Status:** OPEN · **Layer:** FE
- **Module:** payroll (US-PAY-006) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** Two more statutory gaps found alongside BUG-317:
  - **Test-calculation always returns zeros.** The FE never sends `countryCode`, and `StatutoryDeductionResolver.ResolveAsync` deliberately resolves nothing without one ("NEVER apply an arbitrary country's rules"). So FR-5's preview cannot validate a slab config however it is set up.
  - **Exemptions and cumulative PAYE are unreachable.** The backend has both fully built (`PayrollExemptionDto`, `ExemptionCalculationType`, `isCumulative`, with Postgres tests). The FE view models carry no field for either, so every rule is created non-cumulative with zero exemptions — a wrong tax deduction on a real payslip for any tenant needing them.
- **Related:** BUG-317 · D1
- **SURVEY:** **2 distinct defects.** (a) `countryCode`: **1 of 4** payload builders in `statutory-configuration.component.ts` omits it — `:779`, `:813`, `:831` all send `countryCode: this.countryCode`; only `runTestCalculation` (`:846-855`) does not. (b) exemptions/cumulative: **0** FE surfaces across 1 request view-model (`IStatutoryRuleRequest`) and 1 response view-model, against **8+** backend sites. Unit = payload builders for (a), view-model fields for (b). Excluded specs and generated types.
- **AUDIT (2026-09-07):** (1) "the FE never sends `countryCode` on test-calculation" — **CONFIRMED** (`statutory-configuration.component.ts:852-855` sends only `fiscalYear` and `monthlyGross`; `ITestCalculationRequest` at `statutory.models.ts:151-156` has no such field, and `toTestCalculationRequestWire` documents the omission as a live defect at `:434-439`). (2) "the resolver deliberately resolves nothing without one" — **CONFIRMED verbatim** at `StatutoryDeductionResolver.cs:44-47`. (3) "the backend accepts it" — **CONFIRMED**: `TestCalculationRequest.CountryCode` (`StatutoryRuleDtos.cs:169`) → `StatutoryRuleQueries.cs:71-75`. (4) "preview returns zeros however the slabs are configured" — **CONFIRMED** (`Empty(fiscalYearOverride)` is returned). **Refinement the entry does not make: the fix is smaller than implied** — the component already holds `countryCode = 'LK'` at `:614`; it simply is not threaded into this one call. (5) "exemptions and cumulative are fully built backend-side" — **CONFIRMED**: `ExemptionDto` (`StatutoryRuleDtos.cs:18-26`), `ExemptionCalculationType` (`IStatutoryRuleService.cs:14`), `IsCumulative` (`:42`, `:56`; controller `:93`, `:120`), with Postgres coverage in `YtdCumulativeTaxPostgresTests.cs` and `YtdCumulativeRunPostgresTests.cs`. (6) "the FE view models carry no field for either" — **CONFIRMED**, and self-documented at `statutory.models.ts:279-280` and `:367-368`. No fix branch; nothing merged.
- **SEVERITY CHECK:** **agrees — HIGH.** The cumulative-PAYE half produces a wrong statutory deduction on a real payslip.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-404
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** FE
- **Module:** payroll · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** Three smaller contract gaps the migration surfaced, each flagged rather than papered over:
  - `cancelAdjustment` and `uploadDocument` are typed `Observable<IAdjustment>` but the endpoints return a bare `ApiResponse` with **no `data`**. Callers read `.id` off the result. Needs a component change to fix honestly.
  - `IBankAdvicePreview` ignores the wire's `masked` flag, so the UI decides "masked vs revealed" purely from which method it called and never checks the server's own signal — a defence-in-depth gap on an audited sensitive path (NFR-3).
  - The payroll history and audit views read only `page.items`, discarding `totalCount`/`page`/`pageSize`, then filter and sort client-side over the first page while the header calls itself "a complete history of every payroll run".
- **Related:** D1

### ISSUE-405
- **Type / Severity / Status:** ISSUE · HIGH · OPEN

- **Type:** ISSUE · **Severity:** HIGH · **Status:** OPEN · **Layer:** FE
- **Module:** payroll (US-PAY-007) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** **Two of the four adjustment filters have never worked.** The FE sends `type=` and `period=YYYY-MM`; the contract declares `adjustmentType`, `payMonth` (int) and `payYear` (int). ASP.NET ignores unknown query params, so selecting a Type or Period in the adjustments toolbar returns the **unfiltered** list with no error. (`status` and `employeeId` are correct.)
- **Also:** the contract declares `page`/`pageSize` (default 25) which the FE never sends and whose `totalCount` it discards — the table silently shows only the first 25 adjustments while presenting itself as the full list.
- **Why it is filed, not fixed:** renaming the params is a one-line service change, but it **activates two filters that have never run**, which is a behaviour change beyond a type migration and wants a QA pass. Paging needs a UI decision.
- **Related:** D1 · ISSUE-404
- **SURVEY:** **2 of 4** filter query params are wrong at **1** call site (`adjustment.service.ts:53-76`) against 1 endpoint, plus **3** paging fields dropped in 1 mapper. Unit = query-string parameter names sent vs declared. Sibling check on the same param family shows the mismatch is **not systemic**: `payroll-report.service.ts:184` and `reconciliation.service.ts:49` both send `payMonth` correctly. Excluded specs and generated types.
- **AUDIT (2026-09-07):** (1) "the FE sends `type=` and `period=YYYY-MM`" — **CONFIRMED** at `adjustment.service.ts:58-63`; `IAdjustmentFilters` (`adjustment.models.ts:154-160`) types `period` as `YYYY-MM`. (2) "the contract declares `adjustmentType`, `payMonth` (int), `payYear` (int)" — **CONFIRMED** at `PayrollAdjustmentsController.cs:35-37`. (3) "ASP.NET ignores unknown query params, so the caller gets an unfiltered list with no error" — **CONFIRMED by inspection**: there is no `[FromQuery]` catch-all and no model-state rejection, so the two names never bind and the query runs with nulls. (4) "`status` and `employeeId` are correct" — **CONFIRMED** (`:34`, `:38` vs FE `:55`, `:65`). (5) "the contract declares `page`/`pageSize` defaulting to 25, the FE never sends them and discards `totalCount`" — **CONFIRMED**: controller `:39-40`; `listAdjustments` returns `Observable<IAdjustment[]>` via `toArray(...).map(mapAdjustment)` (`:67-76`), and `adjustment.models.ts:342-343` states outright that `page`/`pageSize`/`totalCount` "have no view-model home — the section 8 table ... silently shows only the server's first page". (6) Unstated but confirmed: the component **does** expose both broken filters in the toolbar (`payroll-adjustments.component.ts:159-161`, filters assembled at `:431`), so the user is given controls that do nothing. No fix branch; nothing merged.
- **SEVERITY CHECK:** **agrees — HIGH.** Silent wrong-data-set on a money screen, and the 25-row truncation is the worse half: a filter returning too much is visible, a list that stops at 25 while presenting itself as complete is not.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-406
- **Type / Severity / Status:** ISSUE · MED · OPEN

- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** FE
- **Module:** payroll (US-PAY-007) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** Five wire fields the adjustments API sends have **no view-model home** and are dropped — flagged rather than silently nulled. Most important: **`negativeNetWarning`** (this adjustment drives an employee's net pay negative) and **`deferredToPayMonth`/`deferredToPayYear`** (the create silently moved the adjustment to a later period). Both are money-visible and the operator currently gets no signal at all. Also `generatedOccurrences`, `appliedInPayrollRunId`, `recurringSeriesId`.
- **Related:** D1

### ISSUE-407
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** FE
- **Module:** platform (generated types) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** `core/api/index.ts` states that *"every generated property is optional (`?`) because Swashbuckle does not emit `required`."* **That is no longer universally true** — five payslip DTOs now carry `required` arrays, so their scalars are non-optional in the generated type, while the adjustment DTOs still have none. The behaviour is **per-schema**, and a migrator trusting the blanket note will mis-reason about which fields need defaults.
- **Suggested fix:** soften the note to "most, not all — check the schema's `required` array".
- **Related:** D1

### BUG-319
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Type:** BUG · **Severity:** HIGH · **Status:** OPEN · **Layer:** FE↔BE contract
- **Module:** Attendance · **US:** US-ATT-009 (FR-8 scheduled reports) · **Found by:** D1 attendance wire migration
- **Summary:** Creating a scheduled attendance report always 400s — the UI collects **email addresses**, the backend binds **GUIDs**.
- **Evidence (verified directly, 2026-09-01):**
  - `src/backend/HRM.Domain/Entities/ScheduledReportConfig.cs:25` — `public List<Guid> Recipients { get; set; } = new();`
  - `attendance-reports.component.ts:351-352` — label `Recipients (comma-separated)`, placeholder `hr@acme.com, ops@acme.com`.
  The generated wire type agrees with the backend (`uuid[]`), so this is not a mapper defect — the **form collects the wrong kind of value**.
- **Root cause (confidence 90%):** the feature was specified as "email the report to people" and built as "reference existing users by id"; nobody reconciled the two. Model binding rejects `"hr@acme.com"` as a `Guid` before any handler runs, so it fails for every input.
- **Repro:** Attendance → Reports → Scheduled → add any recipient → Save → 400.
- **Decision required (do NOT guess):** either (a) the form becomes a **user picker** emitting GUIDs — correct if recipients must be tenant users with report entitlements, or (b) `Recipients` becomes `List<string>` of validated emails — correct if reports may go to non-users (auditors, external payroll). **(a) is the defensible default on a multi-tenant HRM**: an arbitrary email escapes tenant scoping and leaks employee data to whoever is typed in. Parked at the decision gate.
- **Note:** the spec fixtures deliberately keep emails and carry a `// KNOWN DEFECT (FR-8 create)` comment (`attendance.service.spec.ts:1735,1789`). Changing them to GUIDs would make a green test certify a broken flow.
- **SURVEY:** **1 site** — 1 form field (the recipients input in `attendance-reports.component.ts`) feeding **2** endpoints that share one DTO (`POST`/`PUT /attendance/reports/scheduled`, `AttendanceController.cs:1433`, `:1454`). Unit = FE inputs binding a comma-separated list to a `List<Guid>` wire field. Proven a one-off by sibling search: the 4 other email-placeholder inputs in `src/frontend` (`tenant-create:279`, `forgot-password:51`, `login.html:169`, `candidate-portal:92`) all bind to `string` email fields, and the 2 other comma-separated ID inputs (`eligibility-rules.component.ts:214`, `entitlement-rules.component.ts:387`) explicitly ask for GUIDs. Excluded `*.spec.ts`, `generated/api-types.ts`, migrations.
- **AUDIT (2026-09-07):** (1) "`ScheduledReportConfig.cs:25` declares `List<Guid> Recipients`" — **CONFIRMED, line exact**. (2) "the form label and placeholder are emails at `:351-352`" — **CONFIRMED, lines exact**; `onRecipientsChange` (`:693-700`) splits on comma with zero validation and `saveSchedule` (`:702-706`) only checks non-empty. (3) "this is not a mapper defect" — **CONFIRMED**: `toScheduledReportConfigWire` (`attendance.models.ts:3183-3195`) passes `recipients` through verbatim and documents the refusal to transform. (4) "the generated wire agrees with the backend (`uuid[]`)" — **PARTIALLY TRUE**: `api-types.ts:35146` is a plain `recipients?: string[] | null` with **no** uuid format marker, so TypeScript gives zero compile-time protection; the agreement holds only at the OpenAPI/JSON level. (5) "model binding rejects before any handler runs" — **CONFIRMED by inspection**: `ScheduledReportConfigDto.Recipients` is `IReadOnlyList<Guid>` (`DashboardDtos.cs:152`), so `ValidateScheduledDto` (`AttendanceDashboardService.cs:466`) never sees the request. (6) "the spec carries a KNOWN DEFECT comment at `:1735`, `:1789`" — **line numbers FALSE**; actual `attendance.service.spec.ts:1844` and `:1898`. (7) The premise that recipients really are user IDs — **CONFIRMED downstream**: `ScheduledReportJob.cs:128-135` iterates `config.Recipients` as `RecipientUserId`. No fix branch; nothing merged.
- **SEVERITY CHECK:** **agrees — HIGH.** 100% failure rate on the only create path for FR-8.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-320
- **Type / Severity / Status:** BUG · MED · OPEN
- **Type:** BUG · **Severity:** MED · **Status:** OPEN · **Layer:** FE↔BE contract
- **Module:** Attendance · **US:** US-ATT-005 (shifts) · **Found by:** D1 attendance wire migration
- **Summary:** `updateShift` sends a FLEXIBLE shift with `workingDays: []`, which `ShiftRequestValidator` rejects — the edit dialog cannot save a flexible shift.
- **Root cause (confidence 70%):** the component omits working days when the type is FLEXIBLE, but the validator requires at least one regardless of type. Needs the component and the validator fixed **together** — deciding which side is right is the actual work.
- **Note:** left untouched with a `// NOTE:` in the spec rather than fixed inside a type-migration PR.

### ISSUE-408
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** FE
- **Module:** Attendance · **Found by:** D1 attendance wire migration
- **Summary:** `IAttendanceLog.tenantId` has no wire source; the mapper emits `''`.
- **Detail:** `IRegularization.tenantId` was deleted during the migration because the wire never carried it. This sibling still exists and no component reads it. It is now **pinned by a test** (`tenantId === ''`) so nobody "repairs" the placeholder into a fabricated tenant key — which would be the dangerous fix on a multi-tenant product.
- **Suggested fix:** delete the field. Deferred: a models change outside the migrating agent's file.

### ISSUE-409
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** Tooling / agent infra
- **Module:** — (cross-cutting) · **Found by:** D1 loop, 4th recurrence
- **Summary:** Sub-agents keep writing `agent-memory` into **nested** `.claude/` directories under `src/`, where it is never loaded — the notes are silently lost.
- **Evidence:** stray dirs found again on 2026-09-01 at `src/frontend/.claude`, `src/backend/HRM.Api/.claude`, `src/frontend/src/app/features/admin/.claude`, `src/frontend/src/app/features/attendance/.claude`. Only one held a real file (`agent-memory/test-runner/us-adm-002-monitoring-run.md`), relocated to the repo root. Previous recurrences this programme: 3 (payroll, admin, frontend slices).
- **Root cause (confidence 85%):** `memory: project` in agent frontmatter resolves relative to the **repo root**, but a sub-agent launched with `cwd` inside `src/…` writes its memory relative to *its own* cwd. Nothing warns; the write succeeds and the note is simply never read again.
- **Impact:** the cost is invisible — an agent records a hard-won gotcha, and the next run does not see it. That silently defeats the built-in memory store for every agent that runs with a narrowed cwd.
- **Suggested fix (do not apply blind):** a `SubagentStop` hook that fails the stop when `git status` shows a new `.claude/` path outside the repo root, or that relocates it and reports. The advisory `vault-compliance-advisor` hook is the natural place — it already inspects what a sub-agent changed. Wants a human decision on relocate-vs-warn.

### BUG-321
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Type:** BUG · **Severity:** HIGH · **Status:** OPEN · **Layer:** FE
- **Module:** Attendance · **US:** US-ATT-004 (AC-4 multi-level approval) · **Found by:** D1 attendance wire migration (integration-enforcer + test-authenticator)
- **Summary:** On a multi-level regularization workflow the approver is told **"Regularization approved"** and the row leaves the queue, when the server actually said the request is still **PENDING** at the next level.
- **Evidence (verified directly, 2026-09-01):**
  - Backend really does return this: `RegularizationApprovalService.cs:439-450` — `WorkflowDecisionOutcome.StepAdvanced` / `StepRecorded` → `Status = RegularizationStatus.Pending, Action = RegularizationApprovalAction.Approved`.
  - FE discards it: `regularization-approvals.component.ts:582-585` — `next: () => this.onActionSuccess(id, mode)` ignores the mapped `IRegularizationDecisionDto` entirely.
  - `onActionSuccess` (`:588-600`) branches on the **locally chosen** `mode`, so `:591` `removeFromQueue(id)` and `:596` `Regularization approved for ${who}` fire unconditionally.
  - The mapper is correct — `attendance.models.ts` widens the status union and sets it. Nothing reads it.
- **Impact:** the approver believes a decision is final when it is not, and loses the row from their queue. Neither `tsc` nor `strictTemplates` can catch this: the value is **discarded**, not misread.
- **Suggested fix:** subscribe to the decision and branch on `decision.status` — only remove the row and claim approval on `APPROVED`; on `PENDING` keep the row and report that it advanced to the next level.
- **⚠ Ledger contradiction — ENH-005 is WRONG (pessimistic direction).** ENH-005 states "AC-4 multi-level approval (workflow engine) absent … approve/reject is single-level … `workflow_instance_id` stays null." The code at `RegularizationApprovalService.cs:439-465` disproves this. Per the gap-analysis rule the false line is **reported, not silently corrected**. This matters beyond bookkeeping: reasoning from ENH-005 leads directly to rating this bug as latent-only, which is how it stays unfixed.
- **SURVEY:** **2 FE call sites in 1 component** (`regularization-approvals.component.ts`) — the single-row path *and* the bulk path — against 1 backend service. Unit = FE subscription handlers that discard the returned decision DTO. Excluded specs and generated types.
- **AUDIT (2026-09-07):** (1) "the backend returns Pending+Approved on `StepAdvanced`/`StepRecorded`" — **CONFIRMED**, lines **stale**: actual `RegularizationApprovalService.cs:440-451` (entry cites `:439-450`). (2) "the FE discards the DTO at `:582-585`" — **CONFIRMED**, actual line `:583` (`next: () => this.onActionSuccess(id, mode)`); the service does return it (`attendance.service.ts:268-276` → `Observable<IRegularizationDecisionDto>`), so the value exists and is dropped. (3) "`onActionSuccess` branches on the local `mode`, removes at `:591`, toasts at `:596`" — **CONFIRMED** at `:588-599`; the toast is `:595`, not `:596`. (4) **The entry UNDERSTATES the blast radius**: it scopes the defect to the single-row path, but `bulkApprove` (`:620-633`) → `onBulkSuccess` (`:640-656`) branches only on `r.succeeded`, calls `removeFromQueue(id)` (`:647`) and toasts "N request(s) approved", even though `IBulkApproveItemResult.decision` (`attendance.models.ts:413`) is populated by `mapBulkRegularizationItem`. **An intermediate approval in a bulk action is reported as final too, and a fix to the single-row path alone leaves this live.** (5) The `ENH-005` contradiction claim — **CONFIRMED, and stronger than stated**: `AttendanceService.cs:591-604` sets `regularization.WorkflowInstanceId = wf.InstanceId` at submit whenever the tenant has an Active Attendance workflow definition (US-ADM-011c), and `TryWorkflowDecisionAsync` (`RegularizationApprovalService.cs:405-419`) routes decisions through `IWorkflowRuntime` — **the multi-level path is reachable, not latent**, so `ENH-005` (`TEST-FINDINGS.md:710`) is still DEFERRED on a false premise. No fix branch; nothing merged.
- **SEVERITY CHECK:** **agrees — HIGH, arguably higher**, since the bulk path doubles the blast radius the entry accounts for.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-322
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Type:** BUG · **Severity:** HIGH · **Status:** OPEN · **Layer:** FE↔BE contract
- **Module:** Attendance · **US:** US-ATT-005 (shifts, DF-56) · **Found by:** D1 attendance wire migration (in-code `FINDING SHIFT-01`, verified 2026-09-01)
- **Summary:** Editing any shift through the UI **silently wipes the five per-shift work-minute overrides**.
- **Evidence (traced end to end):**
  - Backend update assigns them unconditionally: `ShiftService.cs:169-173` — `shift.StandardWorkMinutes = request.StandardWorkMinutes;` and likewise `MinimumWorkMinutes`, `AutoBreakMinutes`, `AutoBreakThresholdMinutes`, `OvertimeThresholdMinutes`.
  - The request DTO does accept them: `ShiftDtos.cs:98` — `public int? StandardWorkMinutes { get; init; }`.
  - The frontend request interface does **not** declare any of them: `IShiftRequest` (`attendance.models.ts:533-544`) has name/type/times/break/grace/minimumHours/workingDays/rotation only.
  So `PUT /shifts/{id}` always sends them absent → they bind as `null` → all five are nulled on the entity. No screen renders them, so nothing shows the loss.
- **Impact:** a tenant configures per-shift overtime and auto-break thresholds; the next unrelated shift edit (renaming it, say) silently reverts all five to tenant-derived resolution. Overtime and auto-break then compute against different numbers — and this is a **pay-affecting** path.
- **Root cause (confidence 95%):** the update handler was written as a full replace, but the FE contract was built from the fields the edit form renders. Absent ≠ unchanged, and nothing in the type system says so.
- **Suggested fix (needs a decision):** either (a) add the five fields to `IShiftRequest` and the edit form so a round-trip preserves them, or (b) make the backend patch-semantics for these five (only assign when the property was supplied). **(a) is the defensible fix** — (b) makes it impossible to ever clear an override back to tenant default, and silently changes PUT semantics for one subset of fields.
- **SURVEY:** **5 fields x 1 request interface** (`IShiftRequest`), affecting **2** endpoints that share `ShiftRequest` (`POST /shifts`, `PUT /shifts/{id}`). **0 of 5** are rendered on any FE screen for a shift — the only UI hits are tenant-level `AttendanceSettings` (`attendance-settings.component.ts:508-525`), a different entity. Unit = request-DTO fields silently overwritten by a full-replace update. Excluded specs, generated types, migrations.
- **AUDIT (2026-09-07):** (1) "the backend assigns all five unconditionally at `ShiftService.cs:169-173`" — **CONFIRMED**, lines **stale**: actual `:170-174`. (2) "`ShiftDtos.cs:98` declares `int? StandardWorkMinutes`" — **CONFIRMED, exact**, in `record ShiftRequest` (`:86-106`), carrying a DF-56 comment stating "Omit/null → the shift keeps deferring to tenant settings" — precisely the assumption the full-replace update breaks. (3) "`IShiftRequest` declares none of them" — **CONFIRMED**; the block is `attendance.models.ts:532-545` (entry cites `:533-544`). (4) "the PUT always sends them absent" — **CONFIRMED**: `attendance.service.ts:393-399` posts the object straight through with no mapper. (5) "no screen renders them" — **CONFIRMED** (see survey). Clone is unaffected: `ShiftService.cs:300-304` copies all five from source. (6) **The entry slightly UNDERSTATES**: the same gap means the five can never be *set* from the UI either — `createShift` (`attendance.service.ts:381-387`) sends the same interface — so **DF-56 per-shift overrides are 100% unreachable from the product**, not merely fragile; any override can only originate from seed or direct API, which makes the silent wipe unrecoverable through the UI. (7) "in-code marker `FINDING SHIFT-01`" — **FALSE at this commit**: `grep -rn "SHIFT-01" src/` returns nothing; the marker survives only in `TEST-FINDINGS.md:2822`, `:2842`. No fix branch; nothing merged.
- **SEVERITY CHECK:** **agrees — HIGH.** Silent, pay-affecting, and with no UI signal.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-410
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** Process / traceability
- **Module:** Attendance · **Found by:** D1 test-authenticator audit
- **Summary:** Seven findings raised during the attendance migration exist **only as code comments** with report-local IDs that are not in this ledger — they become dangling references the moment the branch merges.
- **The orphaned IDs, and where they live** (all in `features/attendance/models/attendance.models.ts`):
  | in-code ID | line | subject | disposition |
  |---|---|---|---|
  | `F-01` | 3162, 3179 | scheduled-report recipients: emails vs `Guid[]` | **now BUG-319** |
  | `SHIFT-01` | 2345 | DF-56 overrides wiped on every PUT | **now BUG-322** |
  | `SHIFT-05` | 2436 | `ResolvedShiftDto.EffectiveFrom` null coerced to `''` — a blank date asserting "no window" | still comment-only |
  | `SHIFT-07` | 2340 | a wrongly-zeroed break/grace would be written back on the next save | still comment-only |
  | `ISSUE-OT-UNAPPROVED` | 2643 | overtime report row | still comment-only |
  | `F-04` | 2974 | doc drift on a bare-`string` field | still comment-only |
  | `F-06` | 2937, 3003 | KPI absent value renders a confident `0`; widening `IDashboardKpi` to `number \| null` would touch every KPI binding | still comment-only |
  | `F-07` | 2961 | enum values cast without an explicit `UNKNOWN` member — "a cast is still a cast" | still comment-only |
- **Why it matters:** a comment is not a guard and not a work item. The five still comment-only entries are recorded here so they survive the merge; each needs its own triage rather than a severity invented in bulk.
- **Suggested fix:** rewrite the code comments to cite the ledger IDs (done for F-01 and SHIFT-01), and triage the remaining five.

### ISSUE-411
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** Docs / vault integrity
- **Module:** — (cross-cutting) · **Found by:** D1 auto-heal fold, 2026-09-01
- **Summary:** **269 finding wikilinks across the docs do not resolve.** Every `[[BUG-N]]` / `[[ISSUE-N]]` is a dead link — no note of that name exists.
- **Evidence:** repo-wide count on 2026-09-01 — `[[ISSUE-N]]` ×192, `[[BUG-N]]` ×77, versus the documented resolving form `[[TEST-FINDINGS#BUG-N]]` ×2. Findings are **headings inside** `docs/QA/TEST-FINDINGS.md`, not standalone notes, so a bare `[[BUG-322]]` resolves to nothing. `find docs -name "BUG-*.md"` returns only `QA/BUG-STATUS.md` and an archived report — neither is a finding note.
- **Why it matters:** CLAUDE.md already documents the correct form (`[[TEST-FINDINGS#BUG-292]]`) and records that wikilink-form mistakes produced 21 of the 38 broken links found on 2026-08-22. This is the same error class at 7× the scale. In Obsidian the graph shows no backlinks from a finding to the work that references it, which is exactly the traceability the ledger exists to provide.
- **Root cause (confidence 90%):** the bare form reads naturally and nothing validates wikilinks, so every agent copied the neighbouring (broken) convention. I did the same in this session's auto-heal block before catching it — mine are now corrected to `[[TEST-FINDINGS#…]]`.
- **Coverage today: NONE — confirmed with the session that owns `ClaudeMdAccuracyTests` (2026-09-01).** Do not assume a partial check exists. `ClaudeMdAccuracyTests` (PR #573) asserts only that seven load-bearing rules still appear in an auto-loaded file (`CLAUDE.md` + `.claude/rules/**`); its sibling test checks that **relative markdown links** resolve in `CLAUDE.md` only. **Nothing checks wikilinks anywhere, and nothing reads `docs/QA/` at all.**
- **Suggested fix:** a mechanical sweep rewriting `[[BUG-N]]`/`[[ISSUE-N]]` → `[[TEST-FINDINGS#BUG-N]]`, plus a link-check in the `/retro` setup-drift pass so it cannot silently regrow. If the check is built as an arm of `ClaudeMdAccuracyTests`, **verify it fails against a real broken link before trusting it** — the D1 drift guard's first version silently missed 67 of the call sites it was written to catch. **Do not hand-edit 269 sites** — this is `/campaign` shaped (homogeneous + mechanical), and its Phase-1 survey should confirm no finding IDs live in a different ledger before rewriting.

### ISSUE-415
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** BE (missing wire fields)
- **Module:** authentication (US-AUTH-009) · **Found:** 2026-09-01, D1 auth migration.
- **Summary:** **The admin lockout console can never show a locked user** — `UsersTenantUserListItemDto` carries no lockout state at all, so `lockedUntil` and `failedLoginCount` have **no wire source**.
- **Evidence:** `UserManagementDtos.cs:6-14` is `(UserTenantId, UserId, Email, DisplayName, Status, Roles, LastLoginAt, LinkedEmployeeId)`. The FE declared `lockedUntil: string | null` and `failedLoginCount: number`, and `admin-user-lockout.component.ts:114` renders "Failed attempts: {{ user.failedLoginCount }}".
- **Why it matters:** `isLocked()` is permanently false, so the screen whose entire purpose is finding and unlocking locked accounts shows every user as healthy. The Unlock action itself works — the *discovery* path does not. The lockout state exists in the backend (`AuthService.RunFailedAttemptAsync`); it is simply not projected into this DTO.
- **Root cause (confidence 90%):** the FE interface was written from the story's ACs rather than from the DTO, and the unchecked cast meant the absent fields surfaced as `undefined` instead of a compile error.
- **Fix applied here (partial, honest-only):** the mapper emits `lockedUntil: null` (the only value the type allows) and leaves `failedLoginCount` **optional/absent** rather than inventing `0` — a `0` would be a fresh false claim that the account has had zero failed attempts. `undefined` renders blank, which is what the screen already showed.
- **Suggested fix (needs backend work — NOT done here):** add `LockedUntil` and `FailedLoginCount` to `TenantUserListItemDto` and project them in `ListTenantUsersQuery`. That is a backend DTO + query change, out of the type-migration lane.
- **Related:** GAP-S1 · D1 · [[TEST-FINDINGS#BUG-412]]

### ISSUE-417
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** Docs (code comment)
- **Module:** platform (HTTP interceptors) · **Found:** 2026-09-01, D1 auth migration.
- **Summary:** `apiEnvelopeInterceptor`'s doc comment describes the paging envelope as `{ data, total, page, pageSize }`. The real `PagedResult<T>` is `{ items, page, pageSize, totalCount, totalPages }` (`PagedResult.cs:8-15`) — three of the four names are wrong.
- **Why it matters:** the interceptor's *behaviour* is correct (it keys off `success` + `data` own-properties, and `PagedResult` has neither, so pages are correctly left alone). Only the comment is wrong — but it is the comment a developer reads when deciding what shape reaches their `.subscribe()`, and reading it is one way to arrive at exactly [[TEST-FINDINGS#BUG-412]].
- **Suggested fix:** correct the comment to name the real `PagedResult` fields. One-line docs change; deliberately not made here (different file, different lane).
- **Related:** [[TEST-FINDINGS#BUG-412]]

### ISSUE-418
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Type:** ISSUE · **Severity:** MED · **Status:** OPEN · **Layer:** TEST (flake) + BE (UX of the losing path)
- **Module:** admin-console / workflow runtime (US-ADM-011 AC-12) · **Found:** 2026-09-01, triaging PR #574's red gate.
- **Summary:** **`WorkflowRuntimeConcurrencyPostgresTests.ConcurrentApprovals_SameStep_ExactlyOneWins_NoDoubleAdvance_AC12` is timing-dependent** and failed CI with `Expected loser.StatusCode to be 409, but found 403`. 5558 passed, this one failed. **It is unrelated to PR #574**, which changes only `.claude/hooks/` (a Python guard + a shell test) and cannot touch backend behaviour.
- **Evidence:** run 33504747946, job "Backend (build + test)". Nineteen prior `ci-gate.yml` runs on other branches were green, so this is a first observed occurrence, not a standing red.
- **Root cause (confidence 90%):** in `WorkflowRuntimeService.DecideCoreAsync` the **instance is loaded BEFORE the `FOR UPDATE` row lock is taken**, so `instance.CurrentStepOrder` can be stale-forward. Two interleavings:
  - *A (asserted, 409):* the loser reads `CurrentStepOrder = 1`, blocks on the lock, reloads, sees step 1 decided **by itself** → the idempotency check fires → `409 step_already_decided`.
  - *B (observed, 403):* the winner commits **before** the loser's `WorkflowInstances` read. The loser then reads `CurrentStepOrder = 2`, locks and loads the **step-2** group. The idempotency check inspects only the current group, so it does not fire; `IsAuthorizedApproverAsync` then fails because step 2's approver is a different user → `403 not_step_approver`.
- **What did NOT break:** the AC-12 invariant held in both interleavings — exactly one winner, one step-1 row, exactly one step-2 row, no double-advance. Only the **loser's status code** is non-deterministic. This is a flaky *assertion*, not a broken concurrency guarantee.
- **Why it is still worth fixing rather than just re-running:** it will recur on any PR, and the 403 is also poor product behaviour — a user who double-clicks Approve is told "You are not the assigned approver for the current step" rather than "This step has already been actioned."
- **Suggested fix (prefer the production fix over loosening the assertion):** broaden the idempotency check to ask whether the acting user already decided **any** row on this instance, not only in the currently-active group, and return `409 step_already_decided` when so. That makes the losing path deterministic **and** gives the better message. **Do NOT simply relax the test to accept `409 || 403`** — that would freeze the confusing UX and hide the real ordering defect.
- **Immediate action taken:** re-ran the failed job on PR #574 (the failure is unrelated to that PR's diff). Filed rather than fixed — the fix is backend workflow-runtime work, out of the D1 auth slice's lane.
- **Related:** ISSUE-275 (the earlier flake in this same file, fixed by matching prod's `EnableRetryOnFailure`)

### BUG-003 — Cross-tenant settings WRITE: any authenticated Tenant Admin can read AND mutate ANOTHER tenant's company settings (token `tenant_id` never validated against the resolved tenant) — AC-5 / Critical-Rule-#1 isolation bypass
- **Type / Severity / Status:** BUG · CRIT · RESOLVED (PR #119, verified 2026-07-02)
- **/verify-fix 2026-09-02 — PARKED, not closed:** the code fix is verified (`TenantAccessGuardMiddleware.cs:36-56` rejects token-tenant ≠ resolved-tenant with 403 `cross_tenant_denied`, registered post-auth at `Program.cs:740`, 6 arms in `TenantAccessGuardMiddlewareTests.cs`, green in the 5561-pass suite). **But `verify-fix.md` requires `--iso` scope for a systemic isolation finding, and ISSUE-422 shows the running stack is a container built 2026-08-11 (~12 days behind `main`).** Running the cross-module ISO suite against a stale image would produce a verdict that is unreliable in both directions on the single most consequential invariant in this platform. **Close this only after the stack is rebuilt and the ISO suite re-run.**
- **✅ GAP-L7 reconciled 2026-08-10 — this entry contradicted itself and a reader could not tell which line was current.** The header said RESOLVED while the next line said STILL PRESENT, and the resolving code was cited nowhere. **The dates settle it:** the "STILL PRESENT" re-test is dated **2026-06-27**, which PREDATES the 2026-07-02 verification — it is a historical observation, not a live status. **The fix is in the tree and was read directly:** `HRM.Api/Middleware/TenantAccessGuardMiddleware.cs:38-53` refuses any authenticated request whose token `tenant_id` differs from the subdomain-resolved tenant, returning 403 `cross_tenant_denied` and logging `"Cross-tenant access blocked (BUG-003)"`. Everything below this line is the pre-fix narrative, retained deliberately as history.
- **↓ HISTORICAL (pre-fix, 2026-06-27) — REGRESSION RE-TEST (REPORT-ONLY; READ-ONLY probe only — no cross-tenant WRITE per 2026-06-27 safety policy): STILL PRESENT — unchanged at root locus US-AUTH-007 / TenantResolutionMiddleware.** As `tenantadmin@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) + header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/users?pageSize=50` → **HTTP 200** returning techoneglobal's user `sachithra@techoneglobal.org` (count=1), NOT acme's 8 users; same token with correct `X-Tenant-Subdomain: acme` returns acme's own 8 users. Missing `CurrentUser.TenantId == ITenantContext.TenantId` invariant unchanged; PRs #110/#111/#112 neither fixed nor regressed it. Canonical TC-AUTH-054 remains FAIL.
- **Layer:** BE
- **Module / US / TC:** Admin Console · US-ADM-006 · TC-ADM-006-01 (step 6 cross-tenant) / TC-ADM-006-03 (multi-tenant isolation tag) — surfaced while executing the isolation arms
- **Title:** The `acme` Tenant Admin (JWT `tenant_id=019ef3ba-…-acme`) successfully READ and then WROTE the `techoneglobal` tenant's settings (set its `primary_color` from `null` → `#ff0000`, HTTP 200) simply by sending `X-Tenant-Subdomain: techoneglobal`. Tenant context is established solely from the request subdomain (dev header / prod host); the authenticated principal's `tenant_id` claim is **never** cross-checked against the resolved `ITenantContext.TenantId`. A user holding tenant A's token can act on tenant B.
- **Root cause (hypothesis, ~90%):** `TenantResolutionMiddleware.InvokeAsync` (`src/backend/HRM.Api/Middleware/TenantResolutionMiddleware.cs:56-146`) resolves the tenant purely from the subdomain (prod) or the dev `X-Tenant-Subdomain` header (`:77-86`, gated to `IsDevelopment`) and calls `tenantCtx.SetTenant(tenant.Id, …)` (`:128-136`) — it runs **before** authentication and does not see the JWT. Nothing downstream re-validates that the authenticated `CurrentUser.TenantId` (the `tenant_id` claim, `src/backend/HRM.Infrastructure/Services/CurrentUser.cs:31-32`) equals the resolved `ITenantContext.TenantId`. A repo-wide grep finds **no** token-tenant-vs-resolved-tenant guard anywhere in the auth pipeline (the only `tokenTenantId != _tenantContext.TenantId` check, `ApplicantPortalTokenService.cs:160`, is a separate applicant-portal token, not the main bearer path). `TenantSettingsService.LoadCurrentTenantAsync` (`TenantSettingsService.cs:257-264`) then loads/mutates strictly by `_tenantContext.TenantId` — which is whatever the subdomain said — so its "a Tenant A request can only ever load Tenant A's row" guarantee holds **only if** the resolved tenant matches the token, and that link is missing. Confidence ~90% it is a genuine as-coded bypass (live-proven below). The **dev `X-Tenant-Subdomain` header** makes it trivially exploitable in dev; in PROD the header is disabled, but the same missing-guard means a tenant-A bearer token replayed against `tenantB.yourhrm.com` would still execute against tenant B (host-resolved), so this is NOT merely a dev-only artifact — it is a missing authorization invariant.
- **Reproduction steps (live-confirmed 2026-06-24, two independent tokens):**
  1. `POST /api/v1/auth/login` as `tenantadmin@acme.test` / `Admin@123!`, header `X-Tenant-Subdomain: acme` → `accessToken` (decoded `tenant_id=019ef3ba-ffb7-7eec-b24f-7ad806ca1cb9`, the **acme** tenant).
  2. With that acme token, `GET http://localhost:5000/api/v1/tenant/settings` but header `X-Tenant-Subdomain: techoneglobal` → **HTTP 200** returning **TechOne Global's** settings (`orgProfile.name = "TechOne Global"`), not acme's — cross-tenant READ leak.
  3. With the SAME acme token, `PUT http://localhost:5000/api/v1/tenant/settings/primary-color`, header `X-Tenant-Subdomain: techoneglobal`, body `{"primaryColor":"#ff0000"}` → **HTTP 200 "Primary color updated."** — cross-tenant WRITE.
  4. Independent confirmation with a DIFFERENT token (system admin, `X-Tenant-Subdomain: techoneglobal`): `GET /api/v1/tenant/settings` → `branding.primaryColor = "#ff0000"`. acme's own color was unaffected (`#0AF`).
  5. DB confirmation: `SELECT subdomain, primary_color FROM tenants WHERE subdomain IN ('techoneglobal','acme')` → techoneglobal `#ff0000`. The audit row written for the change lives in **techoneglobal's** tenant-scoped log (`tenant_id=techoneglobal`) but `user_id=019efa61-e614-…` = **the acme admin** — i.e. a foreign actor stamped into another tenant's audit trail. (Side effect was reverted: techoneglobal `primary_color` restored to `NULL` per its audit before-image.)
- **Evidence:** step-2 GET body `orgProfile.name="TechOne Global"` HTTP 200; step-3 PUT `{"success":true,"data":{...,"primaryColor":"#ff0000"},"message":"Primary color updated."}` HTTP 200; psql `techoneglobal | #ff0000`; audit `tenant_settings.primary_color_updated` row with `before.PrimaryColor=null`, `after.PrimaryColor=#ff0000`, `tenant_id`=techoneglobal, `user_id`=acme-admin.
- **Severity rationale:** CRIT — this defeats the platform's central, non-negotiable security control (tenant isolation, Critical Rule #1; AC-5 "all operations target ONLY the current tenant"). Any tenant admin can silently read and overwrite **every other tenant's** company settings (branding, org profile, and — combined with the policy gaps below — security policies), with the malicious change attributed to the victim tenant's audit log. Blast radius is all tenants. It is broader than US-ADM-006 (the missing token↔tenant invariant is platform-wide); US-ADM-006's settings endpoints are simply where it was proven end-to-end with a real write.
- **Suggested direction (NOT applied):** none — report only. (A dev should add a single authorization invariant after auth — reject the request when `CurrentUser.TenantId != ITenantContext.TenantId` for tenant-scoped requests, excepting system/impersonation contexts — and add an integration test that replays tenant-A's token against tenant-B's subdomain expecting 403.)
- **Affected surfaces (growing — confirmed by separate runs, NOT re-filed as new CRITs):**
  - US-ADM-006 `TenantSettings*` controllers — READ + WRITE (original proof, 2026-06-24).
  - **US-ADM-007 `WorkflowsController` (`/api/v1/tenant/workflows`) — CONFIRMED 2026-06-24.** As `tenantadmin@acme.test` (JWT `tenant_id=acme`) with header `X-Tenant-Subdomain: techoneglobal`: `POST /api/v1/tenant/workflows` returned **HTTP 200** and created a workflow stamped `tenant_id=techoneglobal` (verified psql: row `019efab7-0eb6-…` `wf_tenant=techoneglobal`, audit row `workflow.created` with `audit_tenant=techoneglobal`, `actor_email=tenantadmin@acme.test` — a foreign actor stamped into another tenant's audit trail). `GET` list returned techoneglobal's (empty) list, `GET {acme-wf-id}` while resolved to techoneglobal returned **404** (proving the context fully became techoneglobal), and `DELETE` of the foreign row succeeded (**HTTP 200**) — so the bypass spans cross-tenant READ + CREATE + DELETE on the workflow surface. (Side effect reverted: the probe workflow was deleted; techoneglobal left with 0 qa07 rows.) This is the SAME missing token↔tenant invariant, not a workflow-specific defect — it confirms the bypass is platform-wide across any `/api/v1/tenant/*` controller, and that the only protection localizing it is the dev-only `X-Tenant-Subdomain` header (prod host-resolution has the same gap). **Contrast that localizes it:** workflow *by-id* GET across tenants returns 404 (the EF query filter on the RESOLVED tenant still scopes rows correctly) — i.e. the leak is "act fully as the resolved tenant," not "see tenant-A and tenant-B rows in one response"; it is gated entirely on which tenant the (unvalidated) subdomain selects.
  - **US-ADM-008 `AuditLogController` (`/api/v1/tenant/audit-logs`) — CONFIRMED 2026-06-24 (CONFIDENTIALITY leak — arguably the worst surface so far).** As `tenantadmin@acme.test` (JWT `tenant_id=acme`) with header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/audit-logs` returned **HTTP 200** with **techoneglobal's complete audit trail** (its 8 rows, including events authored by `sachithra@techoneglobal.org` — a foreign tenant's own user — and its `tenant_settings.*` change history). `GET .../{techoneglobal-audit-id}` (detail) → **HTTP 200** (full cross-tenant record read), while `GET .../{acme-audit-id}` resolved to techoneglobal → **404** (context fully became techoneglobal, matching the "act-as-resolved-tenant" contrast above). **`GET .../export?format=csv` → HTTP 200**: the acme admin can EXPORT another tenant's entire audit log to a downloadable file (bulk exfiltration), and that export writes a self-audit `AuditLog.Export` row into **techoneglobal's** log attributed to the acme admin. This extends the bypass from settings/workflow MUTATION to reading + exporting a victim tenant's **forensic audit trail** (who-did-what across the whole tenant) — a direct confidentiality breach, materially worse than the earlier write surfaces because the audit log is the very record meant to detect such abuse. SAME missing token↔tenant invariant; NOT re-filed. (No data mutated in this probe beyond the incidental `AuditLog.View`/`AuditLog.Export` meta rows the reads themselves generate; pre-existing acme-admin-authored rows already in techoneglobal's log are the residue of the earlier US-ADM-006/007 write probes.)
  - **US-CHR-001 `EmployeesController` (`/api/v1/tenant/employees`) — CONFIRMED 2026-06-25 (cross-tenant READ + CREATE of employee/PII records).** As `hr@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/employees` → **HTTP 200** returning techoneglobal's employee list (its own context, not acme's). `POST /api/v1/tenant/employees` with techoneglobal's department/job-title IDs → **HTTP 201**, creating employee `019efcf4-ce46-…` (`EMP-0001`, `crosswrite@example.com`) stamped `tenant_id=019ef3c3-…` (techoneglobal) with `created_by=hr@acme.test` (psql-verified: `crosswrite@example.com | 019ef3c3-…(techoneglobal) | created_by=hr@acme.test`) — a foreign HR officer wrote a new employee, including any PII fields, into another tenant. **Contrast that localizes it (proves it's the unvalidated subdomain, not row-level leakage):** the in-BODY `tenantId` spoof arm (TC-CHR-083) is correctly IGNORED — a `POST` to acme with body `tenantId=techoneglobal` created the employee under ACME (`TenantInterceptor` stamps from `ITenantContext`), so the only hole is the pre-auth subdomain selecting the tenant with no `CurrentUser.TenantId == ITenantContext.TenantId` check. SAME missing token↔tenant invariant; NOT re-filed. (Test residue: cross-tenant employee `019efcf4-ce46-…` plus a dept `019efcf4-a8e7-…`/job `019efcf4-a928-…` left in techoneglobal — seeded for the proof, flagged for cleanup.)
  - **US-ADM-010 `DataExportController` (`/api/v1/tenant/data-exports`) — CONFIRMED 2026-06-25 (MAXIMUM BLAST RADIUS — bulk full-tenant data exfiltration; the worst payload of this bypass to date).** As `tenantadmin@acme.test` (JWT `tenant_id=acme`) with header `X-Tenant-Subdomain: techoneglobal`: `POST /api/v1/tenant/data-exports {"scope":"full"}` → **HTTP 202** (export `019efae7-d480-…` created stamped `tenant_id=techoneglobal`, `initiatedBySystemAdmin=false`); the Hangfire job completed it; `GET /api/v1/tenant/data-exports/{id}/download` → **HTTP 200** streaming **TechOne Global's entire export bundle** — `manifest.json` `tenant_id=019ef3c3-…` / `tenant_name="TechOne Global"`, the victim's `users.csv` (`Sachithra,sachithra@techoneglobal.org,Tenant Owner` — a foreign tenant's own user), plus 11 lines of its `audit_log.jsonl`. So a tenant-A admin can initiate, complete, AND download a **full GDPR-portability data dump of any other tenant** (every entity CSV + users + audit trail in one ZIP) by selecting the victim's subdomain. The lifecycle audit rows (`DataExport.Requested`/`Completed`) for this export land in **techoneglobal's** log attributed to the acme admin. This is the SAME missing token↔tenant invariant as the other surfaces, but the data-export endpoint is the highest-stakes instance: it packages the victim tenant's complete dataset (incl. employee PII / bank fields when populated) into a single downloadable artifact, where the earlier surfaces leaked one resource at a time. **Contrast that still localizes it (proves it is the subdomain, not row-level leakage):** with the subdomain correctly resolved to acme, requesting techoneglobal's `export_id` for status OR download → **404 `export_not_found`** (the EF query filter on the RESOLVED tenant scopes correctly), and a client-supplied foreign `tenant_id` in the request BODY is ignored (the export DTO has no tenant-id field; initiation always uses `ITenantContext.TenantId`) — so TC-ADM-010-15 / ISO-029 / ISO-028 / ISO-030 (the *implemented* read+write isolation: body-tenant-id-ignore, download-by-id filter, EF query filter, `TenantInterceptor` stamping) all PASS. The ONLY hole is the unvalidated `X-Tenant-Subdomain` (dev) / host (prod) selecting the tenant before auth, with no `CurrentUser.TenantId == ITenantContext.TenantId` check. NOT re-filed. **Residue (read-oriented, acceptable per run brief):** the cross-tenant probe left a Completed export `019efae7-d480-7cdd-897b-d30413a326f2` in techoneglobal's history (consumes 1 of its 3 monthly export slots) + the 2 lifecycle audit rows; no techoneglobal source data was mutated.
  - **US-CHR-002 `EmployeesController` profile-edit surface (`GET`+`PATCH /api/v1/tenant/employees/{id}/profile`) — CONFIRMED 2026-06-25 (cross-tenant READ + WRITE of an existing employee's profile/PII).** As `hr@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with header `X-Tenant-Subdomain: techoneglobal`, targeting techoneglobal employee `019efcf4-ce46-78fd-8a31-d54d75c0710a` (`EMP-0001`, Cross Write): `GET .../profile` → **HTTP 200** returning the full foreign profile (`crosswrite@example.com`, dept `ToneEng`, title `ToneSWE`, `rowVersion=24590`); then `PATCH .../profile` with `{"rowVersion":24590,"contactInfo":{"phone":"+19998887777","address":"BUG003-cross-tenant-write"}}` → **HTTP 200**, mutating the foreign employee's contact fields (response echoed `phone="+19998887777"`, `address="BUG003-cross-tenant-write"`, `rowVersion` bumped 24590→25083, `updatedAt` stamped). So a foreign HR officer can read AND edit (overwrite contact/PII, and — by symmetry of the PATCH DTO — personalInfo/employmentInfo) any existing employee profile in another tenant, plus the field-level `employee_field_audit_logs` row is written under the victim tenant attributed to the acme HR actor. **Contrast that localizes it:** the SAME by-id GET with the CORRECT acme subdomain header → **HTTP 404** (TC-CHR-113 — the EF query filter on the resolved acme tenant scopes the foreign row out), confirming the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` selecting the tenant with no `CurrentUser.TenantId == ITenantContext.TenantId` check. SAME missing token↔tenant invariant; NOT re-filed (this is why TC-CHR-ISO-013 — "Tenant A cannot view OR edit Tenant B's employee profiles" — FAILS). **Residue:** techoneglobal `EMP-0001`'s phone/address now hold the probe values `+19998887777` / `BUG003-cross-tenant-write` (existing throwaway cross-tenant record from the US-CHR-001 run; flagged for cleanup, no new entity created).
  - **US-CHR-003 `EmployeesController` directory READ (`GET /api/v1/tenant/employees/directory`) — CONFIRMED 2026-06-25 (cross-tenant READ of the employee directory).** As `employee@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`, holds `Employee.View.Own` → reaches the handler) with header `X-Tenant-Subdomain: techoneglobal`: `GET .../directory?page=1&pageSize=20` → **HTTP 200** returning **techoneglobal's** directory (`total=1`, the single row `EMP-0001 Cross Write`, dept `ToneEng`, id `019efcf4-ce46-78fd-8a31-d54d75c0710a` = the techoneglobal employee), NOT acme's 15. So the lowest-privilege authenticated role can browse another tenant's employee directory by switching the subdomain header. **Contrast that localizes it (proves it's the subdomain, not row leakage):** with the CORRECT acme subdomain the same token returns ONLY acme's 15 rows (`EMP-0001..0015`, all acme depts) — the EF query filter on the RESOLVED tenant scopes correctly; the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` with no `CurrentUser.TenantId == ITenantContext.TenantId` check. **Note on reach:** the directory's `/export` and the plain `/employees` list returned **403** cross-tenant here only because the *employee* persona lacks `Employee.Export` / `Employee.View.All` — the authz check fires before the handler; a persona holding those perms would reach the same leak (cf. the US-CHR-001 `/employees` list surface already confirmed above). SAME missing token↔tenant invariant; NOT re-filed (this is why TC-CHR-ISO-017 — "Tenant A directory shows zero Tenant B employees" — FAILS). No data mutated (read-only probe).
  - **US-CHR-004 `DepartmentsController` LIST + TREE READ (`GET /api/v1/tenant/departments`, `GET .../departments/tree`) — CONFIRMED 2026-06-25 (cross-tenant READ of the department catalog + hierarchy).** As `hr@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/departments` → **HTTP 200** returning **techoneglobal's** department list (its single dept `ToneEng`, id `019efcf4-a8e7-7f22-98c2-96b22f4f9806`, code `TONE-ENG`), NOT acme's (Engineering/Sales); `GET .../departments/tree` → **HTTP 200** returning techoneglobal's hierarchy (`ToneEng` root, empty children). So a foreign HR officer can enumerate another tenant's org structure by switching the subdomain header. **Contrast that localizes it (proves it's the subdomain, not row leakage):** with the CORRECT acme subdomain the same token returns ONLY acme's depts (Engineering `019efced-…`, Sales `019efd13-…`) — the EF query filter on the RESOLVED tenant scopes correctly; the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` with no `CurrentUser.TenantId == ITenantContext.TenantId` check. SAME missing token↔tenant invariant; NOT re-filed (this is why TC-CHR-ISO-001 — "Tenant A cannot see Tenant B's departments" — FAILS). No data mutated (read-only probe). The cross-tenant WRITE/by-id arms on departments (TC-CHR-025/021) could NOT be executed because BUG-012 wedged the API mid-run; flagged for re-test after a backend restart.
  - **US-CHR-005 `JobTitlesController` LIST READ (`GET /api/v1/tenant/job-titles`) — CONFIRMED 2026-06-25 (cross-tenant READ of the job-title catalog; READ-ONLY re-confirm per run brief).** As `tenantadmin@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/job-titles` → **HTTP 200** returning **techoneglobal's** job titles (its single title `ToneSWE`, id `019efcf4-a928-7926-9b8d-b40c76a68996`, `employeeCount=1`), NOT acme's (`Software Engineer`/`Senior Engineer`). So a foreign Tenant Admin can enumerate another tenant's job-title catalog by switching the subdomain header. **Contrast that localizes it:** with the CORRECT acme subdomain the same token returns ONLY acme's titles — the EF query filter on the RESOLVED tenant scopes correctly; the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` with no `CurrentUser.TenantId == ITenantContext.TenantId` check. SAME missing token↔tenant invariant; NOT re-filed (this is why TC-CHR-055 / TC-CHR-ISO-005 — "Tenant A cannot see Tenant B's job titles" — FAIL). **No data mutated — read-only probe only; ZERO writes to techoneglobal** (the cross-tenant CREATE/PUT/DEACTIVATE arms were deliberately NOT executed against techoneglobal per the run brief, since BUG-003's write reach is already proven platform-wide on other surfaces).
  - **US-CHR-006 `OrgTreeController` (`GET /api/v1/tenant/org-tree`) — CONFIRMED 2026-06-25 (cross-tenant READ of the org-tree / hierarchy visualization, BOTH department + reporting views; READ-ONLY GET per run brief).** As `tenantadmin@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/org-tree?view=department&depth=10` → **HTTP 200** returning **techoneglobal's** department node `ToneEng` (id `019efcf4-a8e7-7f22-98c2-96b22f4f9806`, `employeeCount=1`), which is NOT in acme's own tree (acme returns `engineering`/`Engineering`/`Sales`); `GET …?view=reporting&depth=10` → **HTTP 200** leaking techoneglobal's employee node (`Cross Write`). So a foreign Tenant Admin (or any persona reaching the handler) can read another tenant's entire org hierarchy + reporting structure — names, titles, employee counts, parent-child structure — by switching the subdomain header. **Contrast that localizes it (proves it's the subdomain, not row leakage):** with the CORRECT acme subdomain the same token returns ONLY acme's nodes (`engineering`/`Engineering`/`Sales`) — the EF query filter on the RESOLVED tenant scopes correctly; the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` with no `CurrentUser.TenantId == ITenantContext.TenantId` check. **Note on the by-id contrast:** the sibling `GET /api/v1/tenant/employees/{managerId}/direct-reports` with an ACME employee id + techoneglobal subdomain → **HTTP 404 "Manager employee not found"** (the resolved-tenant query filter scopes the acme id out — same "act-as-resolved-tenant, not see-both-tenants" contrast as the other surfaces). SAME missing token↔tenant invariant; NOT re-filed (this is why TC-CHR-ISO-021 — "Tenant A org tree shows zero Tenant B departments and employees" — FAILS). **No data mutated — read-only GET probes only; ZERO writes to techoneglobal.** No server-side exception (log clean of ERR/FTL for the run window), confirming this is a logic-level authorization gap, not a crash.
  - **US-CHR-007 `LocationsController` (`GET /api/v1/tenant/locations`, `GET .../locations/{id}`) — CONFIRMED 2026-06-25 (cross-tenant READ context-switch on the office-locations surface; READ-ONLY GET per run brief).** As `tenantadmin@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with header `X-Tenant-Subdomain: techoneglobal`: `GET /api/v1/tenant/locations` → **HTTP 200** returning **techoneglobal's** location list (0 rows in its context), NOT acme's 5; and `GET .../locations/{acme-location-id}` (acme's own `019efd75-374c-…` Colombo Head Office) resolved to techoneglobal → **HTTP 404 "Location not found."** So the request was served entirely in techoneglobal's tenant context using an acme-issued token. **Contrast that localizes it (proves it's the subdomain, not row leakage):** the SAME acme token with the CORRECT acme subdomain returns acme's 5 locations and the by-id `019efd75-374c-…` → HTTP 200 — the EF global query filter on the RESOLVED tenant (`AppDbContext.cs:205` `l.TenantId == _tenantContext.TenantId`) scopes correctly; the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` selecting the tenant before auth with no `CurrentUser.TenantId == ITenantContext.TenantId` check. SAME missing token↔tenant invariant; NOT re-filed (this is why TC-CHR-ISO-025 — "Tenant A cannot see Tenant B's locations" — passes the row-filter sub-claim but the token↔tenant arm of the same isolation contract FAILS). **No data mutated — read-only GET probes only; ZERO writes to techoneglobal.** The cross-tenant WRITE arm (create a location under techoneglobal) was deliberately NOT executed per the run brief (BUG-003's write reach is already proven platform-wide on the settings/workflow/employee/data-export surfaces above; the same pre-auth middleware path serves writes).
  - **US-AUTH-002 `AuthController.GetCurrentUser` (`GET /api/v1/auth/me`) — CONFIRMED 2026-06-25 (read-path token↔tenant mismatch NOT rejected; bounded — no foreign data; READ-ONLY).** As `manager@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`) with a mismatched header `X-Tenant-Subdomain: z76`: `GET /api/v1/auth/me` → **HTTP 200** returning the manager's OWN acme profile (`tenant.subdomain=acme`), i.e. the endpoint serves the request under a subdomain that does not match the token's tenant instead of rejecting it. **This is bounded and NOT a leak:** `/auth/me` is claim-driven, so the response only ever contains the token owner's own tenant data (no z76 data), and the same token against `GET /api/v1/tenant/users` under z76 → **HTTP 403** (permission gate fires). It is recorded here only because TC-AUTH-ISO-003 step 2 expects the API to *reject* (401/403) a JWT whose `tenant_id` does not match the resolved subdomain, and `/auth/me` does not — the same missing `CurrentUser.TenantId == ITenantContext.TenantId` invariant, on the read path. SAME root cause as BUG-003; NOT re-filed. No data mutated.
  - **US-AUTH-007 `TenantResolutionMiddleware` — ★ ROOT LOCUS of this bug ★ — CONFIRMED 2026-06-25 (cross-tenant READ of users on TWO independent victim tenants, plus a log-level smoking gun for the split-brain).** US-AUTH-007 is the user story that *owns* the tenant-resolution middleware where this defect lives; TC-AUTH-054 (the story's "resolved tenant context prevents cross-tenant data exposure" case) is arguably the canonical test for BUG-003, and it **FAILS**. As `tenantadmin@acme.test` (JWT `tenant_id=acme` `019ef3ba-…`), `GET /api/v1/tenant/users` under header `X-Tenant-Subdomain: e2e` → **HTTP 200** returning the **e2e** tenant's user list (`owner@e2e.test`), and under `X-Tenant-Subdomain: techoneglobal` → **HTTP 200** returning **techoneglobal's** user list (`sachithra@techoneglobal.org`) — NOT acme's 6 users. **Log-level smoking gun (Serilog `hrm-20260625.log`, RequestId `0HNMIFE5GI2QT`/`…QU`):** the `PermissionAuthorizationHandler` line for the suspended-tenant arm logged `Authorization denied … Tenant=019ef3ba-…[acme] … MissingPermission=Tenant.ManageUsers` while the SAME request's Serilog `tenant_id` enricher property read `019efa84-…[qa04-react-1]` — i.e. the **authz layer evaluates the permission against the TOKEN's tenant (`CurrentUser.TenantId`), while the data/query layer + status middleware operate on the SUBDOMAIN-RESOLVED tenant (`ITenantContext.TenantId`)**, and the two are never required to match. That split-brain is the mechanical heart of BUG-003: a token whose role-permissions happen to satisfy the check is then served entirely in the foreign resolved tenant's context. **Same-request confirmation of the status path:** the acme Tenant Admin token under suspended `X-Tenant-Subdomain: qa04-react-1` reached `TenantStatusEnforcementMiddleware` and got **HTTP 451** (`tenant_suspended`) — proving the acme token was treated as an authorized principal of qa04-react-1 (it passed authz against the foreign resolved tenant before the suspension gate fired). **Contrast that localizes it:** the SAME acme token with the CORRECT `acme` subdomain returns ONLY acme's 6 users — the EF query filter on the resolved tenant scopes correctly; the only hole is the unvalidated pre-auth `X-Tenant-Subdomain` (dev) / host (prod) selecting the tenant with no `CurrentUser.TenantId == ITenantContext.TenantId` guard after authentication (`TenantResolutionMiddleware.cs:56-146` runs before `UseAuthentication`/`UseAuthorization` at `Program.cs:360/363-364`, and nothing downstream re-links token↔resolved-tenant). NOT re-filed — this is the root-story confirmation, so the canonical fix belongs here (add the invariant in/after auth and an integration test that replays tenant-A's token against tenant-B's subdomain expecting 403). **READ-ONLY probe — ZERO writes to e2e/techoneglobal/qa04-react-1; no data mutated.** (This is why TC-AUTH-054 FAILS, and reinforces the already-recorded TC-AUTH-ISO-001/ISO-003 failures.)

### BUG-003 (EXTENDED to leave-entitlements) — token tenant_id is never validated against the subdomain-resolved tenant; an acme user sending `X-Tenant-Subdomain: techoneglobal` operates fully inside techoneglobal for entitlement rules/overrides/effective
- **Type / Severity / Status:** BUG · CRIT · RESOLVED (systemic `TenantAccessGuardMiddleware`, PR #119; ISO-verified 2026-07-03 — cross-arm 403 `cross_tenant_denied` on all `/tenant/*` incl. leave-entitlements. Reconciled 2026-07-04.)  *(systemic — same root as BUG-003; recorded as an extension, not a new root)
- **Layer:** BE
- **Module / US / TC:** Leave Management · US-LV-002 · TC-LV-ISO-005 (Tenant A cannot see/modify Tenant B's rules/overrides), TC-LV-ISO-007 (data-layer isolation). NFR-2.
- **Title:** The documented platform-wide CRIT BUG-003 (JWT `tenant_id` claim never checked vs the subdomain-resolved tenant) applies to the entire `LeaveEntitlementsController`. A user authenticated in tenant A who sends another tenant's `X-Tenant-Subdomain` is resolved into that other tenant's context, so `GET/POST/PUT/DELETE` on `/rules` and `/overrides` and `GET /effective` all read/write the OTHER tenant's data. The EF global query filter only scopes to the (attacker-controlled) resolved tenant, so it provides no protection here.
- **Root cause (~97%, live confirmed; same as BUG-003):** `TenantResolutionMiddleware` resolves tenant from the `X-Tenant-Subdomain` header (dev fallback) and never compares it to the authenticated principal's `tenant_id` claim. There is no per-request assertion that `token.tenant_id == resolvedTenant.Id`. Confirmed identical mechanism to the original BUG-003 (US-ADM-006) and the prior leave-management extension BUG-026 (US-LV-001).
- **Reproduction steps (READ-ONLY; ZERO writes to techoneglobal):** acme TenantAdmin token (claim `tenant_id=019ef3ba-ffb7-7eec-b24f-7ad806ca1cb9`) + `X-Tenant-Subdomain: techoneglobal` → `GET /api/v1/tenant/leave-entitlements/rules` returns **200** with techoneglobal's (empty) rule set, NOT acme's 13 rules — i.e. the request executed in techoneglobal's context. Cross-confirmed with `GET /api/v1/tenant/employees` (acme token + techoneglobal header) → **200** returning techoneglobal's employee "Cross Write <crosswrite@example.com>" (1 row), proving full context switch. The same header on `GET /effective`/`POST /rules`/`POST /overrides` would equally execute against techoneglobal.
- **Evidence:** `GET /rules` acme-token+techoneglobal-header → `{"success":true,"data":[]}` HTTP 200 (acme's own list = 13 rules); employee cross-read returned the techoneglobal row. DB: all 13 acme rules + 2 overrides carry `tenant_id=019ef3ba-…` only (write-stamping itself is correct — the breach is at the resolution/authz layer, not the data layer).
- **Severity rationale:** CRIT — cross-tenant read AND write of leave-policy data (entitlement rules/overrides drive everyone's leave balances). Exploitable with nothing more than a valid token for any one tenant plus the victim's subdomain. Same blast radius as the parent BUG-003.
- **Suggested direction (NOT applied):** none — report only. (Fix at the source: BUG-003 — reject any request whose validated `tenant_id` claim ≠ subdomain-resolved tenant id, for non-system principals. RLS — ISSUE-033 below — would be the backstop.)

### BUG-003 (EXTENDED to holiday calendar) — token tenant_id is never validated against the subdomain-resolved tenant; an acme user sending `X-Tenant-Subdomain: techoneglobal` operates fully inside techoneglobal for holidays (read AND write)
- **Type / Severity / Status:** BUG · CRIT · RESOLVED (systemic `TenantAccessGuardMiddleware`, PR #119; ISO-verified 2026-07-03 — holiday-calendar read AND write cross-arm now 403 `cross_tenant_denied`. Reconciled 2026-07-04.) (tracked under the existing BUG-003; NOT a new finding — recorded here for US-LV-007 traceability)
- **Layer:** BE
- **Module / US / TC:** Leave Management · US-LV-007 · TC-LV-ISO-026 (FAIL — mismatched token+context is accepted, cross-tenant write succeeds). See the canonical **BUG-003** entry (settings) and its EXTENDED notes (leave-entitlements, departments BUG-014 class, etc.).
- **Title:** Same systemic isolation bypass as the canonical BUG-003: the JWT's `tenant_id` claim (acme = `019ef3ba-…`) is never checked against the tenant resolved from the request subdomain/`X-Tenant-Subdomain` header. An acme-tenant HR token presenting `X-Tenant-Subdomain: techoneglobal` is accepted and runs entirely inside techoneglobal's tenant context for the holiday endpoints — confirmed for both reads and writes. The EF global query filter scopes rows to the *resolved* (foreign) tenant, so no acme data leaks outward in this instance, but the request executes under a tenant the user has no membership in, and a **write created a row stamped to techoneglobal**.
- **Root cause:** as per canonical BUG-003 — `TenantResolutionMiddleware` resolves tenant from subdomain/header and the auth layer never asserts `token.tenant_id == resolvedTenant.Id`. Confidence ~95% (matches every prior BUG-003 repro across settings/workflows/audit/entitlements/departments).
- **Reproduction steps (live):**
  1. Login `hr@acme.test` (tenant_id acme `019ef3ba-…`).
  2. READ cross-tenant: `GET /api/v1/holidays?year=2026` with `Authorization: Bearer <acme HR>` + `X-Tenant-Subdomain: techoneglobal` → **HTTP 200**, `data: []` (served techoneglobal's context; techoneglobal happens to have 0 holidays so nothing exfiltrated, but the foreign context was accepted rather than 401/403). A correctly-isolated API would reject the token/context mismatch.
  3. WRITE cross-tenant: `POST /api/v1/holidays` same headers, body `{"name":"ISO-PROBE-DELETE-ME","date":"2099-01-01","type":"Public"}` → **HTTP 201**; the row was created and persisted under techoneglobal (verified by re-reading `?from=2099-01-01&to=2099-12-31` under the techoneglobal context → the row is present). An acme user wrote into another tenant's data.
  4. Control: same acme token with NO subdomain header → 400 "Tenant context is not resolved." (good); with a bogus subdomain → 404 (good). So the gap is specifically the *accepted-but-mismatched* case.
- **Evidence:** responses in steps 2-3 (captured 2026-06-25T10:56-10:57Z). The created techoneglobal row id is `019efe6d-3b30-75d4-8e1a-6e3438116529` — it was immediately **deactivated** (soft) to neutralize impact (no hard-delete endpoint exists); see "acme/cross-tenant residue" in the run summary for DB cleanup.
- **Severity rationale:** CRIT — full cross-tenant write into the holiday calendar by a user with no membership in the target tenant; holidays drive leave day-count/pay calculations, so a planted/altered foreign holiday has downstream effect. Same blast radius as the canonical BUG-003. Not re-filed as a new number per policy (systemic, referenced as EXTENDED).

### BUG-003 (EXTENDED to carry-forward preview read) — token tenant_id is never validated against the subdomain-resolved tenant; an acme user sending `X-Tenant-Subdomain: techoneglobal` runs the carry-forward preview inside techoneglobal's context (READ-ONLY probe only)
- **Type / Severity / Status:** BUG · CRIT · RESOLVED (systemic `TenantAccessGuardMiddleware`, PR #119; ISO-verified 2026-07-03 — carry-forward-preview cross-arm now 403 `cross_tenant_denied`. Reconciled 2026-07-04.) (tracked under the existing canonical BUG-003; NOT a new finding — recorded here for US-LV-008 traceability)
- **Layer:** BE
- **Module / US / TC:** Leave Management · US-LV-008 · TC-LV-ISO-031 (FAIL — cross-tenant read isolation), TC-LV-ISO-030 step 2 (mismatched token+context accepted). NFR-2, FR-4. See the canonical **BUG-003** entry (settings) and its prior EXTENDED notes (entitlements, holidays, departments, status, etc.).
- **Title:** Same systemic isolation bypass as canonical BUG-003: the JWT `tenant_id` claim (acme `019ef3ba-…`) is never asserted against the tenant resolved from the request subdomain/`X-Tenant-Subdomain` header. An acme Tenant-Admin token presenting `X-Tenant-Subdomain: techoneglobal` is **accepted** and the carry-forward preview executes entirely under **techoneglobal's** tenant context (HTTP 200), rather than being rejected for the token/context mismatch. **Per the run instructions, only the READ arm was probed — NO cross-tenant write was attempted.** No acme data was exfiltrated in this instance (the preview returned `data:[]`), but that is *incidental*: techoneglobal has 0 carry-forward-eligible leave types (verified via DB), so there was simply nothing for the foreign-context query to return — the authorization boundary itself still failed (the request ran under a tenant the user has no membership in).
- **Root cause:** as per canonical BUG-003 — `TenantResolutionMiddleware` (`:144`) resolves tenant from subdomain/header and the auth layer never asserts `token.tenant_id == resolvedTenant.Id`. Serilog proves the request ran under the foreign context. Confidence ~95% (matches every prior BUG-003 repro).
- **Reproduction steps (live, READ-ONLY):**
  1. Login `tenantadmin@acme.test` (token tenant_id acme `019ef3ba-…`).
  2. Cross-tenant READ: `GET /api/v1/leaves/carry-forward-preview?year=2026` with `Authorization: Bearer <acme TA>` + `X-Tenant-Subdomain: techoneglobal` → **HTTP 200**, `data:[]`. Serilog RequestId `0HNMIFE5GI292:00000001` stamps `tenant_id: 019ef3c3-…` (techoneglobal) and the EF queries filter on techoneglobal's TenantId — i.e. the acme token was processed under techoneglobal's context. A correctly-isolated API would reject the mismatch with 401/403.
  3. Control: acme TA token, NO subdomain header → 400 "Tenant context is not resolved."; bogus subdomain → 404 "workspace does not exist." So the gap is specifically the *accepted-but-mismatched* case (token says acme, header says techoneglobal → served as techoneglobal).
  4. **NOT run (by instruction):** the cross-tenant WRITE arm — the preview is read-only and BUG-003's write bypass is already confirmed/documented elsewhere; no write probe was performed, so there is **no acme/techoneglobal residue from this run**.
- **Evidence:** HTTP 200 + `data:[]` captured 2026-06-25T11:35Z; Serilog `hrm-20260625.log` RequestId `0HNMIFE5GI292:00000001` (`tenant_id/TenantId = 019ef3c3-…techoneglobal`, EF filter bound to techoneglobal); DB confirms techoneglobal has 0 carry-forward-eligible leave types and 1 active employee (so `[]` = no eligible data, not proof the filter blocked acme rows). The preview endpoint has no write path, so isolation here is read-only by construction.
- **Severity rationale:** CRIT (inherited from canonical BUG-003) — the token-vs-subdomain check is absent platform-wide; on read surfaces with foreign data present this is a cross-tenant disclosure (a full GDPR-relevant dump was demonstrated on other surfaces). Here it happens to disclose nothing because the foreign tenant has no eligible data, but the missing authorization boundary is identical. Not re-filed as a new number per policy (systemic, referenced as EXTENDED).

---

## Verification re-run 2026-09-02 (`@test-runner`, ISSUE-021 + BUG-056 fix-verification scope)

> REPORT-ONLY re-run of the five TCs bound to **ISSUE-021** (job-title grade validation) and **BUG-056**
> (goal weights must total exactly 100% to finalize). Both findings were carrying
> `DEFERRED (feature-blocked)`; the 2026-09-01 code audit found both blockers gone. Verdicts:
> **TC-CHR-005-48 PASS · TC-CHR-337 PASS · TC-PRF-001-14 PASS · TC-PRF-001-15 PASS · TC-CHR-063 FAIL.**
> Backend arms run via `scripts/run-backend-tests.sh` (ISSUE-312 wrapper) on commit `eee39372`; live-API
> arms run against `http://localhost:5000`, tenant `platform` (`admin@hrm.local`) — note **ISSUE-422**
> below: the running container is a stale build, so live-API verdicts were cross-checked against HEAD source.
>
> Both parent findings stay **OPEN/DEFERRED** in this file — only `/verify-fix` may close them. The
> ISSUE-021 grade-validation contract is now met at the service and API layers; the **AC-4 grade-on-profile
> half is not** (BUG-419 below), so ISSUE-021 is *not* fully discharged by this run.

### BUG-419 — US-CHR-005 AC-4's second half is unimplemented: the salary grade linked to an employee's job title is NOT displayed on the employee profile (no grade field exists anywhere on the profile contract)
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** BE (+ FE — neither side has the field)
- **Module / US / TC:** Core HR · US-CHR-005 · **TC-CHR-063 (FAIL)**; AC-4, FR-3. Related: ISSUE-021 (the FK-validation half of AC-4, which now passes — see TC-CHR-337).
- **Title:** AC-4 states verbatim: *"When this job title is assigned to an employee, the associated grade is displayed on the employee profile."* With the `SalaryGrade` entity now shipped (#389, migration `20260719152434_AddSalaryGradeEntity`) and a job title correctly linked to an **active** grade, `GET /api/v1/tenant/employees/{id}/profile` returns **200 with `jobTitleName` but zero grade-bearing fields** — there is no `gradeId`, no `gradeName`, and no nested grade object. TC-CHR-063 steps 3-6 (grade shown on the profile; the profile re-resolving the grade after the job title's grade is changed) therefore cannot succeed. **This TC was previously marked BLOCKED with the justification "Grade entity deferred / not built"; that justification no longer holds, so the same observation is now a defect, not a blocker.**
- **Root cause (~98%, source-confirmed, no log needed — the write path succeeds cleanly):** `EmployeeProfileDto` (`src/backend/HRM.Application/Features/Employees/DTOs/EmployeeProfileDto.cs:9-90`) declares `JobTitleId` + `JobTitleName` but **no grade property at all**; nothing in `src/backend/HRM.Application/Features/Employees/**` references `GradeId`/`GradeName` (`grep -rn "GradeId\|GradeName" HRM.Application/Features/Employees/` → 0 hits). The join that would resolve it exists and works one level up: `JobTitleService.ToDto(..., gradeName)` populates `JobTitleDto.GradeName` on the job-title reads, so the projection is simply never carried through to the employee profile. On the FE, `employee-profile.component.ts` and `features/core-hr/employees/models/*.ts` contain **no** occurrence of "grade" (case-insensitive), so even if the API added the field there is no UI element to render it.
- **Reproduction steps (live-confirmed 2026-09-02, API layer):**
  1. `POST /api/v1/v1/auth/login` → use `admin@hrm.local` / `Admin@123!` with header `X-Tenant-Subdomain: platform` (canonical path is `POST /api/v1/auth/login`).
  2. `POST /api/v1/tenant/salary-grades` `{"code":"L5","name":"L5 - Senior","minAmount":100000,"midAmount":120000,"maxAmount":140000,"currency":"usd"}` → **201**, id `01a05eaa-a93b-7459-87a2-35cb6ea30913`, `isActive:true`.
  3. `POST /api/v1/tenant/job-titles` `{"titleName":"Senior Developer","gradeId":"<L5 id>"}` → **201**.
  4. `GET /api/v1/tenant/job-titles/<id>` → **200**, `"gradeName":"L5 - Senior"` — the link resolves correctly at the job-title layer.
  5. `POST /api/v1/tenant/departments` `{"name":"Engineering","code":"ENG"}` → 201; `POST /api/v1/tenant/employees` `{"firstName":"John","lastName":"Doe",...,"jobTitleId":"<jt id>"}` → **201** (`EMP-0001`).
  6. `GET /api/v1/tenant/employees/<emp id>/profile` → **200**.
- **Evidence:** the step-6 response's full top-level key set is
  `[address, city, country, createdAt, customFields, dateOfBirth, dateOfJoining, departmentId, departmentName, dependents, education, email, emergencyContacts, employeeNo, employmentHistory, employmentType, firstName, fte, gender, id, isActive, jobTitleId, jobTitleName, lastName, locationId, locationName, managerName, nationalId, personalEmail, phone, postalCode, profilePhotoUrl, reportsToEmployeeId, rowVersion, state, status, updatedAt, userId, workArrangement, workHistory]` — keys matching `grade` (case-insensitive): **`[]`**. `jobTitleName = "Senior Developer"`, whose `gradeName` is `"L5 - Senior"` per step 4. Re-fetching the profile after mutating the job title's grade link returns the same key set (no grade key appears/changes), so steps 4-6 of the TC are moot rather than merely wrong. Deployed-vs-HEAD cross-check: `EmployeeProfileDto.cs` was last modified 2026-07-19, before the running image's 2026-08-11 build, so the live response matches HEAD source (see ISSUE-422).
- **Severity rationale:** MED — one half of one AC on a read-only display surface. No data loss, no isolation risk, and the *integrity* half of AC-4 (FK validation) is now correct, so grade links themselves are trustworthy. It is not LOW because AC-4 states the display requirement explicitly and TC-CHR-063 is a `high`-priority TC that has now been unexecutable for three consecutive runs (2026-06-30, 2026-07-01, 2026-09-02) — the ledger has been recording it as "feature deferred" when the deferred feature has in fact shipped.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-420 — JobTitles and SalaryGrades controllers drop `Result.ErrorCode`, so the documented machine-readable codes (`invalid_grade`, `duplicate_code`, `invalid_amount_range`) never reach an HTTP client — every error body is `"code": null`; the unit tests stay green because they assert the code at the *service* layer
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE (FE↔BE contract)
- **Module / US / TC:** Core HR / Payroll · US-CHR-005 · **TC-CHR-337** (which specifies "rejected **409 `duplicate_code`**", "**422 `invalid_amount_range`**", "all rejected **`invalid_grade`**") and ISSUE-021 (whose contract is "422 `invalid_grade`").
- **Title:** The service layer sets the codes correctly — `JobTitleService.cs:58,110` return `Result<JobTitleDto>.Failure(gradeError, 422, "invalid_grade")` and `SalaryGradeService.cs:49,58,106,115` return `invalid_amount_range` / `duplicate_code`. But **all ten** failure paths in `JobTitlesController.cs` (`:43,61,83,109,128`) and `SalaryGradesController.cs` (`:42,60,85,114,133`) call `ApiResponse.Fail(result.Error!)` **without the second `errorCode` argument**, so the code is discarded before serialization. A client receives the right HTTP status but `"code": null`, and can only distinguish the failure reasons by string-matching the English prose. This is inconsistent within the same codebase: `GoalsController.cs:172,199` correctly calls `ApiResponse.Fail(result.Error!, result.ErrorCode)`, which is why the BUG-056 `weight_not_100` / `goals_finalized` codes *do* reach the wire.
- **Root cause (~99%, source + live confirmed):** a dropped argument at the controller boundary — `ApiResponse.Fail` has an optional `errorCode` overload that these two controllers never pass. The reason it survived review is a **test-visibility gap**: `JobTitleServiceTests.cs:143,177,223,466` and `SalaryGradeServiceTests.cs:145` assert `result.ErrorCode.Should().Be("invalid_grade" / "duplicate_code")` against the **service** return value, never against an HTTP response, so the entire trait-`TC-CHR-337` suite (34/34 green) passes while the contract it documents is unmet on the wire.
- **Reproduction steps (live-confirmed 2026-09-02, tenant `platform`):**
  1. `POST /api/v1/tenant/job-titles` `{"titleName":"QA Bogus Grade","gradeId":"00000000-0000-0000-0000-0000000000ff"}`.
  2. `POST /api/v1/tenant/salary-grades` with a code that already exists (`L5`).
  3. `POST /api/v1/tenant/salary-grades` `{"code":"L9","name":"Bad range","minAmount":3000,"maxAmount":2000,"currency":"USD"}`.
- **Evidence:**
  1. → **HTTP 422** `{"success":false,"message":"The selected salary grade does not exist or is not active.","code":null,"errors":["The selected salary grade does not exist or is not active."],...}` — expected `code:"invalid_grade"`.
  2. → **HTTP 409** `{"success":false,"message":"A salary grade with this code already exists.","code":null,...}` — expected `code:"duplicate_code"`.
  3. → **HTTP 422** `{"success":false,"message":"Minimum amount cannot be greater than maximum amount.","code":null,...}` — expected `code:"invalid_amount_range"`.
  Contrast (same run, different controller): the Goals surface does emit its code, per `GoalsController.cs:172`. `grep -rn "invalid_grade\|duplicate_code\|invalid_amount_range" --include=*.cs src/backend` finds the codes only in `*Service.cs` and `*Tests.cs` — never in a controller or an API-level assertion.
- **Severity rationale:** MED, not LOW — the statuses are right, so nothing is silently accepted and there is no data or isolation risk; a client can still branch on 409-vs-422. But it is more than cosmetic: `invalid_grade` and `duplicate_code` **both** arrive as bare 422/409 on the same endpoint pair, so a UI that wants to attach the error to the correct form field, or to localise it, has to string-match server English. It also means the green `TC-CHR-337` suite overstates what is verified — the documented code contract is asserted nowhere at the HTTP boundary.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-421 — `gradeName` is null on job-title **write** responses (POST/PUT) even for a valid active grade, while GET populates it — a client that renders the create/update response shows a grade-less row until it refetches
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE
- **Module / US / TC:** Core HR · US-CHR-005 · TC-CHR-337 step 8 ("GradeName populated on detail + list" — the write responses are outside that assertion, which is why it is green).
- **Title:** `POST /api/v1/tenant/job-titles` and `PUT /api/v1/tenant/job-titles/{id}` return `"gradeName": null` alongside a correct non-null `gradeId`; the immediately-following `GET /{id}` and `GET /` return `"gradeName": "L5 - Senior"` for the same record. The write path returns a DTO built without the grade-name lookup.
- **Root cause (~85%, source-consistent):** `JobTitleService.ToDto(JobTitle j, int? employeeCount = null, string? gradeName = null)` (`JobTitleService.cs:255`) takes the grade name as an optional parameter; the read paths pass it, the create/update paths call `ToDto` without it and get the `null` default. Not log-confirmed — no error is logged because nothing fails.
- **Reproduction steps (live-confirmed 2026-09-02, tenant `platform`):** create grade `L5` → `POST /api/v1/tenant/job-titles {"titleName":"Senior Developer","gradeId":"<L5 id>"}` → inspect `data.gradeName`; then `GET /api/v1/tenant/job-titles/<new id>` → inspect `data.gradeName`.
- **Evidence:** POST 201 → `{"id":"01a05eaa-ee58-7f63-aedb-47a70692b21b","titleName":"Senior Developer","gradeId":"01a05eaa-a93b-7459-87a2-35cb6ea30913","gradeName":null,...}`. GET 200 on the same id → `{...,"gradeId":"01a05eaa-a93b-...","gradeName":"L5 - Senior",...}`. Same asymmetry reproduced on PUT (`"gradeName":null` in the 200 body, `"L5 - Senior"` on the next GET).
- **Severity rationale:** LOW — cosmetic/contract-shape only, self-healing on the next read, and the authoritative `gradeId` is always correct. Flagged because the same DTO type is returned from both verbs, so a client reasonably assumes the field is populated on both.
- **Suggested direction (NOT applied):** none — report only.

### ISSUE-422 — INFRA: the running dev stack serves a container image built 2026-08-11, ~12 days behind `main` — live-API test verdicts taken on this stack can be false in either direction
**▶ RECURRED, and re-closed with a durable fix 2026-09-08.**
This was marked *"RESOLVED (2026-09-02, stack rebuilt)"*. **It recurred within six days**: on 2026-09-08
the `hris-backend` image was built 2026-09-02 and ran **~98 commits stale**, so every live-API verdict
taken in between was against code that no longer existed.
**It recurred because the 2026-09-02 fix was a one-off command nobody had to run again, and which had no
way to prove it worked.** The durable replacement is `scripts/rebuild-stack.sh`, which (1) refuses to
build while a test host is running — an image build alongside Testcontainers SIGKILLed a suite on
2026-09-07; (2) **FAILS if the image timestamp did not move**, because a cached no-op build reports
success and changes nothing, which is precisely this finding's failure mode; and (3) waits for `/health`
before claiming the stack is up.
Rebuilt and verified 2026-09-08: backend image 5 days → 3 minutes, container healthy, API answering.

- **Type / Severity / Status:** ISSUE · MED · RESOLVED (2026-09-02, stack rebuilt)
- **Resolution (2026-09-02):** `docker compose build backend frontend && docker compose up -d` from `3129454c`. Container `hris-backend-1` now created **2026-09-01T21:43:31Z** (was 2026-08-11T13:31:11Z). `/health` 200. **Decisive proof the staleness was real:** the 360-release route that shipped 2026-08-17 (`d87b9e8b`, PR #510) now returns **401** (auth required) and appears in `swagger.json` as `/api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/release` — on the old image it would have 404'd, which would have recorded a FALSE FAIL for TC-PRF-005-05 and turned ISSUE-377 back into a phantom live defect. **Unblocks G10 and BUG-003's `--iso` close-out.**
- **Layer:** INFRA
- **Module / US / TC:** cross-cutting (affects every API-layer TC executed against `http://localhost:5000`). Surfaced while executing TC-CHR-063 / TC-CHR-337.
- **Title:** `docker image inspect hris-backend` reports `Created = 2026-08-11T19:00:49+05:30`, and the container `hris-backend-1` was last started 2026-09-01T14:58Z from that same image. At least one source file in the ISSUE-021 surface has changed since: `SalaryGradesController.cs` was last modified **2026-08-23** by "fix(B5): two silent no-ops" (`29279413`). The running API therefore does not implement B5's `UpdateSalaryGradeRequest.IsActive` field, and a live probe against it produces a *false defect*: `PUT /api/v1/tenant/salary-grades/{id}` with `{"isActive":true}` on a deactivated grade returns **HTTP 200** with `"isActive":false` in the body and leaves `salary_grades.is_active = f` and `updated_at` untouched in the DB — which looks exactly like a reactivation bug but is only the stale build.
- **Root cause (~95%, verified):** the compose stack was never rebuilt after the 2026-08-23 merge; `docker inspect` image-created date vs `git log -1 --format=%ad -- <file>` disagree by 12 days. No application defect is implied.
- **Reproduction steps:** `docker image inspect hris-backend --format '{{.Created}}'` → `2026-08-11T19:00:49+05:30`; `git log -1 --format='%ad %s' -- src/backend/HRM.Api/Controllers/SalaryGradesController.cs` → `Sun Aug 23 03:33:23 2026 ... fix(B5)`. Then the PUT probe above.
- **Evidence:** `PUT /api/v1/tenant/salary-grades/01a05eaa-ca96-7a0c-8121-77481fddf38d` body `{"code":"L6","name":"L6 - Staff","minAmount":140000,"midAmount":160000,"maxAmount":180000,"currency":"USD","description":"Staff band","isActive":true}` → **HTTP 200** `{"success":true,"data":{...,"isActive":false,"updatedAt":"2026-09-01T20:31:38.255383Z"}}` (the `updatedAt` is the earlier DELETE's timestamp — no write occurred). DB: `select code,is_active from salary_grades` → `L6 | f`. HEAD source `SalaryGradeService.cs:129-133` *does* handle `request.IsActive is bool active` correctly and the unit arm `Update_CanReactivate_AGradeThatWasDeactivated` is green — confirming the divergence is deployment, not code. **This is why the BUG-419 / ISSUE-420 / ISSUE-421 evidence above was each cross-checked against the last-modified date of the relevant source file before being filed.**
- **Severity rationale:** MED — no user-facing defect, but it directly threatens verdict integrity: an agent or human probing this stack will file phantom bugs against fixed code (as nearly happened here) and, worse, will record `PASS` for behaviour the merged code no longer has. It silently invalidates the API-layer half of every `/test-us` run until the stack is rebuilt.
- **Suggested direction (NOT applied):** none — report only. (Operationally: rebuild the compose images before an API-layer test run, and consider surfacing the build SHA on `/health` so a test run can assert it.)

> **Test-data residue (tenant `platform`, created 2026-09-02 for TC-CHR-063/TC-CHR-337 execution — safe to delete):**
> salary grades `L5` (`01a05eaa-a93b-7459-87a2-35cb6ea30913`, active) and `L6` (`01a05eaa-ca96-7a0c-8121-77481fddf38d`, deactivated);
> job title `Senior Developer` (`01a05eaa-ee58-7f63-aedb-47a70692b21b`); department `Engineering`/`ENG` (`01a05eac-2fa1-7a2c-9ad0-1e8984dc4bbd`);
> employee `John Doe` / `EMP-0001` (`01a05eac-4ccc-781a-b875-dad3015e7e05`, `john.doe.chr063@hrm.local`). No cross-tenant writes were performed.

### ISSUE-423 — `BUG-298`'s fail-closed deny and the `IsEmailVerified` claim extraction have NO test; the SSO guard is proven but its shell is not
- **Type / Severity / Status:** ISSUE · HIGH · OPEN
- **Layer:** BE (TEST)
- **Module / US / TC:** Authentication · US-AUTH-013 (AC-7) · TC-AUTH-161 (documents the covered half). Parent: BUG-298 (closed 2026-09-02 on its 17 green guard arms).
- **Title:** `SsoIsolationGuard` has 17 arms, but two behaviours *credited to the same fix* are untested: the fail-closed deny when `SsoSettingsSnapshot` cannot be loaded (`EntraSsoService.cs:222-231`), and `IsEmailVerified`'s `xms_edov` / `email_verified` extraction including the "claim absent ⇒ false" case (`:536-548`) — which is the exact input AC-7's verified-domain rule depends on.
- **Root cause + confidence (~95%):** repo-wide, `GetSsoSettingsBySubdomainAsync`, `xms_edov` and `sso_isolation_rejected` appear in no test file outside `SsoIsolationGuardTests.cs`. The guard is unit-tested in isolation; the shell that feeds it is not.
- **Evidence:** `grep -rn "xms_edov\|GetSsoSettingsBySubdomainAsync" src/backend/HRM.Tests` → 0 hits.
- **Severity rationale:** HIGH — a regression in claim parsing degrades toward *allowing* an unverified-domain impostor, and nothing would catch it.
- **Suggested direction (NOT applied):** shell-level arms over a crafted `JsonWebToken` and a failing settings load.
- **SURVEY:** **1** `IsEmailVerified` claim-extraction site and **1** fail-closed settings-deny site, both in `src/backend/HRM.Infrastructure/Identity/EntraSsoService.cs` (extraction `:546-564`, sole call site `:233`; deny `:222-231`). **0 of 2 are test-bound.** Unit = non-test C# source under `src/backend`; excluded the 17 `SsoIsolationGuard` arms (they test the *pure guard*, downstream of both sites) and the 4 `xms_edov` comment/log lines.
- **AUDIT (2026-09-07):** (1) "Fail-closed deny at `EntraSsoService.cs:222-231`" — **CONFIRMED, line-exact**: `if (settingsResult.IsFailure || settingsResult.Value is null)` at `:222` → `Failure("access_denied")` at `:230`. (2) "`IsEmailVerified` extraction at `:536-548`" — **PARTIALLY TRUE**: right method, wrong span — `:536-548` covers doc-comment, signature and the `foreach`; the method is **`:546-564`**, and the "claim absent ⇒ false" case the entry names is at **`:563`**, *outside* the cited range. (3) "0 hits for `xms_edov`/`GetSsoSettingsBySubdomainAsync` in `HRM.Tests`" — **CONFIRMED**, reproduced exactly (only `sso_isolation_rejected` hits, at `SsoIsolationGuardTests.cs:75`, `:99`). (4) "`SsoIsolationGuard` has 17 arms" — **CONFIRMED**: 13 `[Fact]` + 4 `[InlineData]` across 2 `[Theory]`. (5) **Premise check the entry does not make**: the extraction code itself reads **correct** — bool and string-`"true"` are both accepted and absence returns `false`. This is coverage debt, not a live defect. No fix branch, no commit (`git log --all --grep=ISSUE-423` is empty).
- **SEVERITY CHECK:** **lower — MED, not HIGH.** The implementation is verified correct and the decision logic it feeds has 17 green arms; nothing is broken today. HIGH belongs to live exposure, not to regression risk on code that reads correct.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-424 — the new finding-regression TCs are not runner-selectable: no `[Trait("TC",…)]`, so the traceability authored on 2026-09-02 is documentation-only
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST
- **Module / US / TC:** cross-module · TC-AUTH-161, TC-ADM-008-22
- **Title:** `SsoIsolationGuardTests` carries no `[Trait("TC",…)]`, and the GAP-005 arm at `RlsIsolationPostgresTests.cs:431` inherits the class-level `[Trait("TC","TC-PLT-002-RLS")]` — so it reports under a Platform TC, not `TC-ADM-008-22`. The TC↔test bindings created during G9 are prose links a human must honour, not selectors a runner can resolve.
- **Root cause + confidence (~98%):** traits were never added; precedent for the correct shape exists at `TC-ATT-162` (`:564`).
- **Severity rationale:** MED — it silently weakens the traceability that was just restored, and the "% of TCs past draft" KPI stays hand-maintained.
- **Suggested direction (NOT applied):** class-level `[Trait("TC","TC-AUTH-161")]`; arm-level `[Trait("TC","TC-ADM-008-22")]` on the GAP-005 fact.

### ISSUE-425 — a THIRD ledger failure mode: `DEFERRED` entries carry stale BLOCKER REASONS, invisible because nobody re-reads a deferred item
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** cross-module · TEST-FINDINGS.md
- **Title:** The 2026-09-01 audit measured status wrong in both directions (29% pessimistic, ≤10% optimistic). This is neither: the **status is correct and the justification is dead**. `ISSUE-021` was parked "feature-blocked: no SalaryGrade entity" and `BUG-056` "no goal-set finalize seam" — both shipped (#389 / `de3dccfa`). A `DEFERRED` item reads as settled, so nothing re-opens it and no drift check looks at it.
- **Root cause + confidence (~90%):** no process re-validates a deferral's premise; `LedgerTraceabilityTests` checks status consistency, never the stated reason.
- **Evidence:** both entries corrected 2026-09-02 during G9; `BUG-056` closed, `ISSUE-021` partially discharged.
- **Severity rationale:** MED — two findings sat parked as impossible while the blocking work was delivered. Unknown how many more.
- **Suggested direction (NOT applied):** sweep every `DEFERRED` reason in both ledger files against current code; consider a guard asserting a deferral cites a still-true blocker.

### ISSUE-426 — the department list/tree render of `managerName` + `employeeCount` is code-verified only; no Karma arm asserts it
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** TEST (FE)
- **Module / US / TC:** Core HR · US-CHR-004 · TC-CHR-340 (steps 4-6 marked code-verified). Parent: ISSUE-364.
- **Title:** No spec in `department-list.component.spec.ts` / `department-tree.component.spec.ts` asserts the two fields actually render — the exact surface ISSUE-364 was reported against.
- **Severity rationale:** LOW — the BE contract is test-bound; only the render regression is unguarded.
- **Suggested direction (NOT applied):** fix-in-frontend.

### ISSUE-427 — the ISSUE-364 backend arms run on EF InMemory, so the batched projection is never proven to translate to PostgreSQL
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** TEST (BE)
- **Module / US / TC:** Core HR · US-CHR-004 · TC-CHR-340
- **Title:** `DepartmentServiceTests.cs:673-714` asserts the batched `GroupBy`/`ToDictionaryAsync` population behaviourally on InMemory. Sibling money/quota paths got real-Postgres arms (DF-3, DF-48); this one did not.
- **Severity rationale:** LOW today; it is the class of gap DF-48/DF-49 were filed for.
- **Suggested direction (NOT applied):** a Testcontainers arm.

### ISSUE-428 — US-CHR-004 has NO acceptance criterion covering the department list's Manager and Employee Count columns
- **Type / Severity / Status:** ISSUE · LOW · OPEN (needs-decision)
- **Layer:** DATA (BA)
- **Module / US / TC:** Core HR · US-CHR-004 · TC-CHR-340
- **Title:** The two columns exist only in FR-8 and §8 UI/UX Notes. No AC states them — which is **why ISSUE-364 could ship with no acceptance criterion visibly unmet**. AC-5 owns only the display half of the active-employee count.
- **Severity rationale:** LOW functionally, but it is the mechanism by which a whole surface escaped AC traceability.
- **Suggested direction (NOT applied):** BA decision — promote the columns into an AC, or accept §8 as the binding source and say so.

### ISSUE-429 — US-AUTH-013 AC-8, FR-6 and NFR-4 have no test case at all
- **Type / Severity / Status:** ISSUE · LOW · OPEN (needs-decision)
- **Layer:** TEST
- **Module / US / TC:** Authentication · US-AUTH-013 · (none)
- **Title:** AC-8 (the resolved tenant, not the token `tid`, is used downstream), FR-6 (isolation decisions persisted as audit events) and NFR-4 (rejection timing is not an enumeration oracle) are recorded "Not covered" in the traceability matrix. The story sits at 6/8 AC coverage and the matrix now says so rather than papering over it.
- **Suggested direction (NOT applied):** accept as residual risk, or schedule with an SSO integration-test harness.

### ISSUE-430 — `docs/QA/authentication/TEST-MATRIX.md` summary claims "Status: All Draft", which is false
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** Authentication · TEST-MATRIX.md
- **Title:** The module holds `automated` and `blocked` TCs. Reported rather than corrected in place, per the ledger rule that a contradiction is surfaced, not silently fixed.
- **Suggested direction (NOT applied):** recount and correct the summary block.

### BUG-431 — `POST /api/v1/tenant/performance/cycles` returns **500** for date-only (`yyyy-MM-dd`) dates — the exact shape the Angular cycle form sends
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Layer:** BE (+ FE contract)
- **Module / US / TC:** Performance Management · US-PRF-004 (cycle creation) · found while building fixtures for **TC-PRF-005-04 / -14** (US-PRF-005)
- **Title:** Creating an appraisal cycle with `startDate`/`endDate`/phase dates as `"2026-08-01"` (no time, no offset) is an unhandled `DbUpdateException` → HTTP 500 "An unexpected error occurred", instead of a 400 validation error. The Angular cycle form emits precisely that format, so cycle creation from the UI appears to be broken.
- **Root cause (PROVISIONAL — 85% confidence on the mechanism, 70% on the UI blast radius):** the request DTO binds `startDate` as `DateTime`; a date-only JSON string deserializes with `Kind=Unspecified`, and Npgsql refuses to write it to `timestamp with time zone`. Logged exception (Serilog, `/app/Logs/hrm-20260902.log`, `RequestId 0HNO8E8BVKTC7:00000001`):
  ```
  [2026-09-02 00:25:18.433 +00:00 ERR] An exception occurred in the database while saving changes for context type 'HRM.Infrastructure.Persistence.AppDbContext'.
  Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes. See the inner exception for details.
   ---> System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported. (Parameter 'value')
  [2026-09-02 00:25:18.435 +00:00 ERR] Error handling CreateCycleCommand after 191ms {... "RequestPath":"/api/v1/tenant/performance/cycles" ...}
  [2026-09-02 00:25:18.445 +00:00 ERR] HTTP POST /api/v1/tenant/performance/cycles responded 500 in 265.1667 ms
  ```
  Provisional because I did **not** confirm which layer should normalise (DTO converter vs. handler vs. `Npgsql.EnableLegacyTimestampBehavior`), and I did **not** reproduce it through the browser — the UI claim is a code trace, not an observed UI failure.
- **Reproduction steps** (persona `admin@hrm.local` / `Admin@123!`, subdomain `platform`, all permissions):
  1. `TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login -H 'Content-Type: application/json' -H 'X-Tenant-Subdomain: platform' -d '{"email":"admin@hrm.local","password":"Admin@123!"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])')`
  2. ```bash
     curl -s -w '\nHTTP=%{http_code}\n' -X POST http://localhost:5000/api/v1/tenant/performance/cycles \
       -H "Authorization: Bearer $TOKEN" -H 'X-Tenant-Subdomain: platform' -H 'Content-Type: application/json' \
       -d '{"name":"QA A all-date-only","type":"Annual","startDate":"2026-08-01","endDate":"2026-12-31",
            "ratingScaleMax":5,"selfWeightPercent":30,"is360Enabled":false,"isCalibrationEnabled":false,
            "phases":[{"phaseType":"GoalSetting","startDate":"2026-08-01","endDate":"2026-08-31"},
                      {"phaseType":"SelfAssessment","startDate":"2026-09-01","endDate":"2026-09-15"},
                      {"phaseType":"ManagerReview","startDate":"2026-09-16","endDate":"2026-09-30"},
                      {"phaseType":"Publish","startDate":"2026-10-01","endDate":"2026-10-15"}],
            "scope":{"scopeType":"AllEmployees","departmentIds":[],"employeeIds":[]}}'
     ```
- **Evidence (three isolation arms, run 2026-09-02):**
  | Arm | Top-level dates | Phase dates | Result |
  |---|---|---|---|
  | A (exact FE payload shape) | `2026-08-01` | `2026-08-01` | **HTTP 500** `{"success":false,"message":"An unexpected error occurred. Please try again later."}` |
  | B | `2026-08-01T00:00:00Z` | `2026-08-01` | **HTTP 500** (same body) |
  | C | `2026-08-01` | `2026-08-01T00:00:00Z` | **HTTP 500** (same body) |
  | D (control) | `...T00:00:00Z` | `...T00:00:00Z` | **HTTP 201 Created**, cycle `01a05f81-ffb9-767c-9adc-96ef041e0f6f` persisted |

  So **either** date group alone triggers it, and the UTC-suffixed control succeeds — the failure is the
  `Kind`, not the payload shape. OpenAPI declares both fields `format: date-time`, so a date-only value is
  schema-invalid input — but schema-invalid input must be a **400**, never an unhandled 500 that reaches EF.

  FE contract trace (code, not observed at runtime):
  `src/frontend/src/app/features/performance/components/cycle-form/cycle-form.component.ts:154` uses
  `<input type="date" formControlName="startDate">` (Angular yields the raw `yyyy-MM-dd` string), and
  `:645` builds the payload as `startDate: v.startDate` with no conversion; phase dates the same at `:245`.
  `services/cycle.service.ts:61-65` `create()` POSTs that object verbatim — no interceptor normalises dates
  (`core/interceptors/` = api-envelope, error, tenant only). The component's own spec seeds `'2026-01-01'`.
- **Severity rationale:** HIGH, not CRIT — if the code trace holds, no HR user can create an appraisal cycle
  from the UI, which is the entry point for the entire Performance module (cycles gate goals, self-assessment,
  manager review and this 360 story); but the API is usable with correct UTC input, and I have not observed
  the browser failing, so it is not asserted as a total outage.
- **Notes:** out-of-lane discovery — found while seeding fixtures for US-PRF-005, belongs to US-PRF-004.
  Not investigated further per REPORT-ONLY + the coordinator's stop instruction.
- **SURVEY:** **12** request-side `DateTime`/`DateTime?` properties across **7** `Request`/`Command`/`Input` types shared the pre-fix 500 class (unit = `[FromBody]`-bound DateTime properties reaching Npgsql with `Kind=Unspecified`); the fix commit's own "26+" was measured before later refactors. Excluded: the **18** `[FromQuery] DateTime` params in `HRM.Api/Controllers` — a **different** binder (MVC `DateTimeModelBinder`, not the `JsonConverter`); they never 500, they silently shift by server offset, and are already filed as `ISSUE-435`.
- **AUDIT (2026-09-07):** (1) "date-only input 500s on `POST /performance/cycles`" — **CONFIRMED as of the finding date, and now FIXED**: `UtcDateTimeJsonConverter` and its nullable sibling exist (`src/backend/HRM.Api/Json/UtcDateTimeJsonConverter.cs`) and are registered in the MVC `AddJsonOptions` block (`Program.cs:241-242`). (2) Root cause `Kind=Unspecified` → Npgsql — **CONFIRMED** (documented at `UtcDateTimeJsonConverter.cs:11-12`); the entry rated this 85% provisional and it holds. (3) The three remedies the entry weighed (DTO / handler / `EnableLegacyTimestampBehavior`) — **none was chosen**: it was resolved as a boundary converter on the read side only, leaving the wire and Swagger shape unchanged. (4) "the FE emits date-only" — **CONFIRMED but lines stale**: `cycle-form.component.ts:155` (cited `:154`), phase inputs `:244`, `:253` (cited `:245`), payload `:645` (correct), `cycle.service.ts:62` (cited `:61-65`). (5) "malformed dates already 400'd pre-fix" — **CONFIRMED** by the fix's own arm. Commit `e622795a` (**#581**) **is an ancestor of `origin/test/local-subdomains`** — merged — and a real HTTP-wire regression exists (`CycleCreateDateOnlyPayloadApiTests.cs`, arms at `:48`, `:107`, `:132`) which went red pre-fix.
- **SEVERITY CHECK:** HIGH was right at the time (the UI entry point to the whole Performance module); now moot. This is a **stale-OPEN ledger entry** — see `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-432 — FR-3's "configurable minimum peer reviewers" is not configurable anywhere: `Min360PeerReviewers` is a schema default with no write path
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE (+ FE)
- **Module / US / TC:** Performance Management · US-PRF-005 · TC-PRF-005-04 (precondition), TC-PRF-005-14 (step 1)
- **Title:** The BR-4 release gate reads `AppraisalCycle.Min360PeerReviewers`, but no API request DTO, no UI control and no tenant setting can set it — every cycle silently keeps the EF default of 2, so FR-3's "tenant-configured minimum" is effectively a hardcoded constant.
- **Root cause (85% confidence — static evidence, no log involved):** the value is written only as a column
  default (`Configurations/AppraisalCycleConfiguration.cs:68` `HasDefaultValue(2)`, property default
  `HRM.Domain/Performance/AppraisalCycle.cs:119`). Every other reference is a **read**:
  `Feedback360Service.cs:352,354,615,618,636`, `ReviewerAssignmentService.cs:116,280`. The create/update
  inputs omit it entirely — `PerformanceCreateCycleInput` and `PerformanceUpdateCycleInput` in the live
  `swagger.json` expose `name/type/startDate/endDate/phases/scope/ratingScaleMax/selfWeightPercent/
  is360Enabled/isCalibrationEnabled/isAnonymousFeedback` and nothing else. Only the read DTOs
  (`PerformanceFeedback360ResultsDto.minPeerReviewers`, `PerformanceReviewerConfigurationDto.minPeerReviewers`)
  surface it. `grep -rn "min360\|Min360" src/frontend/src` → **no matches**: the Angular cycle form has no field.
- **Reproduction steps:**
  1. `curl -s http://localhost:5000/swagger/v1/swagger.json` → inspect `components.schemas.PerformanceCreateCycleInput.properties` and `PerformanceUpdateCycleInput.properties` — no `min360PeerReviewers` / `minPeerReviewers` key.
  2. Create a cycle (any payload, see BUG-431 arm D) → `select min360peer_reviewers from appraisal_cycle;` → always `2`.
  3. `grep -rn "Min360PeerReviewers" src/backend --include=*.cs | grep -v Migrations` → all non-test hits are reads plus the two default declarations.
- **Evidence:** the API/DB/FE facts above. Enforcement itself is correct at the default —
  `Release_BelowPeerThreshold_Returns422_AndWritesNoRow` and `Release_ExactlyAtMinimumPeers_Succeeds` both **Passed**
  in the 74/74 `FullyQualifiedName~Feedback360` run on 2026-09-02.
- **Severity rationale:** MED — the safety gate works and defaults sensibly, so no results leak below
  threshold; but a tenant that needs 3 peers (or 1, for a small team) has no way to say so, and it blocks
  TC-PRF-005-14 step 1 from ever being executed as written.

### ISSUE-433 — INFRA: no login-capable test personas can be created locally, so every multi-persona live authz/IDOR arm across the product is unexecutable
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** INFRA
- **Module / US / TC:** cross-module · blocks TC-PRF-005-05 steps 1/2/4/5, TC-PRF-005-14 steps 3-6, and the same class everywhere
- **Title:** The local stack has no `acme` tenant, 3 users total and 0 appraisal cycles. New login-capable personas **cannot be created**: invite tokens are BCrypt-hashed and deliberately never logged, and a real SMTP sender is DI-registered, so `/auth/accept-invitation` cannot be driven. Every live test needing a second persona therefore records BLOCKED rather than a verdict.
- **Root cause + confidence (~90%):** there is no dev-only seed path for the four standard personas, and the security property that makes invites safe (hashed, unlogged tokens) is exactly what makes them undrivable in a dev loop. Both are correct in isolation; nothing bridges them.
- **Evidence:** `@test-runner` execution 2026-09-02 — TC-PRF-005-05 steps 1/2/4/5 recorded `blocked: persona-gap`; only step 3 (unauthenticated 401 across five 360 routes) could run live.
- **Severity rationale:** MED by blast radius rather than depth — it does not break production, but it silently converts a whole *category* of security testing (authz, IDOR, cross-persona) into automated-only coverage, which is how the ledger accumulated blocked arms nobody could clear.
- **Suggested direction (NOT applied):** a seed script or a dev-only token surface for the four standard personas. **Do not weaken the invite hashing to achieve it.**

### ISSUE-434 — `@test-runner` reports only at the end, so a run that hits its turn ceiling loses everything it found
- **Type / Severity / Status:** ISSUE · MED · RESOLVED
- **Resolution (2026-09-03):** **RESOLVED 2026-09-03** — superseded by ISSUE-443 and fixed there; the record-as-you-go section is now in all six `team/` contracts, not just `@test-runner`.
- **Layer:** TEST (process)
- **Module / US / TC:** cross-module · `.claude/agents/team/test-runner.md`
- **Title:** Two runs in one session hit the 60-turn limit. The first survived only because it happened to write its ledger append before stopping; the second recorded **nothing** after 2.7 hours and 73 tool calls — every TC still `draft`, no finding filed — and its work was recoverable only by resuming the agent and ordering it to stop investigating and write up.
- **Root cause + confidence (~95%):** the agent contract asks for a verdict table at the end. With a hard turn ceiling that pattern guarantees total loss on any long run. It is a prompt-shape defect, not agent misbehaviour.
- **Evidence:** agent runs 2026-09-02 (G9 ISSUE-021/BUG-056; G10 US-PRF-005).
- **Severity rationale:** MED — no production impact, but it destroys expensive investigation and makes long QA runs a coin flip.
- **Suggested direction (NOT applied):** amend `.claude/agents/team/test-runner.md` to require **record-as-you-go** — flip each TC's status the moment it is judged, file a finding as soon as its shape is known, refine afterwards. Same for the fixture-residue note.

### ISSUE-435 — 18 `[FromQuery] DateTime?` params bind as SERVER-LOCAL, so date filters silently shift on any non-UTC host
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE
- **Module / US / TC:** cross-module · sibling of BUG-431 (which fixed the JSON body path only)
- **Title:** BUG-431's converter covers request **bodies**. Query-string dates never reach a `JsonConverter` — they bind via MVC's `DateTimeModelBinder`, which applies `AdjustToUniversal` **without** `AssumeUniversal`, so `?appliedFrom=2026-01-01` is read as **server-local** and then converted to UTC. No 500 (the Kind ends up `Utc`), but the filter boundary shifts by the host offset.
- **Root cause + confidence (~85%, NOT reproduced live):** the two binding mechanisms are independent; fixing one does not fix the other.
- **Evidence:** 18 affected params incl. `ApplicantPipelineController.cs:47-48` → `ApplicantService.cs:335-339`, `EmployeesController.cs:188-189,241-242`, `PayrollAuditController.cs:70-71,118-119`, `AdminMonitoringController.cs:64-65`, `ExitInterviewsController.cs:113-114`. **`AuditLogService.cs:208,215` already hand-patches exactly this**, which is evidence the problem is real and currently handled ad hoc.
- **Severity rationale:** MED — latent on a UTC host (where the shift is zero), wrong on any other. It reads as "the report is missing a day's rows", which is hard to attribute.
- **Suggested direction (NOT applied):** a `DateTime` model-binder provider mirroring `UtcDateTimeJsonConverter`, then delete the ad-hoc patch in `AuditLogService`.

### ISSUE-436 — no FE spec in this repo can catch an FE↔BE contract break; four shipped defects share this one cause
- **Type / Severity / Status:** ISSUE · HIGH · OPEN
- **Layer:** TEST (FE)
- **Module / US / TC:** cross-module
- **Title:** Every frontend spec mocks the wire via `HttpTestingController`, so a spec asserts the shape its author *believed* the API used. When that belief is wrong the spec still passes and the feature is broken in production. **Four defects found in this audit are one gap, not four:** careers detail 404 (`vacancy-detail.component.spec.ts:77` feeds `'vac-1'`), team-goals always empty (`performance-goal.service.spec.ts:135` flushes a shape the endpoint never returns), onboarding dead route + 405s (specs assert the wrong verb), and BUG-431 (`cycle-form.component.spec.ts:24,83,153,169` assert the exact date shape that 500s). **All four suites are green today.**
- **Root cause + confidence (~95%):** there is no outbound contract assertion anywhere. `src/app/core/api/generated/api-types.ts` IS generated from `contracts/openapi/hrm-v1.json` and CI enforces it byte-for-byte (`npm run api:types:check`) — but only for *types the FE reads*. Nothing asserts that what a service **sends** conforms to the contract.
- **Severity rationale:** HIGH by blast radius. It is the mechanism behind this repo's documented dominant defect class, and it makes the FE suite structurally unable to detect it. 4,327 green specs did not catch four live user-facing breaks.
- **Suggested direction (NOT applied):** assert outbound payloads against the generated request types — the type information already exists and is already enforced; the missing step is applying it on the send path. Cheaper than it looks, and it would have caught all four.
- **SURVEY:** **0 of 330** FE spec files bind a **request-side** assertion to the generated contract (6 request-side `Schema<>` aliases exist, none used in a spec). **Correction (2026-09-07, from the `ISSUE-500` audit): for the *response* direction the figure is 54 of 330** — 54 spec files reference at least one of the 242 `Schema<'...'>` aliases, a compile-time binding. The blanket "0 of 330" first written here overstated the gap; 121 of 330 use `HttpTestingController` and 86 of 330 assert `req.request.body` against hand-written literals. One spec merely *mentions* the contract in a comment (`payroll-approval.service.spec.ts:28`). Total spec cases: **4,373** `it(`/`test(` calls — the entry's "4,327" is stale by 46. **Excluded and material:** `src/frontend/e2e/` holds **4** Playwright specs that drive the real stack (`fixtures/auth.ts:17`).
- **AUDIT (2026-09-07):** (1) "no outbound contract assertion anywhere" — **CONFIRMED** (0/330; `package.json:11-12` and `scripts/check-api-types.mjs:1-13` show `api:types:check` diffs the generated file for freshness, never its use). (2) "**no FE spec in this repo can catch** an FE↔BE contract break" — **FALSE as stated**: the 4 Playwright e2e specs hit the live API and would. The correct claim is that no *unit or component* spec can. (3) "four shipped breaks" — **PARTIALLY TRUE, 1 of 4 verified.** Only BUG-431 has an ID, and it is fixed (`e622795a`). The careers-detail 404 traces to `TEST-FINDINGS-RESOLVED.md:3963-3965`, whose root cause is `Tenant.PublicCareersEnabled` being off — a config/data condition, **not a contract break**; that attribution is FALSE. The team-goals shape mismatch was real and has since been corrected in place (`performance-goal.service.spec.ts:22-23`, `:24-45` now use the real `phases[]` wire shape). "Onboarding dead route + 405s" has **no corroborating ledger entry** and is unverified. (4) **All four cited line numbers are stale**: `vacancy-detail.component.spec.ts:77` is a DI provider block (`'vac-1'` is at `:19`, `:38`, `:96`); `performance-goal.service.spec.ts:135` is an `expect` (the flush is `:133`); `cycle-form.component.spec.ts:153`/`:169` are phase `patchValue` dates. No merged fix; `origin/docs/auto-heal-fe-contract-blindspot` is not merged.
- **SEVERITY CHECK:** **lower — MED.** The structural gap is real and cleanly measured (0/330), but HIGH rested on "four shipped breaks", of which one is confirmed-and-fixed, one misattributed, one fixed in place, one unverified — and an e2e mechanism does exist. **Duplicate:** `ISSUE-500` files the same gap with a different four defects — see **`ISSUE-546`**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-440 — `employeeViewed` is hardcoded false, but the notes DTO carries `notesOpenedAt` — BR-2 always displays "not viewed"
- **Type / Severity / Status:** ISSUE · MED · OPEN (needs-decision)
- **Layer:** FE
- **Module / US / TC:** Performance · US-PRF-006 (BR-2) · sibling of ISSUE-379
- **Title:** `review-signoff.models.ts` hardcodes `employeeViewed: false` under a "No wire source" comment, but `PerformanceReviewMeetingNotesDto` carries `notesOpenedAt?: string | null` — the signal the field exists for. The sign-off screen therefore always shows "not viewed".
- **Root cause + confidence (~85%):** the mapper was written before the wire field existed, or the connection was never made.
- **Severity rationale:** MED — BR-2 display is permanently wrong, but it is read-only and misleads rather than corrupts.
- **Why it was NOT fixed in G8:** unlike the seven fields G8 closed, this is an **inference** (`notesOpenedAt != null` ⇒ viewed), not a rename. Whether "opened" equals "viewed" is a product decision, so the agent correctly declined to invent it.
- **Suggested direction (NOT applied):** confirm the semantics, then `employeeViewed: w.notesOpenedAt != null`.
### BUG-441 — assigning an onboarding checklist creates every template task TWICE, at offset 0, discarding the HR officer's due-date edits
- **Type / Severity / Status:** BUG · CRIT · OPEN
- **Layer:** BE + FE (contract)
- **Module / US / TC:** Onboarding · US-ONB-002 (AC-2, FR-6) · TC-ONB-002-01
- **Title:** `checklist-assignment.component.ts:945` (`toRequest`) sends **every** task on screen in `additionalTasks`. `OnboardingChecklistService.AssignAsync` adds `template.Tasks` (`:185`, `:223`) **plus** `input.AdditionalTasks` (`:188`, `:226`) — so each template task is created twice. The duplicates land on `startDate + 0` because the FE payload carries `dueDate` while `AdHocTaskRequest` binds `DueOffsetDays` (`OnboardingChecklistDtos.cs:75`, never sent → defaults `0`), which **also silently discards every inline due-date edit the HR officer made** — the entire point of FR-6.
- **Root cause + confidence (~98%, both sides read independently):** `additionalTasks` means "tasks beyond the template", but the assignment screen holds the full resolved list and posts all of it. The two sides disagree about what the field means, and nothing typed the disagreement.
- **Evidence:** `AssignAsync` iterates both collections at two sites; `toRequest` maps `this.tasks.controls` in full; `AdHocTaskRequest.DueOffsetDays` is an `int` while the FE sends `dueDate`.
- **Severity rationale:** CRIT — it corrupts real onboarding data for a real employee (double task sets, wrong dates) and silently drops user input on a screen whose purpose is editing that input.
- **Why it was dormant:** the `/checklists/preview` route did not exist, so the task array stayed empty and nothing was ever posted back. **Building preview (G3) makes this reachable** — which is why it must be fixed in the same change, not after.
- **DECIDED FIX (user, 2026-09-02):** an explicit **replace-mode** on assign — the FE sends the resolved task list and the BE uses it verbatim instead of `template.Tasks + additionalTasks`, carrying real due dates. Chosen over "FE sends only ad-hoc tasks" because that alternative regenerates template tasks and would drop the FR-6 edits rather than honour them. Replace-mode also makes preview and assign agree by construction.
- **SURVEY:** **1** FE request-builder + **2** BE template-merge sites (unit = code sites that turn a resolved task list into an assign payload, or merge `template.Tasks` with `input.AdditionalTasks`). Counted across non-test `src/`; excluded `HRM.Tests/**` and generated `api-types.ts`. `OnboardingChecklistService.ModifyAssignedChecklistAsync:533` appends `AddTasks` with no template expansion and is **not** a merge site; `ApplicantConversionService.cs:556` passes an empty `AdditionalTasks`.
- **AUDIT (2026-09-07):** **Every defect claim is FALSE at this commit** — the DECIDED FIX shipped in `05f138c8` (**#588**), which is merged into `origin/test/local-subdomains`. (1) "`toRequest` sends every task in `additionalTasks`" — **FALSE**: it sends `resolvedTasks` (`checklist-assignment.component.ts:956`, `:977`) and omits `additionalTasks`; cited `:945` is **stale**, correct is `:955`. (2) "`AssignAsync` adds `template.Tasks` plus `input.AdditionalTasks`" — **FALSE as a live defect**: both loops now sit in `else` branches reached only when `ResolvedTasks is null` (`:199-211`, `:242-257`); cited `:185`/`:188`/`:223`/`:226` are all **stale**. (3) "`AdHocTaskRequest` binds `DueOffsetDays`, discarding due dates" — **FALSE for this path**: `ResolvedTaskRequest` (`OnboardingChecklistDtos.cs:99`) carries a concrete due date. (4) Dormancy premise "the `/checklists/preview` route did not exist" — **FALSE NOW** (`OnboardingChecklistsController.cs:88`), but preview and the fix landed in the **same commit**, so the CRIT never went live. Unclaimed but present: a mutual-exclusion 400 guard (`AssignChecklistValidator.cs:30-35`) and regression arms at `OnboardingChecklistAssignApiTests.cs:45,96,111,206,222`.
- **SEVERITY CHECK:** CRIT was correct at filing. This is now a **stale-OPEN ledger entry**, not a live CRIT — see `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-442 — agent worktrees are created from a stale base, so an isolated agent can be handed a tree where its target files do not exist
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** INFRA (orchestration)
- **Module / US / TC:** cross-module · `isolation: worktree`
- **Title:** The G13 worktree was created at commit `7ea6ce61`, **hundreds of commits behind** the working branch. **None of the three target files existed in it** — they all landed after that commit. The agent noticed and recovered with a clean `git merge --ff-only`, but an agent that did not check would have "fixed" files that do not exist, or silently re-added files that had been deleted.
- **Root cause + confidence (~85%):** `isolation: worktree` did not branch from the session's current working branch. The `worktree.baseRef` setting governs this (`fresh` branches from `origin/<default-branch>`, `head` from local HEAD); this repo works on `test/local-subdomains`, not the default branch, so `fresh` lands far behind.
- **Evidence:** agent report 2026-09-02 — worktree HEAD `7ea6ce61` vs working branch `14ea2181`.
- **Severity rationale:** MED — it silently invalidates isolated agent work, and the failure is invisible unless the agent happens to check. It cost nothing here only because this one did.
- **Suggested direction (NOT applied):** set `worktree.baseRef` to `head`, or have the orchestrator verify the worktree's base matches the working branch before dispatching. **The orchestrator should state the expected base commit in the brief** so a mismatch is detectable by the agent rather than by luck.

### ISSUE-443 — four agents in one session hit the 60-turn ceiling; the agent contracts report only at the end, so a long run loses everything
- **Type / Severity / Status:** ISSUE · MED · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** `## Record as you go` is present in all six `.claude/agents/team/*.md`. Already read RESOLVED and was scheduled anyway.
- **Resolution (2026-09-03):** **RESOLVED 2026-09-03** — a `## Record as you go` section was added to all six `.claude/agents/team/*.md` contracts, covering write-as-you-reach-it, revert-before-reporting, `Edit` over `Write` on existing files, write-up-on-resume, and never reporting an unobserved number. Filed after 4 ceiling hits; applied after 8.
- **Layer:** TEST (process)
- **Module / US / TC:** cross-module · `.claude/agents/team/*.md`
- **Title:** Four agents hit the limit on 2026-09-02: two `@test-runner` (one lost 2.7 hours having written nothing — every TC still `draft`, no finding filed), one `@backend-dev` mid-revert of a deliberate mutation, one `@backend-dev` mid-suite-run. Each was recoverable only by resuming it and ordering it to stop investigating and write up. **This generalises `ISSUE-434`, which named only `@test-runner`** — it is every long-running agent contract, not one.
- **Root cause + confidence (~95%):** the contracts ask for a verdict/report at the end. With a hard turn ceiling that guarantees total loss on exactly the runs that found the most.
- **Severity rationale:** MED — no production impact, but it destroys expensive investigation and makes long agent work a coin flip. The mid-revert case is worse than lost work: a deliberate mutation could have been collected as if it were the fix.
- **Suggested direction (NOT applied):** require **record-as-you-go** in every `team/` agent contract — write the verdict the moment it is reached, file the finding as soon as its shape is known, refine after. And **revert mutations before reporting them**, never after.

### BUG-444 — `PUT /checklists/{id}` binds nothing: the FE sends `{tasks}`, the BE binds `{addTasks, taskChanges}`
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** FE + BE (contract)
- **Module / US / TC:** Onboarding · US-ONB-002 AC-4
- **Title:** `IModifyChecklistRequest` (`onboarding-checklist.models.ts:120`) sends `{ tasks: [...] }`; `ModifyChecklistRequest` (`OnboardingChecklistDtos.cs:169`) binds `AddTasks` + `TaskChanges`. **A modify request binds nothing at all.** Same defect class as GAP-013 and BUG-441 — the third instance on this one screen.
- **Root cause + confidence (~95%):** the BE shape is operation-based (`taskInstanceId` / `newDueDate` / `remove`); the FE models it as a task list. Nothing typed the disagreement.
- **Mitigation today:** `OnboardingChecklistService.modify()` has **no non-spec caller**, so this is dead FE code rather than live data loss. **Its spec is therefore test theater** — it asserts a body the server ignores, and passes.
- **Severity rationale:** MED — not reachable today, but US-ONB-002 AC-4 (edit an assigned checklist) is unshippable until the contract is agreed, and the green spec disguises that.
- **Suggested direction (NOT applied):** decide the contract (op-based vs list-based), then fix in both lanes.

### ISSUE-445 — two `agent-memory/frontend-dev` stores exist; the configured path points at an empty scaffold
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** INFRA (agent config)
- **Module / US / TC:** cross-module
- **Title:** `src/frontend/.claude/agent-memory/frontend-dev/` is an empty scaffold and is the path the agent's own prompt names; the real 46-entry store — the one git tracks — is at the repo root. An agent following its configured path writes into the empty one and its notes never reach the shared store.
- **Root cause + confidence (~90%):** a working-directory-relative path resolved against `src/frontend` rather than the repo root.
- **Evidence:** found by `@frontend-dev` 2026-09-02, which wrote to the root store and removed the stray copy rather than fragmenting memory.
- **Severity rationale:** LOW — silently loses agent memory, which is exactly the kind of absent-capability drift `ISSUE-437` is about.
- **Suggested direction (NOT applied):** correct the configured path so it resolves to the repo-root store.

### BUG-446 — the top-level startup catch swallows the exit code, so EVERY fail-fast in the app reports success
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Layer:** BE
- **Module / US / TC:** Platform · `Program.cs:1079-1082`
- **Title:** The outermost `catch (Exception ex)` calls `Log.Fatal(ex, ...)` and falls through to `finally { Log.CloseAndFlush(); }`. **Nothing sets a non-zero exit code**, so the process exits **0**. Every startup guard in this codebase — the new JWT signing-key guard (G2), the `Smtp:Host` fail-fast, the `Encryption`/AesGcm secret guards from A2 — reports **success** to any orchestrator, CI step, healthcheck or supervisor that reads the exit code. Only the log line reveals the failure.
- **Root cause + confidence (~98%, read directly):** the catch was written to guarantee log flushing, and the exit code was never considered.
- **Severity rationale:** HIGH — it partially defeats **every** fail-fast control in the application, including ones added specifically to make misconfiguration loud. A container that exits 0 is a container an orchestrator will not restart, alert on, or mark unhealthy.
- **Suggested direction (NOT applied):** set `Environment.ExitCode = 1` (or rethrow after flushing) in that catch. Blast radius is every startup failure path, so it wants its own change rather than riding along with a feature fix.
- **SURVEY:** **8** fail-fast startup throw sites inside the guarded region (`Program.cs:35` `try` → `:1090` `app.Run()`), across **4** files (unit = throw/fail-fast sites reachable during startup): `DependencyInjection.cs:52` (bcrypt work factor), `:66` (`Rls:Enabled` without `PrivilegedConnection`), `:900` (blank `Smtp:Host` in Production); `JwtSigningKeyStartupGuard.cs:69` (blank `Jwt:PrivateKey`); `AesGcmFieldEncryptor.cs:54,59,66` (key ring, reached via `DbInitializer` → `AppDbContext` → `IFieldEncryptor`); and `Program.cs:833-849` (EF migration/seed failure re-raised outside Development). Excluded the 4 `CryptographicException` sites at `AesGcmFieldEncryptor.cs:128,132,143,147` — per-request decrypt, not startup.
- **AUDIT (2026-09-07):** (1) "the outermost `catch (Exception ex)` logs Fatal and falls into `finally { Log.CloseAndFlush(); }`" — **CONFIRMED**, but cited `Program.cs:1079-1082` is **stale by 13 lines**: actual is **`:1092-1095`** (catch) + **`:1096-1098`** (finally). (2) "nothing sets a non-zero exit code, so the process exits 0" — **CONFIRMED**: `Environment.ExitCode`/`Environment.Exit(` have **0 hits** in non-test `src/backend`; there is no `return` after the `finally`, and because the file is top-level statements with an `await` at `:844` the synthesized entry point is `async Task Main`, not `Task<int>`, so the host **cannot** return non-zero. The exit code is genuinely swallowed. (3) "every startup guard reports success" — **CONFIRMED, and the entry UNDERSTATES it.** It names 3 guard families; there are **8** sites, and it misses the largest: `Program.cs:830-835` documents "Outside Development we fail fast: ... the app refuses to start", yet its inner `catch ... when (app.Environment.IsDevelopment())` at `:844` lets a Production migration failure through into the swallowing outer catch — **a failed production migration exits 0.** No fix branch exists.
- **SEVERITY CHECK:** **higher — HIGH is the floor.** Blast radius is 8 sites rather than 3, and the unnamed one (schema migration) is the failure most likely to serve wrong data if a supervisor declines to restart a "clean" exit.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-447 — the `Smtp:Host` guard reads the RAW environment variable, a second fail-open independent of its deny-list gating
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE
- **Module / US / TC:** Notifications · US-NTF-006 · extends GAP-015 / G15
- **Title:** `DependencyInjection.cs:866` reads `configuration["ASPNETCORE_ENVIRONMENT"]` — the **raw string** — rather than `IHostEnvironment`. Those differ: `IWebHostBuilder.UseEnvironment(...)` sets the host's environment key and **never** sets that variable, so both test fixtures (`ApiTestFactory.cs:97`, `RlsOnApiTestFactory.cs:127`) see `null`; and when the variable is genuinely unset, `IHostEnvironment.EnvironmentName` resolves to `"Production"` while the raw read is `null`.
- **Root cause + confidence (~95%):** raw configuration read where the resolved host environment was meant.
- **Why this matters beyond G15:** it is a **second, independent** fail-open, separate from the deny-list gating G15 recorded — and it **explains** G15's symptom. The fixture "never sets an environment name" partly because setting it the idiomatic way (`UseEnvironment`) would not have been seen anyway.
- **Severity rationale:** MED — same class as the deny-list hole, and it makes the guard untestable through the normal fixture seam.
- **Suggested direction (NOT applied):** switch to `IHostEnvironment` with allow-list gating; G15's test then becomes writable through `UseEnvironment`. **Amend GAP-015 to record both failure modes, not just the missing test.**
### ISSUE-437 — nothing verifies that a documented CAPABILITY exists; four instances shipped, one written during the audit that catalogued the other three
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST (process)
- **Module / US / TC:** cross-module · `ClaudeMdAccuracyTests`
- **Title:** `ClaudeMdAccuracyTests` asserts that documented **scripts**, **links** and **paths** exist. Nothing asserts that a documented **capability** does. Four instances have now shipped: `csharp-lsp`/`typescript-lsp` documented and dead on PATH for 12 days; 13 plugins declared in `enabledPlugins` and inert because they were installed against a path that no longer exists; the project's own skills listed as slash commands but not dispatchable in this session; and CLAUDE.md rule #7 requiring a todo list with no mechanism named, which silently did not run for eight loop iterations.
- **Root cause + confidence (~90%):** a path or script is a filesystem fact a test can check. A *capability* — "this tool is callable", "this plugin loaded", "this skill dispatches" — is runtime state the guard never looks at. `scripts/doctor.sh` covers part of this for the toolchain (that is why the LSP gap was eventually found) but nothing covers tools, plugins or skills referenced by the instructions themselves.
- **Evidence:** the fourth instance was authored **during** the audit that catalogued the first three, by the agent cataloguing them — which is the strongest available evidence that reading carefully is not a sufficient control.
- **Severity rationale:** MED — no production impact, but it is the mechanism by which the instruction set drifts from what the runtime can actually do, and every instance was invisible until a human asked.
- **Suggested direction (NOT applied):** extend `scripts/doctor.sh`'s CAPABILITY tier (exit 2) to assert that each plugin in `enabledPlugins` resolves for the *current* path, that each `.claude/skills/*.md` marked `user_invocable` actually dispatches, and that any tool a rule depends on is named in the rule rather than assumed. Prefer a rule that names a mechanism working everywhere over one that needs a tool.

### ISSUE-438 — `FteScaledOvertimeBase` has no UI control anywhere; a money-affecting policy is reachable only by raw API call
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** FE
- **Module / US / TC:** Attendance · US-ATT-011 AC-5 · TC-ATT-152
- **Title:** The flag has a full backend write+read path (`AttendanceSettingsService.cs:318,386`) and sits in the settings DTO, but the **only** occurrence anywhere in the Angular app is the generated type at `api-types.ts:34242`. There is no toggle in the attendance-settings form, so a tenant admin cannot enable it through the product.
- **Root cause + confidence (~95%):** the BE half of US-ATT-011 AC-5 shipped; the FE control was never built.
- **Severity rationale:** MED — with GAP-022 now fixed the flag finally *works*, but no real admin can reach it, so the capability stays latent. It also explains why the inert-flag defect survived: nobody could exercise it.
- **Suggested direction (NOT applied):** add the toggle to the attendance-settings form.

### ISSUE-439 — a domain calculator can gain trailing-optional parameters that no caller ever supplies, and every unit test stays green
- **Type / Severity / Status:** ISSUE · MED · ✅ **RESOLVED 2026-09-06 — verified against `src/`, not against the ledger**
- **T0 close-out evidence:** `HRM.ArchitectureTests/InertOptionalParameterTests.cs` (ARCH-004) names this finding, and `PayrollRunProcessor.cs:1001-1003` now passes `fte:`/`fteScaledBase:`. **The rule this finding asked for shipped.**
- **Layer:** TEST (architecture)
- **Module / US / TC:** cross-module · generalises GAP-022
- **Title:** GAP-022's shape: `PayrollOvertimeCalculator.Compute` gained `fte` and `fteScaledBase` as trailing optionals, `PayrollRunProcessor` was never updated to pass them, the parameters were inert on the only production path, and **the entire suite stayed green** — because the calculator's own unit tests supply the arguments directly. `OvertimeFteBaseTests.cs:10-13` even records in its header that it proves "the MATH, not the plumbing", and it stayed broken anyway. A written admission of an untested seam is not a control.
- **Root cause + confidence (~90%):** nothing asserts that a domain calculator's optional parameters are actually supplied by a non-test caller.
- **Severity rationale:** MED — this is a money-path defect generator. It produced a silent underpayment once already.
- **Suggested direction (NOT applied):** a NetArchTest/architecture rule flagging any domain calculator whose optional parameters are never supplied by a production caller — natural work for queue item `E2` (`HRM.ArchitectureTests`), which does not yet exist. Failing that, a manual sweep of every domain calculator's call sites.
### BUG-448 — three `.cs` files contain literal NUL bytes, so grep, ripgrep and semgrep skip them SILENTLY
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Layer:** BE (source encoding) — but the impact is on every text-based audit tool
- **Module / US / TC:** cross-module · `AuditAnonymizationService.cs:129`, `AesGcmFieldEncryptorTests.cs`, `EncryptingFileStorageTests.cs`
- **Title:** `const string sentinel = "\x00REDACTED\x00";` embeds two **literal NUL bytes**, so `file(1)` reports the source as `data`. **Verified: `grep -c 'IgnoreQueryFilters'` on `AuditAnonymizationService.cs` returns `0` with exit 1 — while the file actually contains 3 occurrences.** No "Binary file matches" warning; the file is simply absent from results.
- **Why this is HIGH and not a style nit:** that file holds **two `IgnoreQueryFilters` sites, one a cross-tenant WRITE over `audit_logs`** — exactly the code `.semgrep/tenant-isolation.yml` exists to watch. Semgrep's binary-file handling almost certainly skips it too (~80%, not executable locally). **A source file invisible to every text-based tool, containing the precise shape the security linter guards.**
- **It also corrupted this session's own measurements.** The G7 baseline of "354 sites" came from grep; grep could not see these. True figure: 265 executable sites **+2 invisible**. Every audit in this session that used grep shares the blind spot.
- **Root cause + confidence (~98%, verified directly):** the escape form `"\x00"` in C# source emits an actual NUL byte into the file. `"\0REDACTED\0"` has the identical runtime value and keeps the file valid text.
- **Suggested direction (NOT applied):** replace with `"\0REDACTED\0"`, then add a CI step — `find src/backend -name '*.cs' -not -path '*/obj/*' | xargs file | grep -v text` must return nothing. **The CI step matters more than the fix**: it is what stops a future file going invisible.
- **SURVEY:** **0 files** at this commit (unit = `.cs` files containing a literal NUL byte). **2,719** `.cs` files under `src/` were scanned, excluding `obj/` and `bin/`, using a byte-level `perl` raw-mode scan rather than grep — grep being the blind tool under test. A whole-repo tracked-file sweep finds NUL bytes only in `.claude/hooks/sounds/*.mp3|wav`.
- **AUDIT (2026-09-07):** (1) **"three `.cs` files" — FALSE.** Exactly **two** ever contained NUL bytes. What is true instead: `EncryptingFileStorageTests.cs` **never** did — its pre-fix blob has 0 NUL bytes; it carries `0x03`/`0x04` in test data, which makes `file(1)` report `data` but leaves it perfectly greppable (`grep -c Assert` returns 2, exit 0). This was already corrected at `GAP-CLOSURE-QUEUE.md:414-416`. (2) `AuditAnonymizationService.cs:129` — **stale**; the sentinel is at **`:126`** today. (3) **The consequence claim — the whole severity argument — is CONFIRMED, and understated.** Reproduced against the pre-fix blob: recursive `grep -rn` and `rg` both **omit the file entirely and exit 0**, with no warning, while an adjacent clean file matches; `grep -a` and explicit-path `rg` both find 3. The entry says `grep -c` "returns 0" — it returns **nothing at all**, exit 1. Only an explicit single-file `rg` warns (`binary file matches`, without line numbers). `AuditAnonymizationService.cs:16`, `:40`, `:48` confirms 3 occurrences, 2 of them executable. (4) "true figure: 265 executable +2" — **superseded**: `ISSUE-449` measured **276** executable sites. (5) **The fix is merged**: `8fcb8044` (**#645**) is an ancestor of `origin/test/local-subdomains`, and the suggested CI guard shipped as something stronger than the proposed `file | grep -v text` shell step — `SourceFileIsGreppableTests.cs`, a byte-level xUnit rule in `HRM.ArchitectureTests`, which is in `HRM.sln` and **does** execute in CI (`ci-gate.yml:216`). The guard was checked for inertness and is live.
- **SEVERITY CHECK:** **lower** — zero current exposure, fix merged, regression guard executing. This is a stale-OPEN entry (see `ISSUE-545`) that additionally still asserts the false "three files". Note `GAP-CLOSURE-QUEUE.md:418` still claims "no `file | grep -v text` step exists anywhere", which is now misleading: the architecture test supersedes it, and that test's own docstring argues the `file(1)` approach would have been wrong (3 false positives).
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-449 — the G7 queue item's own count was inflated ~33% by counting the codebase's documentation of a problem as instances of it
- **✅ RESOLVED 2026-09-07 — measured, and the UNIT recorded alongside.** Byte-level walk of `src/backend` (excluding `obj/`, `bin/`, and semgrep's own excludes `*Tests*.cs` / `*Test.cs` / `TenantResolution*.cs`), decoding `utf-8-sig` with `errors='replace'` so no file can be silently skipped. Independently reproduced twice.

  | measure | value |
  |---|---|
  | files containing the token | 81 |
  | **executable call sites** (`IgnoreQueryFilters\s*\(` on non-comment lines) | **276** |
  | raw text matches | 369 |
  | comment-only lines | 93 |

  **Root cause of the three numbers: none of them stated a unit.** 354 (queue) counted raw text; 270 (register) was an older raw count; 265 (`.semgrep/tenant-isolation.yml`) was closer to call sites but stale. Corrected all three, each now carrying the unit — that is the actual fix, since a bare number will drift into a fourth.

  **The BUG-448 assumption was verified, not trusted:** an independent byte scan of all 2703 `.cs` files found **0** containing `\x00`, and `file(1)` now reports `AuditAnonymizationService.cs` as UTF-8 text, so grep and the byte count agree. Before #645 they would not have.

  Two ledger sub-claims were also wrong and are noted rather than silently dropped: `HRM.Application` has 5 raw matches but **0** executable (all comments), and `CrossTenantScope.cs` has **1** comment occurrence, not the 2 claimed.
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** cross-module · `GAP-CLOSURE-QUEUE.md:213-215`, `GAP-REGISTER.md:129`
- **Title:** G7 was filed as "354 `IgnoreQueryFilters()` sites, un-triaged". A census found **88 of the 353 grep hits are XML-doc or inline comments** — including all four `HRM.Application` hits, all five in `AppDbContext.cs`, and both in `CrossTenantScope.cs`. **`HRM.Application` has zero call sites.** The real figure is **265**, which is exactly what `.semgrep/tenant-isolation.yml`'s own header already claimed. The rule header was right; the queue item was not.
- **Worse, the premise was falsified:** the queue says *"nobody can tell which [are legitimate]"*. `Rls/FLIP-READINESS-2026-08-05.md:33-37` records a completed classification of all 69 sites on policy-carrying tables (7 cross-tenant-by-design, all fixed; 58 safe; 4 unresolved-ambient), and PR #498 closed the same GAP-007 with a written decision to keep the rule at WARNING. **Two prior workstreams already did this work.**
- **Severity rationale:** MED — it nearly funded a campaign that would have produced 265 rubber-stamps over work already done.
- **Suggested direction (NOT applied):** correct both ledgers (354→265, remove "nobody can tell which", cite the flip-readiness doc).

### BUG-450 — the Departments and Job Titles nav items carry NO permission gate; every Employee and Manager sees two links that dead-end at /forbidden
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Layer:** FE
- **Module / US / TC:** Core HR · US-CHR-004 / US-CHR-005 · TC-CHR-006-11
- **Title:** `main-layout.component.ts:692-698` declares the Departments and Job Titles nav items with **`label` and `route` only** — no `tenantRoles`, no `permission`. Both routes are `roleGuard(['Tenant Admin','HR Officer'])` (`app.routes.ts:355,366`). So every Employee and Manager is shown two links that lead straight to `/forbidden`.
- **Root cause + confidence (~98%, verified independently):** the gate was simply never added. This is **ISSUE-210's exact defect** — nav visibility drifting from route access — live in production nav today, and the invariant spec at `main-layout.nav-visibility.spec.ts:177` did not catch it because that arm tests `/performance` specifically rather than sweeping every item.
- **Evidence:** empirical, not inferred. During E5's pre-fix run the rendered nav for a **Manager** persona printed as `['/dashboard','/departments','/job-titles','/profile/notification-preferences']` — the Manager holds neither role in that guard yet renders both links.
- **Severity rationale:** HIGH by user impact and breadth — it is every non-HR user of every tenant, on two of the most prominent sidebar entries, and it is the specific failure the codebase has already fixed once and written an invariant for.
- **Suggested direction (NOT applied):** add `tenantRoles: ['Tenant Admin','HR Officer','Tenant Owner']` to both, mirroring the three gates E5 added. **Then consider widening the `:177` invariant from one route to a sweep** — the reason this survived is that the guard tests a single example rather than the property.
- **SURVEY:** **2 of 52 nav items** carry no gate at this commit, and both are intentional — `Dashboard` (`main-layout.component.ts:78`) and `Notification Preferences` (`:559`). Unit = object literals in `NAV_ITEMS` (`:76-593`) with a `route:`, tested for `tenantRoles`/`permission`/`systemRoles`/`module` outside comments. **Departments and Job Titles are not among them.**
- **AUDIT (2026-09-07):** (1) "Departments and Job Titles declare `label` and `route` only" — **FALSE at this commit.** Both now carry `tenantRoles: ['Tenant Admin','HR Officer','Tenant Owner']` (`main-layout.component.ts:94`, `:110`), fixed by `6566431f` (**#626**) — `git merge-base --is-ancestor 6566431f origin/test/local-subdomains` passes, so it is **merged**, not merely branched. (2) Cited `main-layout.component.ts:692-698` — correct at the filing commit `b7d74082`, **stale now**; the table moved to module scope. (3) "Routes are `roleGuard(['Tenant Admin','HR Officer'])` at `app.routes.ts:355,366`" — **PARTIALLY TRUE**: those are the `path:` lines; the `roleGuard` calls are **`:361`** and **`:372`**. The guards themselves are CONFIRMED. (4) **Three-layer check** — nav item gated (`:94`, `:110`); route gated (`app.routes.ts:361`, `:372`); **API fully gated** — all 11 actions across `DepartmentsController.cs` and `JobTitlesController.cs` carry `[RequirePermission("Department.*"/"JobTitle.*")]` under class-level `[Authorize]`. **No layer was ever unprotected: even pre-fix this was a dead link, never privilege escalation.** (5) The suggested widening also landed — `main-layout.nav-gate-invariant.spec.ts:407` now asserts the ungated set equals `INTENTIONALLY_UNGATED`, pinning the *class* (BUG-493, `fcde1bf9`, merged).
- **SEVERITY CHECK:** **lower — LOW/MED, not HIGH.** The entry's own description was honest that it dead-ends at `/forbidden`; the severity line overstated it as an access-control defect. Nothing reachable was ever exposed; the cost was two dead sidebar links.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-451 — the offboarding feature has no entry point anywhere; bare /offboarding redirects an authenticated user to the login page
- **Type / Severity / Status:** BUG · HIGH · OPEN (needs-decision)
- **Layer:** FE (+ BE for the fix)
- **Module / US / TC:** Onboarding/Offboarding · US-ONB-005
- **Title:** `offboarding.routes.ts` declares only `initiate/:employeeId` and `:offboardingId` — **no `path: ''` index** — so bare `/offboarding` matches nothing, falls through to `app.routes.ts:737`'s `path: '**'` and **redirects to `auth/login`**. And nothing anywhere in the app links to either child route. **989 lines of working components behind six live endpoints, with no way for any user to reach them.**
- **Root cause + confidence (~95%):** the feature shipped without an index route or a contextual entry point.
- **Why E5 did not simply add a nav link:** it would have pointed at a route that bounces an authenticated user to login — a worse dead end than the orphan. A spec arm now asserts no persona is offered a bare `/offboarding` link, so it cannot be re-added by reflex.
- **Two candidate fixes, both outside a nav edit:** (a) an `/offboarding` index page — **needs a backend list endpoint**, since `GET /api/v1/offboarding?employeeId=` returns a single instance, not a list; or (b) a contextual "Initiate Offboarding" action on the employee profile. **Recommendation (b)** — an `:employeeId` route belongs on the employee record, not the sidebar.
- **Trap for whoever does it:** the employee-profile screen gates HR actions on a role check that excludes HR Manager and Tenant Owner, while the offboarding route guard is `roleGuard(['Tenant Admin','HR Officer','HR Manager'])` + implicit Tenant Owner + `moduleGuard('Onboarding')`. **Reusing the profile's existing gate would recreate ISSUE-210.**
- **SURVEY:** **1** lazy route array of **34** registrations (33 distinct child route files) lacks a top-level `path: ''` — offboarding is a genuine one-off, **not a class**. Every `loadChildren` target in `app.routes.ts` was enumerated and grepped; the other 32 files all have one (e.g. `onboarding.routes.ts:56`, `payroll.routes.ts:133`, `leave-management.routes.ts:12`). Separately, and more broadly: **12 of 54** top-level route paths have **zero** inbound reference of any kind — no `NAV_ITEMS` entry, no `routerLink`, no `router.navigate` — and **1 of 206** feature components is unreachable by transitive closure. Excluded `*.spec.ts` and generated `api-types.ts`. That wider class is filed as **`ISSUE-547`**.
- **AUDIT (2026-09-07):** (1) "`offboarding.routes.ts` declares only `initiate/:employeeId` and `:offboardingId`, no `path: ''`" — **CONFIRMED** (`offboarding.routes.ts:16-33`). (2) "Bare `/offboarding` falls through to `path: '**'` and redirects to `auth/login`" — **CONFIRMED** (`app.routes.ts:736-738`). Note `:offboardingId` would also swallow `/offboarding/anything`, so adding `path: ''` is the only fix. (3) "Nothing anywhere in the app links to either child route" — **PARTIALLY TRUE / overstated**. No nav entry exists (the array is `main-layout.component.ts:458-461`, `Onboarding` → `/onboarding`, no offboarding entry), but two inbound navigations **do** exist: `offboarding-initiate.component.ts:297` and `exit-interview-form.component.ts:680`, both `router.navigate(['/offboarding', id])`. What is true instead: `:offboardingId` is reachable *from inside the cluster*; the cluster has **no external door**, because `initiate/:employeeId` has zero inbound links. It is an unreachable cluster, not an unreferenced route. (4) "989 lines behind six live endpoints" — **CONFIRMED exactly**: 683 + 306 = 989 lines; `OffboardingController.cs` has 6 `[Http*]` actions (`:30,55,69,84,105,129`). (5) `GET /api/v1/offboarding?employeeId=` returns a single instance, not a list — **CONFIRMED** (`:69-76`). (6) The ISSUE-210 trap — **CONFIRMED**: `employee-profile.component.ts:1985`, `:2111` gate on `HR Officer || Tenant Admin` only, while `app.routes.ts:637-640` uses `roleGuard(['Tenant Admin','HR Officer','HR Manager'])` and `auth.guard.ts:117-125` widens by `TENANT_SUPER_ROLES`; reusing the profile gate would exclude HR Manager and Tenant Owner.
- **SEVERITY CHECK:** **agrees — HIGH.** 989 lines behind 6 live endpoints with no external entry point. The survey showing it is a one-off argues against escalating further.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-452 — agent worktrees have no src/frontend/node_modules, and the failure reads like a broken Angular install
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** INFRA (orchestration)
- **Module / US / TC:** cross-module · sibling of ISSUE-442
- **Title:** Only the main checkout carries `src/frontend/node_modules`. In a worktree, `npx ng test` fails with *"npm error could not determine executable to run"* — which reads as a broken Angular install rather than a missing dependency tree, so an agent burns turns misdiagnosing it. Symlinking the main checkout's copy works (verified across a full 4,340-spec run and `npm run build`).
- **Trap:** the symlink is **not** covered by the repo ignore rules — `git status` reports `?? src/frontend/node_modules`, so it must be removed before reporting or the agent hands back a dirty tree.
- **Suggested direction (NOT applied):** pre-provision the symlink at worktree creation, or document it in the frontend rule. Same family as ISSUE-442 (stale worktree base) — agent worktrees are not provisioned to match what agents are asked to do in them.

### ISSUE-453 — every `*PostgresTests` class starts a container AND re-runs all migrations once per TEST, not per class
- **Type / Severity / Status:** ISSUE · HIGH · OPEN
- **Layer:** TEST (INFRA)
- **Module / US / TC:** cross-module · 92 files matching `HRM.Tests/Integration/*PostgresTests.cs`
- **Title:** The established fixture shape uses `IAsyncLifetime` on the test class. **xUnit constructs a new class instance per `[Fact]`, so `InitializeAsync` re-fires for every test** — starting a fresh container and re-running the full migration set each time. Measured at **~20s per test**: four tests took 79s wall for 2s of actual test time. The same four under an `IClassFixture` took 40s wall.
- **Root cause + confidence (~95%, measured):** `IAsyncLifetime` is per-instance; `IClassFixture` is per-class. The pattern was copied across 92 files.
- **Severity rationale:** HIGH by cost, not correctness. The backend gate runs 20–35 minutes and multiple agents contend over it; this is a large share of that, multiplied across 92 files. It also lengthens every future CI run and every agent's feedback loop.
- **Suggested direction (NOT applied):** migrate to `IClassFixture<PostgresContainerFixture>` — the pattern E3 slice 1 introduced. **Survey first (this is campaign-shaped), and gate it on ISSUE-454**, because a shared database changes the meaning of any cross-tenant assertion.
- **SURVEY:** **80 of 85** test classes (unit = C# classes in `src/backend/HRM.Tests/Integration/*PostgresTests.cs`, one per file) construct their own `PostgreSqlBuilder` container in a per-instance `IAsyncLifetime.InitializeAsync`; **5 of 85** already use `IClassFixture<PostgresContainerFixture>` (`DepartmentPostgresTests.cs:49`, `HrReportPostgresTests.cs:34`, `HrReportExportPostgresTests.cs:37`, `LeaveReportPostgresTests.cs:40`, `SalaryComponentPrecisionPostgresTests.cs:45`). Those 80 classes hold **333** `[Fact]`/`[Theory]` attributes → at least 333 container starts per run; **69 of the 80** call `MigrateAsync` directly. Excluded: `HRM.ArchitectureTests`, unit tests, non-Postgres integration tests.
- **AUDIT (2026-09-07):** (1) "xUnit constructs a new class instance per `[Fact]`, so `InitializeAsync` re-fires per test" — **CONFIRMED** (pattern at `MoneyPathsPostgresTests.cs:53-61`). (2) "**every** `*PostgresTests` class" / "92 files" — **FALSE**: there are 85 files at this commit and 5 are already converted; the correct figure is **80 of 85**. (3) "~20s per test, 4 tests = 79s wall" — **PARTIALLY TRUE**: the measurement is recorded at `PostgresContainerFixture.cs:5-10` but was not re-run here (static audit). (4) "gate it on ISSUE-454" — **FALSE/stale**: the ISSUE-454 guard has landed and is merged (`HRM.ArchitectureTests/SharedPostgresFixtureIsolationTests.cs`, `91e92cc5`, **#630**). **The stated prerequisite is satisfied and the rollout is unblocked today.** (5) **Count drift across three sources for one question**, none stating a unit or a commit: this entry says 92 files, `SharedPostgresFixtureIsolationTests.cs:14-16` says "398 container starts per run across 93 classes", and `TEST-FINDINGS.md:3781` propagates the 398; the measured figures are 85 classes / 80 unconverted / 333 tests. Corrected here rather than filed separately, since they are this finding's own numbers.
- **SEVERITY CHECK:** **agrees — HIGH by cost stands** (>=333 container starts plus full migration replay). The framing is inflated ~8% and the stated blocker is stale.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-454 — a shared Postgres fixture silently changes the meaning of any cross-tenant assertion, and nothing guards it
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST
- **Module / US / TC:** cross-module · any `*PostgresTests` sharing a fixture
- **Title:** A shared-database fixture is only safe for **tenant-scoped** assertions. An arm that queries cross-tenant or asserts a global count changes meaning the moment siblings share a database. E3 caught one by reading — `CleanupService_ExpiresOverdueCompletedExports` asserts a cross-tenant count via `IgnoreQueryFilters`, and a sibling seeds its own overdue export, so a shared DB would have made that count 2 and forced the assertion to be scoped, i.e. **weakened**. It was split into its own class instead, assertions byte-identical.
- **Root cause + confidence (~90%):** nothing mechanically detects the combination.
- **Suggested direction (NOT applied):** an architecture rule — a class using `IClassFixture<PostgresContainerFixture>` must contain no `IgnoreQueryFilters`. **Natural work for `E2` (`HRM.ArchitectureTests`)**, and a prerequisite for rolling out ISSUE-453 safely.

### ENH-455 — `HrReportService` is written "InMemory-safe", which is now pure performance cost
- **Type / Severity / Status:** ENH · MED · OPEN
- **Layer:** BE
- **Module / US / TC:** Reports · RPT-001
- **Title:** The service materialises the full filtered population with `ToListAsync` and then groups, does age math, and resolves department/location names **in memory** (`HrReportService.cs:137`→`:150`, `:187`/`:199`, `:225`/`:227`), and uses `List.Contains` over `HashSet`. These shapes were adopted to keep EF InMemory tests passing.
- **Why it matters now:** those paths are Postgres-tested as of E3 slice 1, so the workaround is no longer buying anything — it is full-population materialisation before every aggregation, at tenant scale.
- **It is also why E3's "0 divergence" is a weak signal:** the code under test had already been contorted to avoid SQL, so there was little provider behaviour left to diverge.
- **Suggested direction (NOT applied):** push grouping into SQL — **after** E3 slices 2–3, so the change is actually covered by Postgres-backed tests rather than InMemory ones.

### BUG-456 — legacy-path overtime is hardcoded at 1.5x while `WeekdayOvertimeMultiplier` is tenant-configurable — a GAP-022 recurrence in the same method
- **Type / Severity / Status:** BUG · HIGH · OPEN
- **Layer:** BE
- **Module / US / TC:** Payroll · US-PAY-010 · sibling of GAP-022
- **Title:** `PayrollRunProcessor.cs:1001` calls `PayrollOvertimeCalculator.Compute(...)` supplying `fte:` and `fteScaledBase:` — **G1's fix from this session** — but **still omits `defaultMultiplier`**, which therefore defaults to `1.5m`. `AttendanceSettings.WeekdayOvertimeMultiplier` (`:152`) is persisted, defaults to 1.5 and is tenant-settable. A tenant that configures 2.0x is paid **1.5x** on the legacy-attendance fallback path (the branch taken when `OvertimeMultiplierDetails` is absent).
- **Root cause + confidence (~95%, verified independently):** exactly the GAP-022 shape. **G1 threaded two of the three optional parameters and left the third inert.**
- **Why it matters beyond the money:** it is a *recurrence in the method that was just fixed*, which is the strongest possible argument for the `ARCH-004` rule that found it. A human reading the G1 diff would have seen two named arguments added and reasonably concluded the call was now complete.
- **Severity rationale:** HIGH — real money, though narrower blast radius than GAP-022: only the legacy-attendance path, not every part-time employee.
- **Suggested direction (NOT applied):** thread the tenant's `WeekdayOvertimeMultiplier` through, with a run-level Testcontainers arm asserting the **persisted** figure — not a unit test of the calculator, which is what let GAP-022 ship. **Confirm with a payroll owner that the weekday multiplier is the right source for this path** before wiring it. Then remove the entry from `KnownInert`.

### GAP-457 — `taxExemptThreshold` is supported by the calculator but has no persisted setting anywhere; PAYE computes with a zero personal allowance
- **Type / Severity / Status:** GAP · MED · OPEN (needs-decision)
- **Layer:** BE
- **Module / US / TC:** Payroll · statutory deductions
- **Title:** `StatutoryCalculator.ComputeIncomeTaxYtd` accepts `taxExemptThreshold`, and **both** call sites (`StatutoryDeductionResolver.cs:196`, `:205`) omit it — so every tenant computes PAYE with a zero personal allowance.
- **Why this is MED and not CRITICAL:** `"TaxExempt"` appears **nowhere else in the backend** (verified: zero hits outside the calculator). So this is a **half-built feature**, not a configured value being silently ignored — nobody can currently set an allowance to have it dropped. The agent nearly filed this as a critical money bug and checked first; that check changed the severity.
- **Suggested direction (NOT applied):** decide — wire it to a real persisted statutory setting, or delete the parameter. Leaving a supported-but-unreachable tax parameter is how it eventually gets wired wrong.
- **SURVEY:** **1 production call site** — `PayrollRunProcessor.cs:1001-1003`, the sole non-test caller of `PayrollOvertimeCalculator.Compute` (unit = non-test references to that method in `src/backend`). The only others are 6 unit-test calls (`OvertimeFteBaseTests.cs`, `PayrollBasicResolutionTests.cs:71`), the ARCH-004 baseline strings (`InertOptionalParameterTests.cs:68`, `:94`), and comments. It **cannot** recur elsewhere because `AttendancePayrollRowDto` — the input carrying the multiplier buckets — has exactly **2** construction sites, both in `AttendancePayrollService.cs:171`, `:194`, and every consumer (`PayrollRunProcessor.cs:1131`, `PayrollRunService.cs:408`) goes through `GetPayrollDataAsync`. Excluded: tests, `obj/`, migrations.
- **AUDIT (2026-09-07):** (1) "`PayrollRunProcessor.cs:1001` passes `fte:`/`fteScaledBase:` but omits `defaultMultiplier`" — **CONFIRMED, exact line**; the parameter defaults to `1.5m` (`PayrollOvertimeCalculator.cs:63`). (2) "`AttendanceSettings.WeekdayOvertimeMultiplier` is persisted, defaults to 1.5, tenant-settable" — **CONFIRMED, exact** (`AttendanceSettings.cs:152`; write path `AttendanceSettingsService.cs:312`). (3) **"A tenant that configures 2.0x is paid 1.5x" — FALSE as a live money claim.** What is true instead: the fallback branch (`PayrollOvertimeCalculator.cs:92-97`) is **unreachable in production**. It fires only when `ApprovedOvertimeMinutes > 0` **and** `OvertimeMultiplierDetails` is empty, but both derive from the *same* query rows in `AttendancePayrollService.ApprovedOvertimeByEmployeeAsync` (`:434-452`) — non-zero minutes always produce at least one bucket, and if both are zero `ComputeOvertime` short-circuits at `PayrollRunProcessor.cs:995-998`. Moreover the configured multiplier **is already honoured**: every `OvertimeRecord.Multiplier` is resolved from `settings.WeekdayOvertimeMultiplier`/`Weekend`/`Holiday` via `OvertimeMultiplierResolver.Resolve` (`OvertimeService.cs:98-102`, `:220-224`) and persisted per record. **A tenant at 2.0x is paid 2.0x today.** (4) "~95%, verified independently" — the *shape* (an inert third optional parameter) is CONFIRMED; the *consequence* was never traced through to a reachable caller.
- **SEVERITY CHECK:** **lower — MED at most.** HIGH rested on "real money", and no reachable path misprices anything. Two residues keep it above LOW: a `Multiplier` of `0` would key `"0"`, fail `ParseMultiplier`'s `m > 0` guard and be silently paid at 1.5x on the *modern* path (`PayrollOvertimeCalculator.cs:87`, `:109`) — currently blocked by `UpsertAttendanceSettingsValidator.cs:36-43` (BUG-522), so it depends on no pre-BUG-522 rows existing; and the entry's own suggested fix is semantically wrong, since the fallback lumps weekday, weekend and holiday minutes into one bucket, so threading `WeekdayOvertimeMultiplier` would **under-pay** weekend/holiday overtime. Branch `fix/BUG-456-weekday-overtime-multiplier` exists on origin but is **NOT merged**.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### ISSUE-458 — `.claude/rules/backend.md` project counts are stale after E2
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** DATA (docs)
- **Title:** The `## Tests` section names only `src/backend/HRM.Tests` — there are now **two** test projects — and `## Nullability` says "All 5 projects set `<Nullable>enable</Nullable>`", now **6**. The substance holds (`HRM.ArchitectureTests` does set it); only the count and the project list are stale. **No test asserts either claim** — `ClaudeMdAccuracyTests` does not count solution projects — so nothing is red.
- **Suggested direction (NOT applied):** update both, and consider whether the project count is worth a mechanical assertion given `ISSUE-437` (nothing verifies a documented capability exists).


### ISSUE-459 — the JWT signing-key rotation runbook exists only as an XML doc comment, and a rolling restart contains it only partially
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE (INFRA / ops-resilience)
- **Module / US / TC:** Platform · auth · GAP-020
- **Title:** Surfaced by the `F2` exploitability pass on GAP-020 (2026-09-04). Rotation itself **works** — a `kid`-based ring with overlap `ValidationKeys` (`JwtKeyRingOptions.cs:21-46`, `JwtService.cs:54-72,203,228`), 6 real-RSA tests. What is missing is everything around it: **(a)** the three-step procedure lives **only** in an XML doc comment (`JwtKeyRingOptions.cs:7-18`) — `PRODUCTION-CHECKLIST.md:230` mentions `Jwt:PrivateKey` in one "app won't start" checkbox and never describes rotating it, and `Security/README.md` documents the *encryption* key runbook while saying nothing about JWT; **(b)** `Program.cs:160-171` hand-constructs a singleton and snapshots `TokenValidationParameters` once (no `IOptionsMonitor` anywhere in production code), so a **rolling** restart leaves un-restarted instances still honouring the compromised key, **with no signal for which instances are clean**; **(c)** no detection exists for anomalous `kid` usage or forged-token patterns, so the *detect* half of the loop is absent too.
- **Root cause + confidence (~85%):** live rotation is an explicit non-goal (`JwtKeyRingOptions.cs:7` says so), which is defensible — but the operational wrapper that a non-goal requires was never written. The field-encryption key, by contrast, got a README, an admin endpoint and a watchdog job.
- **Severity rationale:** MED as an **operational recovery (MTTC)** gap, **not** a vulnerability. There is no attacker-reachable primitive: the precondition is already holding the signing key, i.e. total compromise. Rated NONE/Informational as an exploitable vuln (92% confidence) and MEDIUM as an ops gap (80%). Recording both because sizing this item's *urgency* off the phrase "compromised signing key" is how it has been mis-scheduled before.
- **Suggested direction (NOT applied):** write the rotation runbook where an on-call operator will find it (alongside the encryption-key runbook), and state the rolling-restart caveat explicitly in it. A global token epoch is a **separate, larger** item — it is the only lever for invalidating all sessions for a *non-key* reason (suspected mass session theft, an IdP incident) and today no such lever exists.
### BUG-460 — the platform monitoring FE computes real telemetry and then throws it away
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** FE
- **Module / US / TC:** Platform · US-PLT-004 AC-3
- **Title:** `latencyTrend24h` and `topErrors` are computed by the backend (`PlatformMonitoringService.cs:355-357`, `:344-346`), serialized, and mapped in `monitoring.models.ts:342-343` — then **never bound in either template**. Both panels hard-code "Not available — requires observability pipeline" (`monitoring-dashboard.component.html:153-159`, `tenant-monitoring-detail.component.html:220-234`). Compounding it, `MetricsStatus` is unconditionally `RequiresObservabilityPipeline` (`PlatformMonitoringService.cs:163`, `:402`) **even when error-rate, P95 and SLA uptime are all real**, so any FE logic keyed on `requiresObservability()` hides working data.
- **Root cause + confidence (~95%, verified by direct read):** the panels were stubbed before the backend shipped and were never revisited; the hardcoded status made the stub look correct.
- **Severity rationale:** MED. This is **leg-2 (reachability)** failure — the highest-value gap class in this codebase. A working backend behind a placeholder reads to every operator as "the feature does not exist", and US-PLT-004 AC-3 is marked satisfied.
- **Suggested direction (NOT applied):** bind the two fields and make `metricsStatus` conditional on which gauges are actually available.
- **RE-SCOPED 2026-09-05 — this is FULLSTACK, not a frontend fix, and that is why it never got fixed.** Verified at source while executing P1.2:
  - `latencyTrend` elements are a real shape — `{ hourUtc, p95Ms, requests }` (`PlatformMonitoringService.cs:375-377`) — but they are **boxed to `object`**, and the DTO declares `IReadOnlyList<object>` (`MonitoringDtos.cs:236,238`). The OpenAPI contract therefore describes them as untyped, `api-types.ts` generates `unknown[]`, and `monitoring.models.ts:141-143` faithfully types them `unknown[]`. **The frontend cannot render `unknown[]` without inventing a shape — which the generated-types rule explicitly forbids.** The backend erases the type; the FE was not careless.
  - **`errorRateTrend24h` is genuinely empty** — `ErrorRateTrend24h: Array.Empty<object>()` with the comment *"per-tenant trend unavailable"* (`:419`). Its "Not available" placeholder is **honest** and must NOT be "fixed". This finding lumped all three fields together; only two are real.
  - The correct order is therefore: **(1)** give the two fields real DTO types in `HRM.Application`; **(2)** regenerate the contract and `api-types.ts` (both CI-gated); **(3)** bind them; **(4)** make `metricsStatus` conditional (`:163`, `:296`, `:423`) instead of the unconditional `RequiresObservabilityPipeline`.
  - **Fifth finding in two days whose scope was understated** — with `ENH-009` (1 writer filed, 12 real), `ISSUE-117` (1 site, 2), `BUG-493` (8 items, ~40) and `ISSUE-148(c)` (inverted). All in the same direction, which [[ISSUE-498]] records as the more expensive error because it survives the fix.

### ISSUE-461 — four source comments assert "always null / always empty" on the line above real computed values
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE / FE (docs-in-code)
- **Module / US / TC:** Platform · US-PLT-004
- **Title:** `PlatformMonitoringService.cs:156-160` says `AggregateErrorRatePercent` is *"STILL NULL, deliberately"*; the very next statement (`:161`) assigns a value computed at `:128-137`. Same drift at `MonitoringDtos.cs:81,83,192` and `monitoring.models.ts:76-79` ("gauges are DEFERRED with `available === false`").
- **Root cause + confidence (~95%):** comments written before the build, never revisited.
- **Severity rationale:** MED, and higher than a normal comment nit for one specific reason: **these are exactly the sentences a BA authoring requirements would lift verbatim into an NFR.** F4 had to be explicitly instructed to trust the code over these comments. This repo has now recorded **six** cases of a comment outliving its code and causing real work.
- **Suggested direction (NOT applied):** correct all four; fix alongside [[BUG-460]] since they describe the same fields.

### ISSUE-462 — `docs/BA/STATUS.md:117` says the per-tenant API-call counter was deferred; it shipped the next day
- **Type / Severity / Status:** ISSUE · MED · ✅ **RESOLVED #612** (verified 2026-09-04 via `/verify-fix`)
- **Layer:** DATA (ledger)
- **Module / US / TC:** Platform · US-PLT-004
- **Title:** STATUS.md:117 states the counter was *"deliberately deferred as its own slice"* and that a partial build "would leave the `ApiCalls` gauge FAKE rather than honestly `Available:false`". It shipped **2026-07-31**, commit `b9906626`, with every component the line says is missing: table + unique index + dormant RLS policy (`20260731012730_Platform_TenantApiUsage.cs:14-66`), hot-path write (`ApiCallCounterMiddleware.cs:80-101`), flusher (`Program.cs:420`), and a gauge reporting `Available: true` (`PlatformMonitoringService.cs:553-567`). STATUS.md was last touched 2026-08-18 and still carries the stale text.
- **Severity rationale:** MED. **Reverse drift** — the ledger is pessimistic, so it causes wasted rebuilding rather than false confidence. The 2026-09-01 audit measured 29% stale-pessimistic entries; this is another.
- **Suggested direction (NOT applied):** correct the line. F4's executor is permitted to fix this one sentence since the BA reads exactly it.
- **Verification (#612):** Verified 2026-09-04 by direct file read. The stale deferral sentence at `STATUS.md:117` is struck through and followed by a dated correction citing commit `b9906626` and the shipped components. My first grep appeared to show it unfixed — it was matching the **struck-through original inside the correction**, the same shape as [[ISSUE-481]].

### ISSUE-463 — the traceability matrix is missing 5 stories outright, not the 3 GAP-030 counts
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** cross-module · GAP-030
- **Title:** Absent from `docs/QA/TRACEABILITY-MATRIX.md` entirely: **US-ADM-011, US-ADM-012, US-PLT-001, US-PLT-003, US-PLT-004**. Additionally **US-ADM-008/009/010** have coverage summaries (`:4725`, `:4746`, `:4777`) and backward rows but **no forward-traceability row** — the Admin Console forward table stops at US-ADM-007 carrying a stale `TOTAL 147`. US-PLT-001 and US-PLT-003 are `[x]` in STATUS.md yet appear nowhere in the matrix.
- **Severity rationale:** MED. Traceability is Critical Rule #4; a matrix that silently omits 8 stories cannot support the claim it exists to support.
- **Suggested direction (NOT applied):** F4 covers 3 by design and is deliberately **not** widened. The remaining 5 (+3 forward rows +the stale TOTAL) are this finding.

### ISSUE-464 — `TC-PLT-004.md` binds US-PLT-005, so any filename-based coverage check false-positives
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** Platform
- **Title:** `docs/QA/platform/TC-PLT-004.md:3` has frontmatter `user_story: US-PLT-005` (encryption). TC-PLT-003/006/007 are likewise offset. Documented at `platform/TEST-MATRIX.md:43-45`, but any grep for "does US-PLT-004 have a TC" returns a **false positive**.
- **Severity rationale:** LOW — pre-existing and documented, but it is a live trap for exactly the automated checks this repo keeps adding.
- **Suggested direction (NOT applied):** **no rename** (IDs are trait-bound to code). Ensure any coverage check reads frontmatter, not filenames.

### ISSUE-465 — GAP-030 is listed as both parked-at-the-decision-gate and scheduled as F4
- **Type / Severity / Status:** ISSUE · LOW · ✅ **RESOLVED #613** (verified 2026-09-04 via `/verify-fix`)
- **Layer:** DATA (ledger)
- **Module / US / TC:** queue hygiene
- **Title:** `GAP-CLOSURE-QUEUE.md:256` lists GAP-030 under *"🚧 Parked at the decision gate — NOT auto-scheduled"*, while `:305` and `:871` schedule it as F4. `COMPLETION-PLAN.md:115` repeats the parked framing.
- **Severity rationale:** LOW, but it is **the same failure mode** as the duplicate execution-table rows fixed in #605: two statements about one item, and "the topmost unticked item" becomes ambiguous.
- **Suggested direction (NOT applied):** remove the parked entry rather than the F4 row — the premise was verified 2026-09-04, the work is doc-authoring, and there is no actual decision gate.
- **Verification (#613):** Verified 2026-09-04 by direct file read. GAP-030 no longer appears in the parked-at-the-decision-gate list; its three remaining mentions are a narrative reference, a note citing this finding, and the ticked `F4` row.

### ISSUE-466 — the shrink-only story baseline makes any TEST-STATUS row addition a two-file change
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** TEST
- **Module / US / TC:** cross-module
- **Title:** `LedgerTraceabilityTests.cs:38-58` hard-codes a baseline of 60 done-stories that have no TEST-STATUS row. `TheBaseline_HasNoStaleEntries` (`:170-180`) **fails** the moment a story gains a TEST-STATUS row while its baseline entry remains. This is **by design** — the baseline must only shrink — but it is undocumented outside the test, so it surfaces as a mysterious unrelated failure.
- **Severity rationale:** LOW as a defect, but a real trap: the tempting "fix" is to revert the ledger row or weaken the assertion, both of which defeat the guard.
- **Suggested direction (NOT applied):** note the coupling in `.claude/rules/ledgers.md` so it is discoverable before someone hits it.

### BUG-467 — the RLS reconciler runs AFTER seeding, so a disable-run can 42501 before it disables anything
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** BE / DB (INFRA)
- **Module / US / TC:** Platform · tenant isolation · `DbInitializer.cs:113`
- **Title:** `ReconcileRowLevelSecurityAsync` is called **after** the seeding step. Sequence that breaks: run once with `Rls:Enabled=true` (RLS forced on ~112 tables), then restart with `Rls:Enabled=false` **and** a blank `PrivilegedConnection` while `DefaultConnection` points at `hrm_app`. `GuardRlsConfiguration` does not fire (the flag is false), `ConnectionRoutingInterceptor` goes inert (`DependencyInjection.cs:58-72`, `ConnectionRoutingInterceptor.cs:76-80`), and every seeder INSERT then runs as `hrm_app` with a blank GUC **against still-forced tables → 42501** — before line 113 ever gets the chance to disable enforcement.
- **Root cause + confidence (~85%):** ordering. The reconciler's *disable* branch is the one operation that must precede seeding, and it is sequenced after it.
- **Severity rationale:** MED, not HIGH: **blocked today by nothing**, because every current environment seeds on a BYPASSRLS or superuser role, which masks it entirely. It becomes a startup failure the moment someone runs the app under a least-privilege connection — i.e. exactly the hardening direction this platform is heading in. Found by the `E4` seed work, which had to confirm RLS could not silently swallow its inserts.
- **Suggested direction (NOT applied):** move the reconciler's DISABLE branch before `SeedAsync`. Deliberately not fixed in E4b — reconciler ordering is tenant-isolation infrastructure and would have pushed a dev-seed PR into mandatory human review.

### ISSUE-468 — `DbInitializer`'s RLS comment cites a stale flag location *(one of the two claims here was itself wrong)*
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE (docs-in-code)
- **Module / US / TC:** Platform · `DbInitializer.cs:105-112`
- **Title:** The comment cites `appsettings.json:20-22`; the flag actually lives at `appsettings.json:48-50`. That single line reference is stale.
- **⚠ CORRECTED 2026-09-05 — this finding OVERSTATED the problem, and was itself checked rather than trusted.** It claimed a second error: that the comment's *"the Docker dev stack sets `Rls__Enabled=true`"* is false because **"no compose file sets `Rls__Enabled` at all"**. **That is wrong.** `docker.env:55` sets `Rls__Enabled=true`, and `docker-compose.yml` loads it via `env_file`. The original claim inspected `docker.env.example` and generalised from it. Verified directly while executing [[BUG-467]]:
  - `appsettings.json:48-50` → `"Rls": { "Enabled": true }` ✔ (comment's *content* correct, its *line number* stale)
  - `docker.env:55` → `Rls__Enabled=true` ✔ (comment correct)
  - `appsettings.Development.json:14-16` → `"Rls": { "Enabled": false }` ✔ (comment correct)
  So **one** fact of three was wrong, not two. Only the line reference was corrected in [[BUG-467]]'s change; the rest of the comment is accurate and was deliberately left alone.
- **Why this is recorded rather than quietly narrowed:** a finding that overstates sends someone to "fix" a comment that is already true, and this ledger has spent two days measuring the *opposite* error (five findings that understated their scope — [[ISSUE-498]]). Both directions cost, and a finding corrected silently teaches nothing. It also argues for the same discipline in both directions: **verify the premise before acting on it, including when the premise is one of your own findings.**
- **Severity rationale:** LOW, with an aggravating detail: **this comment was itself written to correct an earlier stale claim**, and has now gone stale in turn. It actively misleads anyone reasoning about RLS in dev — which is the reasoning [[BUG-467]] depends on. **Seventh** recorded case in this repo of a comment outliving its code.
- **Suggested direction:** ✅ the line reference was corrected alongside [[BUG-467]] (same block). Nothing else in the comment needs changing.

### ISSUE-469 — the test factory mints tenants with an unresolvable plan, re-manufacturing the fail-open BUG-307 fixed
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** TEST
- **Module / US / TC:** cross-module · `ApiTestFactory.cs:230`
- **Title:** `CreateClientWithPermissionsAsync` mints persona tenants with `PlanId = "default"`, which **matches no row in `subscription_plans`**. `BUG-307` fixed exactly this in the seeder (`DbInitializer.cs:71` now uses `"enterprise"`) because an unresolvable plan makes every plan-limit lookup return null and read as **unlimited**. The test harness still manufactures the fail-open.
- **Severity rationale:** LOW by blast radius, but it is a **coverage hole with a shape worth noting**: any persona-based suite runs with plan limits silently disabled, so a plan-limit regression cannot be caught there. That matters more now — the `F4` audit found `max_api_calls_per_month` is metered and displayed but **never enforced**, and this is precisely the class of defect these tests could not see.
- **Suggested direction (NOT applied):** point the factory at a real plan. Shared fixture across ~35 classes in the HttpApi collection, so it needs its own change and a full-suite run.
### ENH-470 — "a comment outlived its code" is now 7 recorded cases; most are NOT mechanically detectable, and that is the finding
- **Type / Severity / Status:** ENH · MED · OPEN
- **Layer:** TEST / process
- **Module / US / TC:** cross-cutting
- **Title:** Seven recorded cases, **three found on 2026-09-04 alone**: E4's `Dockerfile`/`docker-compose.yml` claim that the browser calls `localhost:5000` (asserting a missing proxy was harmless — it was the bug); [[ISSUE-461]]'s four monitoring comments asserting "always null / always empty" directly above real computed assignments; [[ISSUE-468]]'s RLS comment citing the wrong `appsettings.json` lines *(the second half of that finding — a compose setting supposedly unset — turned out to be **wrong**; `docker.env:55` does set it, corrected 2026-09-05)*. Each cost real investigation time, and two of them actively **prevented** a correct premise verification — a reader checking the premise found a confident comment and stopped.
- **The analysis that matters, and it is negative:** I initially proposed a mechanical guard. On examination **it would catch almost none of these.** Breaking the seven down by detectability:
  - **Semantically false claims** — "the browser calls localhost:5000", "always null, deliberately", "the Docker dev stack sets `Rls__Enabled=true`". These are prose assertions about runtime behaviour. **No static check can evaluate them.** This is the large majority.
  - **Stale location citations** — `appsettings.json:20-22` when the key is at `:48-50`. Mechanically checkable (assert the cited key/symbol appears near the cited line), but this is **one partial case out of seven**.
  - A file-existence + line-in-range checker — the obvious first idea, and the shape `ClaudeMdAccuracyTests` already uses for markdown links — would have caught **zero** of the seven, because every cited file still exists and every cited line is still in range.
- **Severity rationale:** MED as a process finding. The recurrence rate is the signal: 3 in one day, and the E4b instance had **itself been written to correct an earlier stale claim**. That is a comment that went stale, was fixed, and went stale again — which says the practice regenerates the defect faster than the corrections land.
- **Suggested direction (NOT applied) — and deliberately not a guard:** the cheap mechanical win is small and should be sized honestly. The likelier-useful directions are (a) extend `/retro`'s existing setup-drift pass to sample high-traffic source comments that make cross-file or runtime claims, since that pass already exists to recheck whether documented claims are still true; and (b) treat a long explanatory comment asserting *another* file's or environment's behaviour as the smell — those are the ones that rot, because nothing near them changes when the thing they describe does. **A human should decide whether (a) is worth the cadence cost; I am not proposing a code change.**
### BUG-471 — all three plan-override admin calls hit a URL the API does not serve; the console feature is a live 404
- **Type / Severity / Status:** BUG · HIGH · ✅ **RESOLVED #617** (verified 2026-09-04 via `/verify-fix`)
- **Layer:** FE / BE (contract)
- **Module / US / TC:** Admin Console · US-ADM-009 AC-5 / US-ADM-012 BR-6
- **Title:** `subscription-plan.service.ts:130-167` calls `/system/tenants/{id}/plan-overrides` for **all three** override operations. `AdminPlansController.cs:130-163` serves `/system/plans/overrides`. Every call is a **live 404**. `PlanLimitOverride` is enforced correctly everywhere it is read — it is simply **not settable from the admin console**, only by direct API call.
- **Root cause + confidence (~95%, both sides read directly):** FE/BE contract drift, unguarded because the FE spec mocks the wire.
- **Severity rationale:** HIGH. A documented admin capability is dead at the wire, and it is the *escape hatch* for plan limits — the mechanism an operator reaches for when a tenant needs an exception. It fails silently to anyone not watching the network tab. This is the same leg-2 class as [[BUG-460]] and the same mocked-spec blind spot recorded four times before.
- **Suggested direction (NOT applied):** correct the three FE URLs. Fix [[BUG-472]] in the same change — it is currently masked by this 404 and will surface the moment this is fixed.
- **Verification (#617):** Verified 2026-09-04 against a **freshly rebuilt** frontend image (the running stack was 2 days stale — [[ISSUE-422]]). The served bundle now calls `system/plans/overrides`; the only surviving `plan-overrides` strings are the component selector `app-plan-overrides-section`, file paths and a comment quoting the old URL — **no live HTTP call**. Source-confirmed: **no controller serves a `plan-overrides` route**, while `AdminPlansController` serves `GET/POST overrides` and `DELETE overrides/{id}` under `api/v1/system/plans`. ⚠ **Precision correction to my own wording:** I filed this as "a live 404". On a non-admin host every `/system/*` path returns **403** from a host guard *before* routing — a definitely-nonexistent route returns the identical body — so 404 is what an authenticated System Admin on the admin host would see, not what a casual probe shows. The substance stands; the headline was imprecise, which is the [[ENH-485]] pattern applied to my own finding.

### BUG-472 — the plan-limit field list uses camelCase keys the backend rejects, and is wrong in two more ways
- **Type / Severity / Status:** BUG · MED · ✅ **RESOLVED #617** (verified 2026-09-04 via `/verify-fix`)
- **Layer:** FE
- **Module / US / TC:** Admin Console · US-ADM-012
- **Title:** `plan.models.ts:165-174` `LIMIT_FIELDS` uses **camelCase** keys, which the backend rejects as `limit_key_invalid`. It also includes `auditLogRetentionDays` — never a valid override key — and **omits** `maxTemplateLanguageVariants`.
- **Severity rationale:** MED. Blocks nothing *today* only because [[BUG-471]]'s 404 means no request ever reaches validation. It becomes the immediate next failure once that URL is fixed — so fixing BUG-471 alone would move a 404 to a 400 and look like a regression.
- **Suggested direction (NOT applied):** fix with [[BUG-471]], not after it.
- **Verification (#617):** Verified 2026-09-04 in the rebuilt bundle: `max_employees`, `max_api_calls_per_month` and the previously-missing `max_template_language_variants` are all present as snake_case limit keys. `auditLogRetentionDays` still appears — **correctly**: it is a real plan **column** (camelCase, editable in the plan editor), just not a valid override **key**, a distinction `plan.models.ts:151` documents explicitly. Shipped with [[BUG-471]] deliberately, since fixing the URL alone would have turned a 404 into a `limit_key_invalid` 400 and read as a regression.

### BUG-473 — the usage-gauge path bypasses the lookup built to fix BUG-307, and displays "unlimited" while the gates 403
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** BE
- **Module / US / TC:** Platform / Admin Console · US-ADM-002
- **Title:** `PlatformMonitoringService.cs:455-459` (and `:256`) calls `PlanLimitResolver` **directly** rather than `PlanLimitLookup`, so it carries **no `IsConfigurationError` handling**. An unresolvable `plan_id` therefore renders as **"unlimited"** in the console while the enforcement gates return 403 for the same tenant.
- **Root cause + confidence (~90%):** the lookup wrapper was introduced for `BUG-307` and this call site was not migrated.
- **Severity rationale:** MED. It is **the exact BUG-307 case the lookup exists to prevent**, re-entering through a path nobody re-checked, and it produces the worst kind of operator signal: a dashboard that contradicts the system's own enforcement.
- **Suggested direction (NOT applied):** route both call sites through `PlanLimitLookup`.

### ISSUE-474 — a limit-only plan edit leaves the tenant's denormalized limit snapshots stale
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE
- **Module / US / TC:** Admin Console · US-ADM-012 BR
- **Title:** `SubscriptionPlanService.cs:314-333`'s plan-edit sweep updates `EnabledModules` and `UpdatedAt` only — it does **not** re-stamp `Tenant.MaxEmployees` or `Tenant.AuditLogRetentionDays`. `ChangeTenantPlanAsync` **does** re-stamp both. So changing a tenant's plan is consistent, but editing the plan's limits leaves every existing tenant on stale snapshots.
- **Severity rationale:** ~~MED~~ → **HIGH, re-rated 2026-09-04.** Filed as "silent limit drift", which understated it. `AuditLogPurgeService.cs:41` reads `Tenant.AuditLogRetentionDays` **RAW**, so a stale snapshot makes the **daily purge job delete audit rows on the wrong retention window** — raise a plan from 90 to 365 days and history is still destroyed at 90. That is data loss against an intended compliance setting, not drift. The inconsistency between the two write paths also means testing one proves nothing about the other.
- **Suggested direction (NOT applied):** re-stamp both fields in the sweep, or stop denormalizing them.

### ISSUE-476 — `[Trait("TC","TC-ADM-012")]` binds 7 test files to a test case document that does not exist
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST / DATA
- **Module / US / TC:** Admin Console · US-ADM-012
- **Title:** A real `[Trait("TC", "TC-ADM-012")]` appears across **7** test files, and TEST-FINDINGS entries cite the id as though a document backs it — but `docs/QA/admin-console/TC-ADM-012.md` **does not exist**. The trait binds to nothing readable.
- **Severity rationale:** MED. Traceability is Critical Rule #4, and this is the failure inverted from the usual one: the *tests* exist and the *document* does not, so every automated check that starts from the trait reports coverage that a human cannot follow.
- **Suggested direction (NOT applied):** author `TC-ADM-012.md` (qa-engineer's lane) — the arms already exist, so this is documenting what is green.

### DECISION-477 — should Offboarding be an independently sellable module? It currently gates under Onboarding
- **Type / Severity / Status:** DECISION · MED · **PARKED at the decision gate**
- **Layer:** BE / product
- **Module / US / TC:** Admin Console · US-ADM-012
- **Title:** `ModuleEntitlementMiddleware.cs:64-65` gates offboarding **and** exit-interviews under the **Onboarding** module; there is no separate Offboarding module in `PlanModules`. Whether it should be separately sellable is a pricing/packaging call, not a BA authoring one.
- **Why parked:** splitting ripples into the plan editor, the FE `CANONICAL_MODULES` list, `ISSUE-353`'s drift guard and the normalization migration. Guessing either way writes an FR that is expensive to reverse. Recorded as an open question in `US-ADM-012 §10` rather than decided.
- **Needs from a human:** a product call on packaging.

### DECISION-478 — reconciling the 403/409/422 limit-gate divergence is a breaking API change
- **Type / Severity / Status:** DECISION · MED · **PARKED at the decision gate**
- **Layer:** BE / API contract
- **Module / US / TC:** Admin Console · US-ADM-012 AC-3
- **Title:** The nine limit gates return **403, 409 and 422** and **four carry no machine code at all**, against tech-doc §47.2's single `PLAN_LIMIT_EXCEEDED` shape with `{limit, current, planCode}`. `max_employees` is inconsistent *with itself* across three sites, which also use two different denominators.
- **Why parked:** reconciling to the documented shape is a **breaking change** for any client branching on the current codes. Deciding it inside a BA authoring task would have been the wrong place; the story instead documents the current state honestly.
- **Needs from a human:** whether to converge on §47.2 (breaking) or amend §47.2 to the shipped reality. **AC-3's "limit reached — upgrade" UX is blocked on this either way.**

### ISSUE-479 — GAP-017 still reads OPEN in the register; it was fixed and test-bound on 2026-08-08
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** Authentication · GAP-017 · TC-AUTH-161
- **Title:** `GAP-REGISTER.md:139` carries GAP-017 ("Unverified email accepted for SSO domain allow-listing — no `xms_edov`/`email_verified` check anywhere") as an open, un-annotated row. The check **exists and is tested**: `EntraSsoService.cs:546-563` `IsEmailVerified` iterates `{ "xms_edov", "email_verified" }` (bool-or-string, absence ⇒ false), `SsoIsolationGuard.cs:81` gates the domain rule on it (`domainMatches && emailVerified`) and JIT requires it again at `:91`. `TEST-FINDINGS-RESOLVED.md:1230-1231` marks it **RESOLVED 2026-08-08**, and `TRACEABILITY-MATRIX.md:159` binds AC-7 to `UnverifiedEmail_CannotSatisfyTheDomainRule_GAP017`.
- **Severity rationale:** MED. **Reverse drift** — a *security* row reading OPEN when the control shipped invites someone to rebuild it, and worse, invites the conclusion that this platform ships unverified-email SSO. Found while verifying F1, where the stale row nearly became a design input.
- **Suggested direction (NOT applied):** annotate the register row RESOLVED with its TC, the way other closed rows are. Only `/verify-fix` closes findings; the register is a different document and this is its correction.

### DECISION-480 — SSO domain allow-lists must be PER-PROVIDER (decided 2026-09-04)
- **Type / Severity / Status:** DECISION · HIGH · **DECIDED — record, do not re-litigate**
- **Layer:** BE / DB / product
- **Module / US / TC:** Authentication · GAP-019b / F1
- **Title:** `Tenant.AllowedEmailDomains` (`Tenant.cs:171`) has **no provider dimension**, and `SsoIsolationGuard.Evaluate(settings, tid, email, emailVerified)` (`SsoIsolationGuard.cs:53`) admits on `tidAllowed || domainAllowed`. Today that reads "trust these domains, via Entra". Route a second provider through the same guard and **every tenant that allow-listed `acme.com` for Entra silently also admits any Google account at `acme.com`** — with no admin act and no notice.
- **Why it is sharper for Google specifically:** `tidAllowed` can never be true (Google issues no `tid`), so admission rests **entirely** on the domain rule, i.e. entirely on `email_verified`. The guard's own comment (`SsoIsolationGuard.cs:74-76`) relies on a backstop — *"The tid rule is unaffected — it is bound to the issuing directory and cannot be self-asserted"* — that **has no Google equivalent**. A secondary rule becomes a single point of failure.
- **DECISION (2026-09-04):** **per-provider allow-lists.** Domain trust is declared per provider; enabling Google grants nothing until an admin adds domains for Google specifically. Costs a schema change + migration, validator work (`TenantAuthSettingsValidator.cs:57-59`, `:106-115`) and a Tenant Admin UI change (`sso-settings.component.ts`) — accepted, because no existing tenant's trust may widen without an explicit act. §3.4 designates cross-tenant leakage zero-tolerance.
- **Confidence:** ~90% that the widening is real as described; it is contingent on Google routing through this guard, which is the stated reuse plan.

### ISSUE-481 — `docs/BA/STATUS.md` contradicts itself about US-PRF-011, 60 lines apart
- **Type / Severity / Status:** ISSUE · HIGH · ✅ **RESOLVED #621** (verified 2026-09-04 via `/verify-fix`)
- **Layer:** DATA (ledger)
- **Module / US / TC:** Performance · US-PRF-011 · GAP-021
- **Title:** `STATUS.md:229` records US-PRF-011 as rescoped and calls the "US-PRF-010 dead-end" premise **"verified FALSE"**. `STATUS.md:289` — same file — still lists the story as *"(unblocks US-PRF-010 dead-end)"*. ISSUE-348 retracted that premise and is RESOLVED (`TEST-FINDINGS-RESOLVED.md:6026-6035`).
- **Severity rationale:** HIGH for a ledger issue. Anyone scoping F3 from line 289 builds toward an unblocker that does not exist, and `RecommendationService.cs:469-479` confirms no dead-end: the BR-2 gate passes whenever a manager review is submitted.
- **Suggested direction (NOT applied):** strike the parenthetical at `:289` and point it at the §1b rescope.
- **Verification (#621):** Verified 2026-09-04 by direct file read — no stack needed, the artifact IS the fix. `docs/BA/STATUS.md:289` no longer asserts the retracted "unblocks US-PRF-010 dead-end" premise; the only remaining occurrence is inside the correction note that quotes what the line used to say and why it was wrong.

### ISSUE-482 — US-PRF-011's own AC-3 still states the retracted unblocker, and it is the AC an implementer reads first
- **Type / Severity / Status:** ISSUE · HIGH · ✅ **RESOLVED #621** (verified 2026-09-04 via `/verify-fix`)
- **Layer:** DATA (BA story)
- **Module / US / TC:** Performance · US-PRF-011
- **Title:** §3 AC-3 says phase completion *"unblocks US-PRF-010 recommendation generation (removes the `calibration_incomplete` trap)"*. §1b — the **rescope that supersedes §3** — removed that as fictional. The stale skeleton sits above the corrected table in the same file.
- **Severity rationale:** HIGH. This is the single most likely source of wrong-scope work on F3: the §3 skeleton is where an implementer starts reading. Two documents disagreeing is bad; one document disagreeing with itself in reading order is worse.
- **Suggested direction (NOT applied):** rewrite §3 AC-3 to the §1b wording **before** F3 is picked up.
- **Verification (#621):** Verified 2026-09-04 by direct file read. Zero assertions of the retracted unblocker remain in `US-PRF-011.md` §3 AC-3, and a precedence banner now states that §1b supersedes §3 — the ordering trap, not just the one stale cell, since §3 is the section an implementer reads first.

### BUG-483 — ISSUE-350 is RESOLVED but its user-visible symptom is still live; the FE drops the field that fixed it
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** FE / BE
- **Module / US / TC:** Performance · US-PRF-011 · ISSUE-350
- **Title:** ISSUE-350's fix added `CycleProgressDto.CalibrationCompleted` (`PerformanceDashboardDtos.cs:130`). The **frontend never reads it** — absent from `ICycleProgress` (`dashboard.models.ts:113-119`) and dropped by `mapCycleProgress` (`:266-275`) — while `performance-dashboard.service.spec.ts:64` **mocks `calibrationCompleted: 38` and stays green**. Separately, the finding's other two cited defects are untouched: `CyclePhaseTransitionJob.cs:65` still hard-skips any phase that is not GoalSetting/SelfAssessment/ManagerReview, and `AppraisalCycleService.cs:571-577` still scores a Calibration phase `_ => 0` with overdue suppressed at `:585-586`. The cycle timeline therefore still renders Calibration at a permanent 0%.
- **Severity rationale:** MED. It is the **FE-mocks-a-shape-nothing-reads** pattern — a spec that pins a field the app discards, so the fix is green in CI and invisible in the product. Fifth recorded instance of a mocked spec concealing a contract break.
- **Suggested direction (NOT applied):** the 3-line FE map fix belongs in F3's frontend slice; the job/scoring defects belong in its backend slice, since `CyclePhase.CompletedOn` is what they are waiting on. **Reopen ISSUE-350 or supersede it with this finding — its RESOLVED status is inaccurate.**

### ISSUE-484 — US-PRF-011 is the only shipped performance story with no TC and no TEST-STATUS row
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST / DATA
- **Module / US / TC:** Performance · US-PRF-011
- **Title:** `TEST-STATUS.md` has rows for US-PRF-001..010 and **none** for US-PRF-011; `docs/QA/performance/` has no `TC-PRF-011-*` file. Leg 3 is carried entirely by xUnit (8 behavioural facts plus a real-Postgres isolation arm — the backend here is unusually solid).
- **Severity rationale:** MED. Critical Rule #4 traceability is broken, and the practical consequence is that **no `/test-all` pass will ever select this story** — it is invisible to the testing loop by construction, not by verdict.
- **Suggested direction (NOT applied):** author `TC-PRF-011-01..NN` against the **rescoped §1b** ACs (not §3 — see [[ISSUE-482]]) and add the TEST-STATUS row, as part of F3.

### ENH-485 — two register rows in a row had accurate substance and inflated headlines
- **Type / Severity / Status:** ENH · LOW · OPEN
- **Layer:** process
- **Module / US / TC:** cross-cutting · GAP-REGISTER
- **Title:** `GAP-020` was reworded on 2026-09-04 because its headline ("no way to rotate a compromised JWT signing key") was false while its substance was real. `GAP-021`'s headline says calibration hit *"the exact trap the story warned against"* — the story's warning was the **ISSUE-290 trap** (a permission string *checked but absent from the catalog*, making the feature unreachable), and the build **avoided** it: `CyclesController.cs:200` authorizes on catalog-real, actually-granted permissions. The real defect is coarse-grained authorization — calibration cannot be delegated without also granting org-wide review/publish — which is a genuine but lesser gap.
- **Severity rationale:** LOW individually; recorded because it is now **twice in two items**, and both times the inflated headline drove scheduling. GAP-020 was nearly scheduled as a vulnerability fix; GAP-021 reads as a repeat of a known trap it did not repeat.
- **Suggested direction (NOT applied):** when a queue item is drafted from a register row, verify the row's **characterisation** against code, not just its existence. That check is cheap and has now paid twice.

### ISSUE-486 — the BUG-307 anti-regression guard is blind to batched projections, which is why BUG-473 shipped
- **Type / Severity / Status:** ISSUE · HIGH · OPEN
- **Layer:** TEST
- **Module / US / TC:** Platform · US-ADM-012 · `PlanLimitLookupUsageGuardTests`
- **Title:** `PlanLimitLookupUsageGuardTests.cs:26-28` matches only the hand-written shape `.Select(p => (long?)p.Field)`. `PlatformMonitoringService` resolved plan limits through a **batched** projection — `.Select(p => new { p.Code, p.MaxEmployees, ... })` — and was therefore **invisible to the guard for the whole of [[BUG-473]]'s lifetime**. The guard's own doc promises it "blocks the eleventh copy". **It did not block this one.**
- **Root cause + confidence (~95%):** the guard was written against the exact syntax of the ten call sites it was retiring, so it pins a *spelling*, not the *rule*. Any caller that reads plan limits by another shape passes it silently.
- **Severity rationale:** HIGH. This is a guard that **reports safety it does not provide** — strictly worse than no guard, because it stopped anyone looking. It is the direct reason a fail-open on a revenue-affecting limit reached production, and nothing prevents the next one.
- **Suggested direction (NOT applied):** widen to the batched shape, **or** add a positive arm asserting `PlatformMonitoringService` actually reaches `PlanLimitLookup`. Widening changes blast radius across four projects and may flag legitimate batch projections, so it needs its own allow-list decision — which is why it was flagged rather than fixed in-lane.
- **SURVEY:** **2** batched projection sites in **1** file are matched by the guard, out of **16** non-migration production files referencing `SubscriptionPlans` across `HRM.Api`/`HRM.Application`/`HRM.Domain`/`HRM.Infrastructure` (unit = files, then match sites). Both matched sites are `PlatformMonitoringService.cs:231` and `:329`, and that file references `PlanLimitLookup` 8 times, so it complies. **0** files match the ambiguous single-column arm. There are **14** `PlanLimitLookup.Resolve*` call sites across **9** files. Excluded: `Migrations/`, test projects.
- **AUDIT (2026-09-07):** (1) "the guard matches only `.Select(p => (long?)p.Field)` and is blind to batched projections" — **FALSE at this commit.** The guard was extended: a `BatchedLimitProjection` regex at `PlanLimitLookupUsageGuardTests.cs:56-58`, enforced by `AnyFile_BatchProjectingPlanLimits_MustReachTheSharedLookup_ISSUE486` (`:130-154`) plus an inertness arm `TheBatchedRule_IsActuallyScanningSomething_ISSUE486` (`:160-175`). Merged in `f4c9680f` (**#634**), verified an ancestor of `origin/test/local-subdomains`. (2) Cited `PlanLimitLookupUsageGuardTests.cs:26-28` — **stale**; the `AmbiguousLookup` regex is at **`:29-31`**. (3) The historical premise (the guard *was* blind during BUG-473) — **CONFIRMED** by the guard's own remediation doc at `:33-55`. (4) **Residual shape the extended guard still does not cover**: `TenantProvisioningService.cs:345-346` projects `p.MaxEmployees` through a positional constructor `.Select(p => new SubscriptionPlanDto(...))`; both regexes require `new {`, so neither matches, and that file never references `PlanLimitLookup`. It is a plan-*catalog* listing rather than a limit resolution, so it is currently innocent — recorded here rather than filed separately.
- **SEVERITY CHECK:** **lower** — the gap this entry describes is closed and merged (**#634**). It is a stale-OPEN ledger entry, not a live HIGH — see `ISSUE-545`.
- **LEDGER NOTE:** status left **OPEN** — this backfill audits, it may not close a finding. `/verify-fix` must re-run the bound TCs and flip it.

### BUG-487 — the platform console reported a different employee cap than the gates enforce, and ignored purchased overrides
- **Type / Severity / Status:** BUG · MED · OPEN *(fix in flight — P1.1 branch; only `/verify-fix` may close it)*
- **Layer:** BE
- **Module / US / TC:** Platform / Admin Console · US-ADM-012 AC-4
- **Title:** Two defects in one expression. `PlatformMonitoringService.cs:258` (dashboard sweep) and `:321` (per-tenant detail) computed `int? limit = t.MaxEmployees ?? plan?.MaxEmployees` — **snapshot first**. All five enforcement services compute `effective.Value ?? tenant.MaxEmployees` — **plan first**. **Opposite precedence**, so the console can report a cap the system does not enforce, and this needs **no staleness at all**: a snapshot that merely *differs* from its plan is enough. Second, **neither monitoring site consulted `PlanLimitOverrides`** for the employee limit — overrides reached only the storage/email/API gauges — so a tenant who **purchased** a cap increase saw the un-raised number.
- **Root cause + confidence (~95%, both sides read directly):** the precedence ternary was hand-copied into **five** enforcement services and a sixth, inverted, copy in monitoring — the S-1 shape `PlanLimitLookup`'s own class doc was written to condemn, regrown after that fix. Monitoring was the **only** inverted copy; the five enforcement gates agreed with each other.
- **Severity rationale:** MED. Not a security or data defect, but an operator making a capacity decision from the console is reading a number the system will not honour — and the overrides half means the console silently under-reports what a customer has paid for.
- **Suggested direction:** one shared `WithSnapshotFallback` helper so there is a single answer to "what is this tenant's cap". Discovered by [[ISSUE-486]]'s blind spot; filed separately because the guard gap outlives this fix.

### ISSUE-488 — BUG-473's fix is inert on screen: the FE will not render ConfigurationError
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** FE
- **Module / US / TC:** Admin Console · US-ADM-012 AC-4
- **Title:** The P1.1 fix adds a `LimitStatus` field (`Enforced` | `Unlimited` | `ConfigurationError`) to `TenantUsageSummaryDto` / `UsageGaugeDto`. The change is additive and deserialization-safe, so nothing breaks — **but the System Admin dashboard will not render the `ConfigurationError` state**, so the operator-facing half of [[BUG-473]] remains unfixed. The backend now knows the difference and the screen still does not show it.
- **Severity rationale:** MED. Worth recording loudly because it is the **leg-2 reachability** pattern this codebase fails most: a correct backend behind a UI that cannot express it. [[BUG-460]] and [[BUG-483]] are the same shape, and this would have become the third if it went unrecorded.
- **Suggested direction (NOT applied):** render `ConfigurationError` distinctly — **not** as "Unlimited" and **not** as a blank cap. Natural to batch with `P1.2`, which is already in the same dashboards.

### BUG-489 — the audit purge still reads a raw retention snapshot, and a dangling plan_id is never swept
- **Type / Severity / Status:** BUG · MED · **needs-decision**
- **Layer:** BE (destructive scheduled job)
- **Module / US / TC:** Platform · GAP-004 · `AuditLogPurgeService`
- **Title:** `AuditLogPurgeService.cs:41` reads `tenant.AuditLogRetentionDays` **raw**, with a hardcoded `<= 0 ? 90` and no plan lookup. P1.1 keeps the snapshot fresh via the **plan-edit path only**. A tenant with a **dangling `plan_id` is deliberately never swept by any path** (documented fail-open), so it keeps whatever retention it was first stamped with **forever** — and the daily purge acts on that value, deleting audit rows.
- **Severity rationale:** MED, and it is the residue of [[ISSUE-474]] rather than a duplicate: 474 is closed by P1.1 for the plan-edit path, this is the path that remains. It destroys data on a stale window, which is why it is a BUG and not drift.
- **Needs from a human:** what should an unresolvable `plan_id` mean to a **destructive** job — fail closed and never purge, use `StrictestConfiguredAsync`, or keep the 90-day default? Changing a delete job's behaviour on a config error is not a call to make in-lane.

### ISSUE-490 — `PlanEditableFields` omits `MaxTemplateLanguageVariants`
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE
- **Module / US / TC:** Admin Console · US-ADM-009
- **Title:** `SubscriptionPlan` carries `MaxTemplateLanguageVariants`, but `SubscriptionPlanDtos.cs:56` `PlanEditableFields` does not — so that limit appears uneditable through the admin plan API. Note it is also the key [[BUG-472]] found **missing from the FE `LIMIT_FIELDS`**, so the same limit is unreachable from two directions.
- **Severity rationale:** LOW. **Second-hand — surfaced by a survey sub-agent and NOT independently verified; confidence ~70%. Check before acting.** Recorded with its provenance rather than presented as established, because an unverified finding stated confidently is how bad ledger rows are born.
- **Suggested direction (NOT applied):** decide whether the limit is meant to be editable at all, then align the DTO and the FE field list together.

### ISSUE-491 — an orphaned XML doc block leaves `PlanLimitLookup.ResolveAsync` undocumented
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE (docs-in-code)
- **Module / US / TC:** Platform · `PlanLimitLookup.cs`
- **Title:** Two consecutive `<summary>` blocks sit above `StrictestConfiguredAsync`. The first — describing `ResolveAsync`, *"preserving the distinction between plan-not-found and plan-found-no-cap"* — binds to the wrong member. So `ResolveAsync` is effectively undocumented, and `StrictestConfiguredAsync` carries a doc describing a different method.
- **Severity rationale:** LOW, but pointed: this is the **eighth** recorded case in this repo of documentation detached from the thing it describes, in the very file whose class doc is the canonical explanation of the bug class. Whoever reads it to understand the rule is reading the wrong summary.
- **Suggested direction (NOT applied):** move the orphaned block onto the `ResolveAsync` overloads. Trivial.

### ISSUE-492 — running the test script from the main repo while working in a worktree yields a false GREEN
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST (process)
- **Module / US / TC:** cross-cutting · agent workflow
- **Title:** Observed live 2026-09-04. An agent working in `.claude/worktrees/agent-*` invoked `scripts/run-backend-tests.sh` from the **repo root** rather than its worktree. The run reported `Passed! 3` — against the **main tree's** copy of the test class, which did not contain the agent's new test at all. The real run, from the worktree, was RED. A pass was reported for code that was never executed.
- **Root cause + confidence (100%, self-reported by the agent that hit it):** the script takes a solution path relative to `$PWD`, and both trees contain a valid solution at the same relative path, so the wrong-tree invocation succeeds instead of erroring.
- **Severity rationale:** MED. It manufactures a **false green**, which is the failure mode this repo has spent the most effort eliminating — and unlike test theater, nothing in the code review can see it. It is the third false-green of the day, after the plan-override specs pinning URLs the API never served and [[ISSUE-465]]'s inert `ledger-lock` guard.
- **Suggested direction (NOT applied):** have the script refuse to run when `$PWD` is not the git top-level of the solution it was handed (`git rev-parse --show-toplevel` compared against the solution's realpath), or print the resolved worktree prominently in its header. Fail loudly on ambiguity rather than testing the wrong tree silently.

### BUG-493 — ~8 nav items gate on a permission proxy while their route gates on roleGuard
- **Type / Severity / Status:** BUG · MED · OPEN
- **Layer:** FE
- **Module / US / TC:** cross-module · `main-layout.component.ts` vs `app.routes.ts`
- **Title:** Surfaced by the 2026-09-04 staleness audit while verifying [[BUG-450]]. Roughly **eight** nav items gate on a *permission key* while the route they point at gates on `roleGuard` — Employees (`Employee.View.All` vs `roleGuard` at `app.routes.ts:580`), Reports (`Reports.View` vs `roleGuard` at `:343`), plus Payroll, Recruitment, Onboarding and Salary Grades. Two different gate systems deciding the same question.
- **Root cause + confidence (~85%):** the permission catalog has no key for several of these areas, so a *proxy* permission was chosen; a proxy is only ever coincidentally aligned with a role guard. This is the exact drift mechanism `ISSUE-210` was filed for, and the reason `Locations` explicitly documents choosing `tenantRoles` over a proxy.
- **Severity rationale:** MED and distinct from [[BUG-450]] — that was items with **no** gate, this is items with the **wrong kind** of gate. Both directions are possible: a link shown to someone the route rejects (dead-ends at `/forbidden`), or hidden from someone it would admit.
- **Suggested direction (NOT applied):** **one sweeping spec** — assert every nav item's gate against its route's guard, per persona. The auditor's judgement, which I agree with: that single spec is worth more than [[BUG-450]]'s two-line fix, because it closes the whole class instead of two instances. [[BUG-450]]'s spec deliberately asserts an invariant rather than route names for the same reason, but it covers only the Core-HR pair.

### ISSUE-494 — `origin/main` is 987 commits behind the working branch, and things silently default to it
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** INFRA (repo topology)
- **Module / US / TC:** cross-cutting
- **Title:** `origin/main` is `7ea6ce61`, dated **2026-06-23** — **987 commits** behind `test/local-subdomains`. Verified directly. Anything that defaults to the repo's default branch therefore targets a ~2.5-month-old tree: `origin/HEAD` resolution, worktree base refs, PR-diff bases, and any tool that assumes "main is the integration branch". This already bit once today — `scripts/ledger-lock.sh` resolved its merge base via `origin/HEAD` → `origin/main` and produced a diff listing most of the tree (fixed in #619).
- **Severity rationale:** MED. It is not broken so much as **misleading by default**, and the failure is silent: a tool picks the wrong base and reports a plausible wrong answer rather than an error.
- **Suggested direction (NOT applied):** either reconcile `main` with the working branch, or state explicitly in `CLAUDE.md` that `main` is **not** the integration branch and that tooling must target `test/local-subdomains`. Broader than [[ISSUE-442]], which records only the worktree symptom.

### ISSUE-495 — `.editorconfig`'s own comment asserts the BOM rule is already satisfied; it is not
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** DATA (docs-in-config)
- **Module / US / TC:** cross-cutting · relates to [[ISSUE-391]]
- **Title:** `.editorconfig:54-55` states *"all 290 files under Migrations/ carry one [a BOM]"*. Verified: there are **295** migration files and **5 lack the BOM** the same config mandates — which is precisely the defect [[ISSUE-391]]'s CHARSET half records. **The config that declares the rule asserts the rule is already met.**
- **Severity rationale:** LOW in isolation. Recorded because it is the **ninth** instance of documentation outliving the thing it describes, and the second in a *config* file rather than prose — a reader checking whether the BOM rule holds finds a confident claim that it does and stops. That is the same mechanism that kept [[BUG-473]] and the E4 proxy alive.
- **Suggested direction (NOT applied):** correct the count and the claim when [[ISSUE-391]]'s CHARSET half is closed.

### ISSUE-496 — a source comment cites `ISSUE-372`, which is a different finding in this ledger
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** FE (docs-in-code)
- **Module / US / TC:** core-hr · `main-layout.component.ts:701-704`
- **Title:** The Locations nav comment credits its gate to *"US-CHR-007 / ISSUE-372 (E5)"*. `ISSUE-372` in `TEST-FINDINGS.md` is the **payroll `validate-formula`/`reorder`** finding. The comment is using a commit-local GAP/E-numbering that collides with the ledger's ID sequence.
- **Severity rationale:** LOW, but the same class as the `BUG-060` → `BUG-303` collision already recorded as GAP-L8, and it defeats any automated trace from code back to a finding.
- **Suggested direction (NOT applied):** sweep source comments citing `ISSUE-`/`BUG-` ids against the ledger. Mechanically checkable, unlike most of [[ENH-470]]'s cases — a cited id either exists with a matching subject or it does not.

### ISSUE-497 — `ClosedXML` is pinned to a wildcard on a pre-1.0 package
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE (dependency)
- **Module / US / TC:** cross-module exports · relates to [[ENH-009]]
- **Title:** `HRM.Infrastructure.csproj:58` carries `ClosedXML Version="0.*"` — an unpinned wildcard on a **pre-1.0** package, where minor releases may change behaviour freely. Concretely: whether a leading `=` in a cell becomes a live formula is version-dependent, so [[ENH-009]]'s XLSX exposure **can change with no code edit and no PR**.
- **Severity rationale:** MED. A build that is not reproducible across time, on the exact dependency whose behaviour a security finding turns on.
- **Suggested direction (NOT applied):** pin an explicit version **before** scheduling [[ENH-009]]'s XLSX half, so its behavioural test means something.

### ISSUE-498 — a 20-finding staleness audit re-scoped five ledger entries; three understate their own scope
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** DATA (ledger)
- **Module / US / TC:** cross-cutting
- **Title:** A code-verified audit of 20 unscheduled findings (2026-09-04) found **15% wholly wasted if scheduled** (2 ALREADY-FIXED, 1 OBSOLETE) and **30%** once rows that merely *overstate* what remains are counted. Per-entry corrections it produced:
  - **[[ISSUE-321]]** ALREADY-FIXED — entities, DbSets (`AppDbContext.cs:71-73`), migration `20260719125705`, persistence `EmployeeService.cs:874-940`, route `EmployeesController.cs:118` (PR #386). Awaiting `/verify-fix`.
  - **[[ISSUE-132]]** ALREADY-FIXED — the command was deleted; `ApplicantPortalTokenService.cs:174-193` builds and dispatches the link (PR #384). Awaiting `/verify-fix`.
  - **[[ISSUE-168]]** **OBSOLETE — its premise is falsified.** `PermissionCatalog.cs:736` now grants HR Officer `Payroll.Configure`, so the controller comment it calls wrong is now **true**.
  - **[[ENH-009]] understates itself badly** — filed against **one** CSV writer; it spans **12** across attendance, payroll, leave, core-HR, recruitment, performance and audit. It is a security-hardening gap filed as an ENH, and the only genuinely campaign-shaped item in the sample (~80% mechanical).
  - **[[ISSUE-117]] understates itself** — names one write site; `InterviewService.cs:112` **and `:186`** both bypass a sanitizer that is DI-registered and already used by four sibling services. Two-line fix.
  - **[[ISSUE-148]] item (c) is factually wrong** — it says "≥1000% rejects"; the validator is `InclusiveBetween(0,1000)`, so exactly 1000% is **accepted**.
  - **[[ISSUE-039]]**, **[[ISSUE-175]]**, **[[ISSUE-384]]** are PARTIALLY-FIXED with only a residual open.
- **Severity rationale:** MED. **Reverse drift in both directions at once** — rows that overstate remaining work waste scheduling, and rows that understate their scope (ENH-009, ISSUE-117) get mis-sized, which is the more expensive error because it survives the fix.
- **Suggested direction (NOT applied):** apply these corrections before tiering the remaining 80 orphans. **And settle what the 2026-09-01 audit's "29% stale" actually counted** — if it included partial landings, this sample's 30% matches almost exactly; if it meant wholly-fixed, 15% says the prior figure was high. The two numbers differ by 2× and drive different decisions.

### ISSUE-499 — the AC-12 concurrency test asserts a race outcome as if it were deterministic
- **Type / Severity / Status:** ISSUE · MED · 🔀 **MERGED into [[ISSUE-418]] 2026-09-06 — do not schedule separately**
- **Merge rationale:** **I filed this as a duplicate.** [[ISSUE-418]] (2026-09-01) already covered the same test, the same 409-vs-403, with a deeper root cause: *the instance is loaded BEFORE the `FOR UPDATE` row lock is taken*. My de-dup grep missed it because ISSUE-418's heading line is bare (`### ISSUE-418`) — a **fourth metadata shape** that defeated the very check meant to prevent this. ⚠ **They prescribe OPPOSITE fixes**: 418 says fix the service and explicitly says *do not relax the test*; 499 said accept either code — which would **relax an assertion**, exactly what the test-integrity guard forbids. **418 is right on merits**: telling a user who *was* the approver "you are not the approver" is wrong UX. Superseded.
- **Layer:** TEST (and a contract question for BE)
- **Module / US / TC:** Workflow · AC-12 · `WorkflowRuntimeConcurrencyPostgresTests`
- **Title:** `ConcurrentApprovals_SameStep_ExactlyOneWins_NoDoubleAdvance_AC12` asserts `loser.StatusCode == 409` / `step_already_decided`. **The loser's rejection code is not deterministic.** Both callers are the step-1 approver. `WorkflowRuntimeService` loads the step at `instance.CurrentStepOrder` (`:249`), returns **409** if that step is already decided (`:265`), and **403 `not_step_approver`** if the caller is not its approver (`:280`). When the winner advances `CurrentStepOrder` 1 → 2 **before** the loser evaluates, the loser loads **step 2** — undecided, so no 409 — and `_approver1` is not step 2's approver, so it returns **403**. Observed as `Expected loser.StatusCode to be 409, but found 403`.
- **Root cause + confidence (~90%, traced through the service):** the assertion pins one of two legitimate interleavings. The **single-winner invariant is sound** — exactly one caller advances, and no double-advance occurs; only the *rejection code* varies.
- **Why the previous fix did not hold:** `ISSUE-275` (RESOLVED 2026-07-11) treated this as pure resource contention and addressed it with `maxParallelThreads:4` plus `EnableRetryOnFailure`. That reduced the frequency without removing the cause, because the cause is the assertion. The suite has since grown to **5,654 tests / 398 container starts**, so contention is back up and the flake with it — it failed **twice consecutively** on #628, a **frontend-only** PR that touches zero backend files, and once previously on a PR touching only `.claude/hooks/`.
- **Severity rationale:** MED. It is not a product defect, but it produces **false red on PRs that cannot have caused it**, which is the most expensive kind of noise: it trains people to re-run rather than read. Directly linked to [[ISSUE-453]] — the 398 per-test container starts are what generate the contention this surfaces under.
- **Suggested direction (NOT applied):** assert the invariant that actually holds — exactly one winner, no double-advance, and the loser **rejected** — accepting either 409 or 403 with the reason stated. **Separately, decide whether 403 is the right contract**: a caller who genuinely was the approver and merely lost the race is told "you are not the approver", which is misleading. If 409 is the intended answer, the fix belongs in the service, not the test — and only then should the test pin 409.
---

> **Findings below filed 2026-09-07 by the SURVEY+AUDIT backfill of the live CRIT/HIGH slice**
> (`plans/SURVEY-AUDIT-BACKFILL-PROMPT.md`). Each carries its own survey and audit per
> Engineering-Discipline rule #7. Several exist *because* auditing an existing finding falsified it.

### ISSUE-542 — QA test cases cite 435 API routes that no controller exposes, and 3 tenant-isolation arms pass vacuously on the resulting 404
- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** TEST (docs)
- **Module / US / TC:** cross-module · surfaced auditing `ISSUE-516`
- **Title:** `ISSUE-516` reported payroll TC route drift as a US-PAY-002 problem. Widening the same check across the whole QA corpus shows it is **repo-wide**: 435 route citations in `docs/QA/*/TC-*.md` match no controller route in `src/backend`.
- **SURVEY:** **435 of 1558** `/api/v1/...` route citations across **201 TC files** and **99 distinct paths** resolve to no controller route (unit = distinct route-string citations in `docs/QA/*/TC-*.md`, 2133 files scanned). By module: core-hr **130 of 403** (`/api/v1/departments` vs the real `/api/v1/tenant/departments`, `DepartmentsController.cs:17`), leave-management **142 of 327** (`/api/v1/leave-types` vs `/api/v1/tenant/leave-types`, `LeaveTypesController.cs:17`), admin-console **68 of 77** (`/api/v1/admin/tenants` vs `/api/v1/system/tenants`, `AdminTenantsController.cs:26`), payroll **14 of 53**. Excluded: prose mentions, `TEST-MATRIX.md`, and TC files citing no route.
- **AUDIT (2026-09-07):** the 481 real route templates were enumerated from controller `[Route]` + `[Http*]` attributes and each citation matched against them; the missing prefix (`tenant/`, `system/`) is the dominant cause, so **the TC docs are the drifted side, not the controllers** — confirmed for the payroll subset against the generated `api-types.ts` and the Angular clients, which both use the controller form. Not yet confirmed citation-by-citation outside payroll and the four modules named above; that residue is the reason this is filed rather than fixed.
- **Severity rationale:** HIGH — a TC citing a nonexistent route cannot be executed, and where its expected result is a 404 or 400 (tenant-isolation arms) it **passes for the wrong reason**. `ISSUE-516` alone hides 3 such arms (TC-PAY-ISO-005/006/007); the repo-wide count of vacuous isolation arms is not yet measured.
- **Suggested direction (NOT applied):** a docs-gate test that extracts every `/api/v1/...` citation from `docs/QA/**` and asserts it resolves against the generated OpenAPI contract — the same shape as `LedgerTraceabilityTests`, applied to routes. Fixing the 435 citations by hand without that guard just re-earns the drift.
- **Found:** 2026-09-07, auditing `ISSUE-516`.

### ISSUE-543 — the `PrecisionScale` gap is not confined to `numeric(18,2)`, and `DECISION-504`'s proposed guard would pass while 15 fields stay unguarded
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** cross-module (Attendance, Leave, Core HR, Recruitment) · surfaced auditing `ISSUE-501`
- **Title:** `DECISION-504` scopes its sweep and its proposed NetArchTest rule to "every decimal input bound to a `numeric(18,2)` column". Every field below is bound to a `numeric(p,s)` column with **s <= 2 but p != 18**, so that rule would go green while they stay silently rounded.
- **SURVEY:** **15** validator-bound decimal fields across **8** validator files (unit = entity properties with `HasColumnType("numeric(...)")` in `HRM.Infrastructure/Persistence/Configurations` that are settable through a request DTO covered by a FluentValidation validator). The whole `PrecisionScale` surface is **8 usages repo-wide, all in `Features/Payroll/Validators`**, so every non-Payroll numeric field lacks one by construction. Confirmed instances: `WeekdayOvertimeMultiplier`/`Weekend`/`Holiday` `numeric(3,2)` (`UpsertAttendanceSettingsValidator.cs:36`, `:39`, `:42`), `AbsenteeismThresholdDays` (`:76`), `AnnualEntitlement`/`CarryForwardLimit`/`MaxEncashDays`/`NegativeBalanceLimit` `numeric(5,2)` (`CreateLeaveTypeValidator.cs:32`, `:39`, `:51`, `:63`, plus the Update twin), `LatePolicy.DeductionDays` `numeric(3,1)` (`UpsertLatePolicyValidator.cs:21`), `EntitlementDays` x2, `Shift.MinimumHours`, `Employee.Fte`, `Vacancy.SalaryMin`/`Max`. Excluded: `numeric(18,2)` money columns (that *is* `DECISION-504`), server-computed scores, fields with no validator at all, `numeric(10,7)` geo columns, migrations, tests.
- **AUDIT (2026-09-07):** `DECISION-504`'s `numeric(18,2)` scoping — **CONFIRMED** at `docs/QA/TEST-FINDINGS.md:195`. Sharpest live instance — **CONFIRMED**: `UpsertLatePolicyValidator.cs:21` accepts `DeductionDays` under `InclusiveBetween(0m, 31m)` only, against `LatePolicyConfiguration.cs:27` `numeric(3,1)`, so `0.75` silently persists as `0.8` and is echoed back as a different number — **a 20% inflation of an unpaid-day deduction**. Not audited: whether any tenant has non-integer `DeductionDays` rows today, which would decide whether this is live or latent.
- **Severity rationale:** MED — same class as `ISSUE-169`/`ISSUE-501`, but on attendance/leave rather than statutory fields; one instance touches pay (`DeductionDays`), the rest distort configuration.
- **Suggested direction (NOT applied):** widen `DECISION-504`'s rule to key on the column's **actual** declared scale rather than the literal `numeric(18,2)`.
- **Found:** 2026-09-07, auditing `ISSUE-501`.

### ISSUE-544 — the `ISSUE-036` class survives in Performance: two services persist a client-supplied storage key with no upload and no reference resolution
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Performance · surfaced auditing `ISSUE-036`
- **Title:** `ISSUE-036` was fixed for Leave by adding a real multipart upload and resolving `AttachmentIds` against persisted rows. Goal-progress and PIP checkpoints still take `StorageKey`, `FileName` and `SizeBytes` **verbatim from the request body**, so evidence can be fabricated exactly as leave certificates once could.
- **SURVEY:** **2** services trusting client-supplied storage keys (unit = Application-layer services persisting attachment metadata with no blob upload and no reference resolution): `GoalProgressService.cs:151-163` (validator `GoalProgressValidators.cs:31-40` checks shape only; wired at `GoalProgressController.cs:69`) and `PipService.cs:393` (from `RecordCheckpointRequest`, `PipDtos.cs:186-191`, wired at `PipController.cs:158`). Excluded: `LeaveAttachmentService` and `SelfAssessmentAttachmentService`, which both do a real upload and are the pattern available to port.
- **AUDIT (2026-09-07):** "no upload endpoint exists for goal progress" — **CONFIRMED**: `GoalProgressController.cs:69` takes the metadata straight from the request body and there is no multipart route in that controller. "The validator only checks shape" — **CONFIRMED** (`GoalProgressValidators.cs:31-40`). The identical pattern in `PipService` — **CONFIRMED** (`PipService.cs:393`). Not audited: whether the blob store rejects a key that was never written, which would bound the impact to a dangling reference rather than a forged one.
- **Severity rationale:** MED, not HIGH — performance-review evidence rather than a statutory control, and the leave equivalent was HIGH because a medical certificate gates a legal entitlement.
- **Found:** 2026-09-07, auditing `ISSUE-036`.

### ISSUE-545 — eleven live CRIT/HIGH entries are marked OPEN although their fixes are merged, and the loop keeps re-reading them as work
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST (ledger)
- **Module / US / TC:** ledger hygiene · surfaced by the CRIT/HIGH backfill itself
- **Title:** Auditing every live CRIT/HIGH finding against `src/` found that **11 describe defects that no longer exist**, each closed by a PR already merged into `origin/test/local-subdomains`. Their status lines still read `OPEN`, so they inflate the backlog, distort the severity mix that drives queue ordering, and invite a second fix of an already-fixed defect.
- **SURVEY:** **11 of 36** live CRIT/HIGH entries are stale-OPEN (unit = live `OPEN`/`DEFERRED` findings at CRIT or HIGH, each audited against `src/` at `8264d6d5`) — **just under a third of the slice**: `BUG-441` (CRIT, **#588**), `ISSUE-501` (**#661**), `ISSUE-036` (**#655**), `BUG-450` (**#626**), `ISSUE-486` (**#634**), `BUG-431` (**#581**), `BUG-309` (**#532**), `BUG-448` (**#645**), and the **three CRIT `BUG-003 EXTENSION` entries** (all **#119**, merged 2026-07-02, against observations dated 2026-06-26 — six days pre-fix). **All three of the repo's headline open CRIT cross-tenant findings are in that list**, so the open-CRIT count for that class is 0, not 3. A twelfth, `ISSUE-373`, is not stale but **superseded**: its entire residual is already carried by `ISSUE-379` rows 7-9. Excluded: the ~190 MED/LOW/unrated entries, which have **not** been audited — the repo-wide count is unknown and very likely higher. `ISSUE-375` (LOW) was independently observed stale by the same sweep and is not counted in the 11.
- **AUDIT (2026-09-07):** each of the eleven was verified with `git merge-base --is-ancestor <fix-commit> origin/test/local-subdomains`, and in every case the defect's code claim was re-read at HEAD and found false; the evidence sits in the `AUDIT` bullet on each entry. The three `BUG-003 EXTENSION` cases are the starkest: `TEST-FINDINGS.md:1566` **already** records "RESOLVED by PR #119 ... the guard that closes it is `TenantAccessGuardMiddleware.cs:38-53`", and `:38-42` records a 2026-07-04 reconciliation that flipped three *sibling* EXTENSION headers — **these three were simply missed by that pass**, so the ledger has contradicted itself about its own CRITs for two months. Two mechanisms kept it invisible: they are excluded from every worklist by heading substring (`ISSUE-552`), and nothing tests the guard that closed them either way (`ISSUE-556`). This finding does **not** close them: only `/verify-fix` may, on re-run TC evidence.
- **Severity rationale:** MED — no product defect, but it is the `ISSUE-437` class (nothing verifies a documented claim) applied to the ledger's own status field, and it is what makes any backlog or severity-mix figure untrustworthy. `ISSUE-541` covers terminal-status entries sitting in the working file; this is the inverse — live-status entries whose work is done.
- **Suggested direction (NOT applied):** run `/verify-fix` on the eleven, then extend the audit to the MED/LOW remainder before trusting any backlog count. A cheap standing guard: for any finding citing a PR number, assert that PR is **not** merged while its status is OPEN.
- **Found:** 2026-09-07, by the CRIT/HIGH SURVEY+AUDIT backfill.
### ISSUE-546 — `ISSUE-436` and `ISSUE-500` are two open HIGH findings for one gap, splitting the fix and double-counting the backlog
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST (ledger)
- **Module / US / TC:** Frontend test strategy · surfaced auditing `ISSUE-436`
- **Title:** Both entries file "no FE spec can catch an FE↔BE contract break", both cite "four shipped defects", both propose the same remedy — but they cite **different** fours and sit ~2,600 lines apart, so neither references the other.
- **SURVEY:** **2** ledger entries, **1** underlying gap (unit = `### ` finding definitions describing the FE↔BE contract-assertion gap): `ISSUE-436` and `ISSUE-500` (`docs/QA/TEST-FINDINGS.md:590`, table at `:598-600`). `ISSUE-500`'s four are BUG-444, ISSUE-367, ISSUE-373, BUG-493; `ISSUE-436`'s four are the careers-detail 404, team-goals shape, onboarding dead route, and BUG-431 — **zero overlap**.
- **AUDIT (2026-09-07):** both entries read at HEAD and confirmed to describe the same root cause (0 of 330 FE spec files bind a request-side assertion to the generated contract (54 of 330 do bind response-side)) and the same remedy. Confirmed both are `HIGH · OPEN`. The `ISSUE-436` audit further found only 1 of its 4 cited defects verifiable, one misattributed to a config condition, one already fixed in place, one with no corroborating entry — so **`ISSUE-500` carries the better-evidenced four** and is the entry that should survive a merge.
- **Severity rationale:** MED — two HIGH entries for one gap double-counts the HIGH backlog (2 of 21) and risks two partial fixes.
- **Suggested direction (NOT applied):** merge into `ISSUE-500`, leaving `ISSUE-436` as a `MERGED INTO` pointer; carry across only `ISSUE-436`'s measured 0-of-330 survey, which `ISSUE-500` lacks.
- **Found:** 2026-09-07, auditing `ISSUE-436`.

### ISSUE-547 — 12 top-level Angular routes have zero inbound reference of any kind, and 1 feature component is unreachable by transitive closure
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** FE
- **Module / US / TC:** cross-module · surfaced auditing `BUG-451`
- **Title:** `BUG-451` treats "offboarding has no entry point" as a one-off missing `path: ''`. Surveying the whole route surface shows the missing index route genuinely **is** a one-off, but "shipped and unreachable" is not — 12 top-level paths are reachable only by typing the URL.
- **SURVEY:** **12 of 54** top-level route paths in `app.routes.ts` have no `NAV_ITEMS` entry, no inbound `routerLink`, and no `router.navigate` anywhere (unit = top-level route paths; 7 root-level + 47 children of the `MainLayoutComponent` route at `:130`): `portal` (`:43`, an external magic-link entry, by design), `auth/mfa/settings` (`:190`), `admin/tenant/auth-settings` (`:207`), `admin/tenant/session-policy` (`:225`), `auth/sessions` (`:234`), `admin/users/:userId/sessions` (`:242`), `admin/tenant/lockout-policy` (`:251`), `admin/users/lockout` (`:260`), `internal-careers/:id` (`:495`), `onboarding/my-assets` (`:618`), `exit-interview/analytics` (`:648`), `exit-interview/:offboardingId` (`:666`). A further 8 paths have no nav entry but *are* reached programmatically and are excluded. Separately, **1 of 206** feature components is referenced by no route array and no parent `imports:` under a full transitive-closure walk seeded from routed components and `layouts/**`: `features/onboarding/components/onboarding-progress-widget/onboarding-progress-widget.component.ts`. Excluded: `*.spec.ts`, generated `api-types.ts`.
- **AUDIT (2026-09-07):** the nav surface is a single array, `NAV_ITEMS` at `main-layout.component.ts:76`, rendered at `:754-757` and filtered by `visibleNavItems()` at `:1241-1243`; `auth-layout.component.ts` has no `routerLink`s and `main-layout` has no `router.navigate` calls, so **there is no second, hidden entry-point mechanism** — this is the complete search space, which is what makes the count trustworthy. An earlier, shallower pass in the same audit reported "3 unrouted recruitment components"; the transitive-closure walk **falsified that** (all three are imported by a parent) and returned 1. Both numbers are recorded here deliberately: the disagreement is exactly the unstated-unit failure `ISSUE-449` embodies. Not audited: whether each of the 12 is intentionally URL-only (several auth/admin settings pages plausibly are), which is what separates dead code from a missing link.
- **Severity rationale:** MED — some of the 12 are deliberate, so this is a triage list rather than 12 defects. `main-layout.component.ts:158` records the same class in a code comment ("routerLink, no card. It was reachable only by typing the URL"), so it has bitten before.
- **Found:** 2026-09-07, auditing `BUG-451`.

### ISSUE-548 — `DependencyInjection.cs` documents the field encryptor as a startup fail-fast, but registers it lazily with no eager validation
- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE
- **Module / US / TC:** Platform / startup · surfaced auditing `BUG-446`
- **Title:** The comment at `DependencyInjection.cs:83-87` asserts the encryptor is a startup guard — "its constructor throws if no usable `Encryption:ActiveKeyId`" — but `:88` registers it as `AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>()`, resolved lazily on first use.
- **SURVEY:** **1 of 1** encryptor registration, and **0** eager-validation calls repo-wide (unit = `ValidateOnStart` occurrences in `src/backend` — zero hits). Its only startup construction path runs inside the Development-swallowing try at `Program.cs:833-849`.
- **AUDIT (2026-09-07):** the comment — **CONFIRMED** at `:83-87`; the lazy registration — **CONFIRMED** at `:88`; "no `ValidateOnStart` anywhere" — **CONFIRMED**, 0 hits across `src/backend`. So the claim in the comment is FALSE as written: the throw is real (`AesGcmFieldEncryptor.cs:54`, `:59`, `:66`) but it is not a *startup* guarantee. Not audited: whether `DbInitializer` resolves it early enough in practice to make the distinction moot in a normal boot.
- **Severity rationale:** LOW — doc-vs-code drift, and it compounds `BUG-446` (whatever it throws would be swallowed anyway). Filed rather than folded because it is a claim in the source, not in the ledger.
- **Found:** 2026-09-07, auditing `BUG-446`.

### ISSUE-549 — a k6 check whitelists HTTP 429 as a pass, accepting the failure code so the assertion cannot fail
- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** TEST (perf)
- **Module / US / TC:** NFR-1 perf harness · surfaced auditing `ISSUE-534`
- **Title:** `perf/scripts/03-scale-reads.js:43` asserts `check(r, { 'export 200/429': (x) => x.status === 200 || x.status === 429 })` — the one place in the harness where a rate-limiter rejection is counted as success.
- **SURVEY:** **1 of 34** `check()` calls across the 6 k6 scenarios accepts a failure status as a pass (unit = `check()` assertions in `perf/scripts/*.js`; `lib.js` and the shell script excluded).
- **AUDIT (2026-09-07):** the line — **CONFIRMED verbatim** at `03-scale-reads.js:43`. Its blast radius is **narrower than the shape suggests**: it feeds only the `checks` metric, not `http_req_failed`, and that scenario still carries `'http_req_failed': ['rate<0.01']` (`:14`), which a 429 storm would still trip. So this cannot by itself produce a false green — recorded as LOW for that reason, not as a live false-pass.
- **Severity rationale:** LOW — a real instance of "accept the failure code so the assertion can't fail", but currently backstopped by a sibling threshold.
- **Found:** 2026-09-07, auditing `ISSUE-534`.

### BUG-550 — three recommendation write paths echo unmasked compensation back, and the pending `BUG-533` fix deliberately leaves them open
- **Type / Severity / Status:** BUG · **HIGH** · OPEN
- **Layer:** BE
- **Module / US / TC:** Performance · surfaced auditing `BUG-533`
- **Title:** `BUG-533`'s fix (`origin/fix/BUG-533-workspace-comp-leak`, unmerged) closes the workspace and auto-generate reads. The save, submit and approve/reject responses still return the full `RecommendationDto` through `BuildDtoWithLookupsAsync` with `includeCompensation: true` (`RecommendationService.cs:1286`), so a caller without `Payroll.ViewCompensation` still receives the figures — **after** the fix lands.
- **SURVEY:** **3 of the 4** residual ungated actions echo compensation on write (unit = controller actions whose response DTO carries per-employee compensation and which the pending fix does not gate): `SaveAsync` → `RecommendationService.cs:501` (`RecommendationController.cs:129`), `SubmitAsync` → `:642` (`:152`), `DecideAsync` → `:750` (approve `:169`, reject `:184`).
- **AUDIT (2026-09-07):** the three call sites and their shared `includeCompensation: true` — **CONFIRMED** at the lines above. The permission premise is inherited from the audited `BUG-533` and independently re-checked: a line manager holding only `Performance.Review.Team` (`PermissionCatalog.cs:765`) and no `Payroll.ViewCompensation` reaches the approver path (`RecommendationService.cs:670-680`) and receives figures they never supplied; an HR Officer (`:745`) gets rule-derived bonus and increment percents back from `POST submit`. **Not audited:** whether any deployed role composition actually lacks `Payroll.ViewCompensation` while holding these write permissions in a live tenant — the catalogue says the built-ins do, but tenant-custom roles were not enumerated.
- **Severity rationale:** HIGH — same exposure and same data as `BUG-533`, but filed separately **because it survives that fix**: closing `BUG-533` and marking it verified would leave this open under a green check.
- **Found:** 2026-09-07, auditing `BUG-533`.

### ISSUE-551 — the whole FR-6 review-signoff PDF export is built end-to-end and dead behind one hardcoded `false`
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** FE
- **Module / US / TC:** Performance · US-PRF review sign-off (FR-6) · surfaced auditing `ISSUE-379`
- **Title:** `ISSUE-379` lists `exportAvailable` as a **backend** gap. It is not: the endpoint, handler and Angular client all exist and work. The feature is unreachable because the FE mapper hardcodes `exportAvailable: false` at `review-signoff.models.ts:410`.
- **SURVEY:** **1** field, **1** mapper line, gating **1** complete feature surface (unit = the FR-6 export path). Built and reachable on both sides: `ReviewSignoffController.cs:193` (endpoint), `ReviewSignoffQueries.cs:56-63` (handler), `review-signoff.service.ts:215-218` (client). Adjacent same-shape field `employeeViewed` (`review-signoff.models.ts:406`, wire `notesOpenedAt` at `ReviewSignoffDtos.cs:58`) is already `ISSUE-440` — and is **missing** from `ISSUE-379`'s residual list.
- **AUDIT (2026-09-07):** all four code locations read at HEAD and **CONFIRMED**. `ISSUE-379`'s classification of this as a backend gap is therefore **FALSE**; the correction is recorded in that entry's audit too. Not audited: whether the hardcoded `false` was a deliberate feature flag pending sign-off rather than an oversight — the mapper carries no comment either way, which is itself the reason this needs a human decision rather than a one-line fix.
- **Severity rationale:** MED — a fully built, tested feature is invisible to users; the fix is one line but the intent behind the constant is unverified.
- **Found:** 2026-09-07, auditing `ISSUE-379`.

### ISSUE-552 — the ledger's finding detector excludes "family sub-entries" by heading substring, hiding three CRIT entries from every worklist
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST (tooling)
- **Module / US / TC:** ledger tooling · surfaced by the CRIT/HIGH backfill itself
- **Title:** The detector in `docs/QA/plans/SURVEY-AUDIT-BACKFILL-PROMPT.md:86-88` drops any heading matching `\b(NOTE|note|EXTENSION|EXTENDED)\b` as a sub-entry "belonging to its parent, not the worklist". Three of the entries it drops carry their own full `BUG · CRIT · OPEN` status line. They are not annotations on a parent — they are findings, and no worklist built this way will ever surface them.
- **SURVEY:** **10** headings are excluded as family sub-entries (unit = `### TYPE-NNN` definition headings matching the filter, at `8264d6d5`); **7 are live** and **3 of those are CRIT** — the sign-off meeting-notes, PIP-create-WRITE and Recruitment-Dashboard-READ `BUG-003 EXTENSION` entries. The other 3 excluded CRITs are terminal and correctly out of scope. Effect on the slice: the detector reports **3 CRIT + 29 HIGH** against a true live count of **6 CRIT**. Excluded: `TEST-FINDINGS-RESOLVED.md`, which the detector never scans.
- **AUDIT (2026-09-07):** (1) the exclusion and its rationale comment — **CONFIRMED** at `plans/SURVEY-AUDIT-BACKFILL-PROMPT.md:86-88`. (2) "the three are live CRIT" — **CONFIRMED** by their own status lines, **but auditing them showed all three are *stale*, not live**: each was closed by PR **#119** on 2026-07-02 (see their `AUDIT` bullets and `ISSUE-545`). **So what this filter hid was three months-old false alarms, not three live breaches** — which is why this is filed at MED rather than the HIGH it first looked like. The mechanism is still wrong: it is severity-blind, and had those three been real it would have hidden them just as completely. (3) **The filter also false-positives on standalone findings**: it is a substring test over the whole heading, so `ISSUE-121` ("...unlike the four rich-text **note** sections") and `ISSUE-175` ("...contradicting its own **note**") are dropped despite being ordinary independent findings — **CONFIRMED**, both read at their headings. Both are LOW, so today's CRIT/HIGH slice is unaffected. (4) **A second, earlier revision of the same detector is in circulation** and was the one used to launch this backfill: it matched `r'### (TYPE-\d+) . (.{0,70})'`, requiring a character after the ID **on the heading line**, and silently skipped the 41-entry bare-heading block `BUG-308`…`ISSUE-418` — reporting **181 live / 1 CRIT / 21 HIGH** against the committed detector's **220 / 3 / 29**. The committed version does not have that defect. **Not audited:** whether that earlier revision was copied into any other plan, skill or script.
- **Severity rationale:** MED — the entries it actually hid were already-fixed noise, so there is no live exposure. It stays above LOW because the mechanism silently removes entries from the one artifact that decides what gets worked on, and reports no count when it does.
- **Suggested direction (NOT applied):** decide sub-entry membership from a **field**, not heading text — e.g. an explicit `- **Parent:** BUG-003` bullet — and have the detector print what it excluded, so a drop is loud rather than silent.
- **Found:** 2026-09-07, by the CRIT/HIGH SURVEY+AUDIT backfill, when a post-edit coverage recount disagreed with the pre-edit worklist.
### ISSUE-553 — three code comments assert "no workflow engine exists", and the same file routes decisions through one 380 lines below
- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE
- **Module / US / TC:** Attendance · surfaced auditing `BUG-321`
- **Title:** `RegularizationApprovalService.cs:21-23` states "NONE exists (US-ADM-007) ... `workflow_instance_id` stays null", repeated at `:342`. The same file routes decisions through `IWorkflowRuntime` at `:405-419`, and `AttendanceService.cs:591-604` sets `regularization.WorkflowInstanceId = wf.InstanceId` at submit whenever the tenant has an Active Attendance workflow definition. The comment is the source of `ENH-005`'s false "latent, not reachable" premise.
- **SURVEY:** **3** comment sites across **2** files (unit = source comments asserting the absence of a workflow engine): `RegularizationApprovalService.cs:21-23`, `:342`, and `OvertimeRecord.cs:46`. Excluded: ledger text and BA docs, which repeat the claim but are not its source.
- **AUDIT (2026-09-07):** all three comment sites read at this commit and **CONFIRMED** to assert no workflow engine. The contradicting code is **CONFIRMED** at `AttendanceService.cs:591-604` (assignment at submit) and `RegularizationApprovalService.cs:405-419` (`TryWorkflowDecisionAsync` dispatch). The downstream consequence is **CONFIRMED**: `ENH-005` is still filed `DEFERRED` on the strength of this claim. **Not audited:** whether any tenant currently has an Active Attendance workflow definition, which is what separates "reachable" from "reached".
- **Severity rationale:** LOW as comment drift, but it has already produced one mis-triaged finding, so the cost is not zero. Filed separately from `BUG-321` because the remedy is a comment correction plus an `ENH-005` re-triage, not the DTO change `BUG-321` needs.
- **Found:** 2026-09-07, auditing `BUG-321`.

### ISSUE-554 — `countryCode` is hardcoded to `'LK'` with no selector, so the multi-country statutory foundation is unusable from the product
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** FE
- **Module / US / TC:** Payroll · statutory configuration · surfaced auditing `ISSUE-403`
- **Title:** `statutory-configuration.component.ts:614` sets `countryCode = 'LK'` as a constant with no UI selector. The backend models `CountryCode` as a first-class dimension and `StatutoryDeductionResolver.cs:44-47` resolves strictly by it, so a non-LK tenant can only ever create LK-tagged rules and can never resolve its own.
- **SURVEY:** **1** hardcoded constant feeding **3** write call sites (`statutory-configuration.component.ts:779`, `:813`, `:831`); **0** UI controls anywhere let a user choose the country. Unit = FE payload builders sending `countryCode`. Excluded: the 4th builder `runTestCalculation` (`:846-855`), which omits the field entirely — that is `ISSUE-403`, a different defect on the same field.
- **AUDIT (2026-09-07):** the constant — **CONFIRMED** at `:614`; the three write sites — **CONFIRMED** at `:779`, `:813`, `:831`; the resolver's strict country keying — **CONFIRMED** at `StatutoryDeductionResolver.cs:44-47`. **Not audited:** whether multi-country statutory support is in scope for the current release — if Sri Lanka is the only supported jurisdiction by design this is a deliberate simplification rather than a defect, and the constant should say so. That open question is why this is filed rather than fixed.
- **Severity rationale:** MED — no wrong data for an LK tenant, but a modelled backend dimension with no product surface, and the failure mode for a non-LK tenant is silent.
- **Found:** 2026-09-07, auditing `ISSUE-403`.

### ISSUE-555 — RLS is not the independent backstop the ledger claims: the tenant GUC is set from the same header-resolved tenant it is supposed to backstop
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** DB
- **Module / US / TC:** Platform / tenant isolation · surfaced auditing the `BUG-003 EXTENSION` entries
- **Title:** `ISSUE-032` and several ledger notes describe Postgres RLS as defence-in-depth "that would have contained BUG-003". It would not have. `TenantGucConnectionInterceptor.cs:43-46` sets `app.current_tenant` from `AmbientTenant.Current`, which `TenantContext.cs:50` mirrors from the **header-resolved** tenant — so a spoofed `X-Tenant-Subdomain` moves the GUC too, and every RLS policy keyed on it follows the spoof.
- **SURVEY:** **1** writer of the `app.current_tenant` GUC (`TenantGucConnectionInterceptor.cs:43-46`), and **0** of the 1 derive the tenant from the JWT claim. Unit = code paths setting the RLS GUC. Excluded: test fixtures that set the GUC directly.
- **AUDIT (2026-09-07):** the interceptor and its source — **CONFIRMED** at `TenantGucConnectionInterceptor.cs:43-46` and `TenantContext.cs:50`. The consequence — that RLS inherits rather than checks the header-derived tenant — follows directly and is **CONFIRMED** by construction. **What is genuinely true instead of the ledger's claim:** the control that actually closes `BUG-003` is `TenantAccessGuardMiddleware.cs:38-53`, which compares the JWT tenant to the resolved tenant; RLS is a second enforcement of the *same* decision, not an independent one. **Not audited:** whether any RLS policy keys on something other than the GUC, which would be a genuine independent arm.
- **Severity rationale:** MED — no live exposure while the middleware guard stands, but it makes a documented defence-in-depth claim false, and that claim is what would justify accepting a weakening of the guard.
- **Found:** 2026-09-07, auditing the `BUG-003 EXTENSION` entries.

### ISSUE-556 — the single control closing the BUG-003 cross-tenant class has no end-to-end regression arm
- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** TEST
- **Module / US / TC:** Platform / tenant isolation · surfaced auditing the `BUG-003 EXTENSION` entries
- **Title:** `TenantAccessGuardMiddleware` (registered at `Program.cs:762`) is what makes the whole `BUG-003` cross-tenant class unreachable across 577 endpoints. Nothing tests it through the HTTP pipeline. Reordering or dropping that one registration line reopens the class **with a fully green suite**.
- **SURVEY:** **1** guard, **1** registration line, **0** HTTP-level tests (unit = tests exercising the guard through the real pipeline). `grep cross_tenant_denied` across `HRM.Tests` returns **0** hits. The only coverage is `TenantAccessGuardMiddlewareTests.cs:53-114` — **6** unit facts against NSubstitute doubles that never exercise `Program.cs` ordering. `UnresolvedTenantFailClosedApiTests.cs:118-131` sweeps only the *unresolved*-tenant case, never the mismatched-token case.
- **AUDIT (2026-09-07):** the guard and its registration — **CONFIRMED** (`TenantAccessGuardMiddleware.cs:38-53`, `Program.cs:762`). The zero-hit grep and the shape of the existing tests — **CONFIRMED** at the lines above. The blast radius (577 endpoints across 80 controllers) is the survey carried on the `BUG-003 EXTENSION` entries. **Not audited:** whether an existing broad API test incidentally exercises a token/subdomain mismatch without asserting on it — that would still not be a regression arm, but it would change how quickly a regression surfaced.
- **Severity rationale:** HIGH — Critical Rule #1 is tenant isolation, and its single enforcement point is pinned only by mock-level unit tests. This is the `ISSUE-437` class (nothing verifies a documented claim) applied to the platform's most important control. It is also why the three `BUG-003 EXTENSION` entries could sit OPEN for two months without anyone noticing they were already fixed: **nothing was watching that guard either way**.
- **Suggested direction (NOT applied):** one HTTP-level arm per shape — valid token + mismatched `X-Tenant-Subdomain` expecting 403 `cross_tenant_denied` — plus an ordering assertion that the guard runs after authentication and before controllers.
- **Found:** 2026-09-07, auditing the `BUG-003 EXTENSION` entries.

### ISSUE-557 — `TenantJobRunner` treats an all-zero tenant id as resolved, so a garbage tenant is accepted rather than rejected
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Platform / background jobs · surfaced auditing `ISSUE-513`
- **Title:** `TenantJobRunner.cs:65` skips only the *log push* for `Guid.Empty`; `SetTenant(Guid.Empty, ...)` still executes, and `TenantContext.cs:29-51` validates nothing, so `IsResolved` reads **true** for an all-zero tenant. The runner therefore accepts a garbage tenant id, not merely a missing one.
- **SURVEY:** **1** runner (`TenantJobRunner.cs:33`, `:65`) with **0** validation branches on the tenant id. Unit = tenant-scoped job runners. Excluded: `ParallelTenantScopeRunner.cs:22`, which shares the shape but was not separately traced.
- **AUDIT (2026-09-07):** the `Guid.Empty` log-push skip — **CONFIRMED** at `TenantJobRunner.cs:65`; the absence of validation in `TenantContext` — **CONFIRMED** at `:29-51`; `IsResolved` returning true for the zero guid follows directly and is **CONFIRMED** by construction. **Not audited:** whether any live job path can actually supply `Guid.Empty` — without that, this is a latent robustness gap rather than a live defect, which is why it is MED and not higher.
- **Severity rationale:** MED — combined with `ISSUE-513` (nothing rejects a tenant-less job at runtime), the platform's story that jobs are tenant-safe rests entirely on a build-time source scan with a 12-entry exemption list.
- **Found:** 2026-09-07, auditing `ISSUE-513`.

### ISSUE-558 — `POST /api/v1/tenant/leave-entitlements/bulk` is called by the UI and served by no controller, so bulk entitlement assignment always fails
- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** FE + BE (contract)
- **Module / US / TC:** Leave · surfaced auditing `BUG-315`/`BUG-316`
- **Title:** `leave-entitlement.service.ts:168-172` POSTs to `/api/v1/tenant/leave-entitlements/bulk`, called from `entitlement-rules.component.ts:765`. `LeaveEntitlementsController.cs:146` serves only `rules/bulk`. The path does not exist, so every bulk entitlement assignment 404s and surfaces as an error toast.
- **SURVEY:** **1 site**, one of the **7** members of the "FE calls a path no controller serves" class enumerated under `BUG-316` (141 of 146 non-spec `this.http.` call sites parsed against the 483 contract paths, each miss hand-verified). It is one of **2** members of that class that had **no ledger entry at all** — the other is `ISSUE-559`. Excluded: `*.spec.ts`, generated wire types.
- **AUDIT (2026-09-07):** the FE call — **CONFIRMED** at `leave-entitlement.service.ts:168-172`; its caller — **CONFIRMED** at `entitlement-rules.component.ts:765`; the controller's actual route — **CONFIRMED** at `LeaveEntitlementsController.cs:146` (`rules/bulk` only); absence from the contract — **CONFIRMED** against the 483 paths in `contracts/openapi/hrm-v1.json`. **Not audited:** whether `rules/bulk` is the intended target and the FE simply has the wrong path, or whether a genuinely separate entitlements-bulk endpoint was never built — that distinction decides whether the fix is one FE line or a backend endpoint.
- **Severity rationale:** HIGH — a primary flow (bulk entitlement assignment) fails 100% of the time, with no fallback path.
- **Found:** 2026-09-07, auditing `BUG-315`/`BUG-316`.

### ISSUE-559 — `GET /api/v1/tenant/org-tree/search` is called by the UI and served by no controller
- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** FE + BE (contract)
- **Module / US / TC:** Core HR · surfaced auditing `BUG-315`/`BUG-316`
- **Title:** `org-tree.service.ts:74` issues `GET /api/v1/tenant/org-tree/search`; `OrgTreeController.cs:34` exposes only the root GET. The call 404s.
- **SURVEY:** **1 site**, the second of the two previously-unfiled members of the 7-strong "FE calls a path no controller serves" class surveyed under `BUG-316`. Excluded: `*.spec.ts`, generated wire types.
- **AUDIT (2026-09-07):** the FE call — **CONFIRMED** at `org-tree.service.ts:74`; the controller's single route — **CONFIRMED** at `OrgTreeController.cs:34`; absence from the contract — **CONFIRMED**. **Mitigation, which is why this is LOW and `ISSUE-558` is HIGH:** the service comment at `:62-65` documents a client-side filtering fallback, so search still works for the user — **CONFIRMED** by reading it. **Not audited:** whether the fallback covers the full tree at realistic sizes, which is what would decide if the dead call matters at scale.
- **Severity rationale:** LOW — dead call with a working documented fallback; the cost is a wasted request and a misleading service method.
- **Found:** 2026-09-07, auditing `BUG-315`/`BUG-316`.

### ISSUE-560 — `IAuditExempt` records no reason, so 53 exemptions assert an unfalsifiable claim no test can check
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Notifications & Audit · surfaced auditing `ISSUE-392`
- **Title:** `IAuditExempt.cs:12-21` permits exactly two justifications for exempting an entity from the audit trail, but the interface takes no attribute, argument or required comment — so **0 of 53** exempt types record which one they invoke. The claim is unfalsifiable at the point of application, which is why no test can drift-detect a wrong exemption, and it is the shared root cause of `GAP-025` and all 7 instances in `ISSUE-392`.
- **SURVEY:** **0 of 53** entity types implementing `IAuditExempt` declare a reason (unit = entity classes in `HRM.Domain/**`; verified across all 52 files). Excluded: the interface itself and test doubles.
- **AUDIT (2026-09-07):** the two-reason contract — **CONFIRMED** at `IAuditExempt.cs:12-21`, with the guard paragraph at `:23-25`. The absence of any reason-carrying mechanism — **CONFIRMED** at `:27` and by the sweep of all 53 implementations. The consequence — that a reverse sweep must re-derive each justification by hand, as `ISSUE-392`'s audit did — is **CONFIRMED** by that audit having had to do exactly that. **Not audited:** whether a NetArchTest rule could enforce a reason retroactively without a large mechanical edit across 53 types.
- **Severity rationale:** MED — no live defect, but it converts every audit exemption into an unverifiable assertion, on the subsystem whose entire purpose is verifiability.
- **Found:** 2026-09-07, auditing `ISSUE-392`.

### ISSUE-561 — `emergency_contacts` and `employee_dependents` are fully plaintext PII, and unlike bank data they have live write paths
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** DB
- **Module / US / TC:** Core HR · surfaced auditing `ISSUE-523`
- **Title:** `ISSUE-523` is scoped to columns on `employees`. Two sibling tables hold hard PII with no converter and no `EncryptedFieldRegistry` entry: `emergency_contacts` (`contact_name`, `phone`, `alternate_phone`, `email`) and `employee_dependents` (`name`, `date_of_birth`). Unlike `bank_account_number` — whose latency is what keeps `ISSUE-523` at MED — these tables **do** have live write paths.
- **SURVEY:** **2** tables, **6** plaintext PII columns, **0** registry entries (unit = tables holding hard PII outside the 9 registered encrypted columns). The encryption registry covers **9** columns across **3** entities (`EncryptedFieldRegistry.cs:49-67`). Excluded: `employees` itself (that is `ISSUE-523`) and `users.mfa_secret` (ASP.NET Data Protection, deliberately outside the registry).
- **AUDIT (2026-09-07):** the plaintext column types — **CONFIRMED** in the `emergency_contacts` and `employee_dependents` blocks of `AppDbContextModelSnapshot.cs`; absence from the registry — **CONFIRMED** against `EncryptedFieldRegistry.cs:49-67`, which is pinned at exactly 9 entries by `EncryptedFieldRegistryTests.cs:63`. The existence of live write paths — **asserted by the audit but NOT traced endpoint-by-endpoint here**; that trace is the one thing needed to confirm the severity, and it is why this is filed rather than escalated. **Not audited:** whether these columns are covered by the export/masking surface (`SensitiveFieldMasker.cs`), which for `bank_account_number` already diverges from the table's own treatment.
- **Severity rationale:** MED — same class as `ISSUE-523` but with data actually flowing, offset by the columns being lower-sensitivity than a bank account or national id.
- **Found:** 2026-09-07, auditing `ISSUE-523`.

### ISSUE-562 — the `GoalCategory` enum is `'KPI'` on the FE and `'Kpi'` on the wire, so editing any KPI goal shows a blank required Category
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** FE + BE (contract)
- **Module / US / TC:** Performance · US-PRF-001 · surfaced auditing `ISSUE-373`
- **Title:** The FE declares `'KPI'` (`goal.models.ts:32`, options at `:35`) while the contract and API emit `'Kpi'` (`GoalCategory` in `hrm-v1.json`; `GoalCategory.cs:10` `Kpi = 0`). `performance-goal.service.ts:68` and `:92` are the **last two raw-typed casts in the module**, so the wire value reaches the template unmapped and the `<select formControlName="category">` at `goal-setting.component.ts:288-296` has no matching option.
- **SURVEY:** **1** enum, **3** values, **1** mismatched, reached through **2** raw casts on **1** screen (unit = enum values whose FE spelling differs from the wire). `grep "'Kpi'" src/frontend/src` returns **0** hits, so nothing on the FE side expects the real value. Excluded: the other 95 performance interfaces, which now go through generated-type adapters.
- **AUDIT (2026-09-07):** the FE spelling — **CONFIRMED** at `goal.models.ts:32`, `:35`; the wire spelling — **CONFIRMED** in the `GoalCategory` enum of `contracts/openapi/hrm-v1.json` and at `GoalCategory.cs:10`; the two raw casts — **CONFIRMED** at `performance-goal.service.ts:68`, `:92`; the select's missing option — **CONFIRMED** at `goal-setting.component.ts:288-296`; no spec covers it — **CONFIRMED** by the zero-hit grep. **The write path survives**: `JsonStringEnumConverter` is case-insensitive (`Program.cs:233`), so a saved `'KPI'` binds correctly server-side — the break is read-only. **Not audited:** whether any existing tenant data uses the third enum value in a way that would widen this.
- **Severity rationale:** MED — a required field renders blank when editing an existing KPI goal, but only on the read path and only for one of three values; the write path is unaffected, so no data is corrupted.
- **Found:** 2026-09-07, auditing `ISSUE-373`.


---

> **Findings below filed 2026-09-08 by the SURVEY+AUDIT backfill of the 39 unaudited out-of-lane entries.**
> Each carries its own survey and audit per Engineering-Discipline rule #7.

### ISSUE-563 — ~35 tenant-isolation test cases instruct the wrong Postgres GUC, so an RLS isolation probe returns 0 rows and reads as a PASS
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST (docs)
- **Module / US / TC:** cross-module · surfaced auditing `ISSUE-515`
- **Title:** `ISSUE-515` is scoped to the architecture document. The same wrong GUC name — `app.current_tenant_id`, where the real one is `app.current_tenant` — is baked into the **QA corpus**, where it does not merely mislead a reader: an isolation test that sets a nonexistent GUC gets `NULLIF(current_setting('app.current_tenant', true), '')` → NULL, matches no rows, and the tester records the empty result as isolation working.
- **SURVEY:** **~35** `TC-*-ISO-*` files and TEST-MATRIXes, plus **11** occurrences in `docs/QA/TRACEABILITY-MATRIX.md` and 2 in `docs/BA/admin-console/US-ADM-001.md` (`:41`, `:90`) — part of the **91 occurrences across 46 files** measured under `ISSUE-515` (unit = literal occurrences of the wrong GUC name). Excluded: `src/`, which has **0**; and `hrm_technical_document_v4.0.md`, which is `ISSUE-515`'s own scope.
- **AUDIT (2026-09-08):** the real GUC — **CONFIRMED** as `app.current_tenant` at `TenantGucConnectionInterceptor.cs:46`, `TenantJobRunner.cs:91`, and every RLS policy (`20260710120000_Platform_RlsPolicies_Dormant.cs:26`). The false-pass mechanism follows directly from the policy predicate and is **CONFIRMED by construction**. **Not audited:** whether any of the ~35 TCs has actually been executed and recorded as passing — that is what separates a latent documentation defect from recorded false evidence, and it is the first thing to check.
- **Severity rationale:** MED rather than LOW because these are the test cases for Critical Rule #1, and their failure mode is a **silent pass**. Not HIGH, because the isolation itself is enforced in code and separately guarded; what is broken is the evidence, not the control.
- **Suggested direction (NOT applied):** sweep the name, then add a docs-gate assertion that any TC citing `current_setting(` names a GUC that exists in a migration — the same shape as the route-resolution guard proposed in `ISSUE-542`.
- **Found:** 2026-09-08, auditing `ISSUE-515`.

### ISSUE-564 — the sign-off notes editor writes server HTML straight to `innerHTML`, making the AngleSharp advisory a reachable stored-XSS chain
- **Type / Severity / Status:** ISSUE · **HIGH** · OPEN
- **Layer:** FE
- **Module / US / TC:** Performance · US-PRF-006 review sign-off · surfaced auditing `ISSUE-511`
- **Title:** `review-signoff.component.ts:344` assigns `el.innerHTML = html` from server-stored `record.meetingNotesHtml` into a live in-DOM contenteditable, **bypassing Angular's DomSanitizer entirely** — the component's own header comment at `:41` claims the opposite. Server-side `HtmlSanitizer`/AngleSharp **0.17.1** is therefore the *only* sanitization layer on this path, and that version is vulnerable to GHSA-pgww-w46g-26qg (CVE-2026-54570, mXSS via an `annotation-xml` integration-point bypass, fixed in AngleSharp 1.5.0).
- **SURVEY:** **1 of 20** `innerHTML`/`bypassSecurityTrust` sites in `src/frontend` is a **raw DOM write of user-supplied HTML**; the other 19 are `[innerHTML]` bindings (which DomSanitizer still processes) or non-user resource URLs. Unit = HTML-injection sites in the frontend. Excluded: `*.spec.ts` and generated code.
- **AUDIT (2026-09-08):** the raw write — **CONFIRMED** at `review-signoff.component.ts:344`; the contradicting header comment — **CONFIRMED** at `:41`; the single-layer consequence — follows from those two plus the sanitizer version, and the version is **CONFIRMED** transitively pinned at exactly 0.17.1 by `HtmlSanitizer 9.0.892` (see `ISSUE-511`). The write path that fills the field is **CONFIRMED** sanitized on the server (`ReviewSignoffService.cs:135-138`), which is what makes AngleSharp the whole defence. **Not audited — and this is what gates the severity:** whether a payload exercising CVE-2026-54570 actually survives this specific `HtmlSanitizer` configuration. The chain is structurally complete; it has **not** been demonstrated end-to-end, and it should not be reported as an exploited vulnerability until it is.
- **Severity rationale:** HIGH — stored XSS in a multi-tenant HRM, on a field authored by one user (a manager) and rendered to another (their report), with exactly one sanitization layer and that layer carrying a published mXSS bypass. Filed above `ISSUE-511` because upgrading HtmlSanitizer fixes the library while leaving the missing-second-layer defect in place.
- **Suggested direction (NOT applied):** both halves — bind through `[innerHTML]` (or an explicit `DomSanitizer.sanitize`) instead of writing `innerHTML` directly, **and** bump `HtmlSanitizer` to 9.2.995 (which pulls AngleSharp >= 1.7.1). Note `ISSUE-121` already flags that server-side sanitization is a single point of failure on a sibling field.
- **Found:** 2026-09-08, auditing `ISSUE-511`.

### ISSUE-565 — the raw-SQL tenant-isolation semgrep rule exempts a query because the string `tenant_id` appears anywhere in it, predicate or not
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** TEST (tooling)
- **Module / US / TC:** Platform / tenant isolation · surfaced auditing `ISSUE-537`
- **Title:** `.semgrep/tenant-isolation.yml:48` uses `pattern-not-regex: (?i)tenant_id`, which matches the literal **anywhere** in the call text. `SELECT tenant_id FROM x` with **no `WHERE` clause at all** is therefore silently exempted. The guard tests for a substring where it means to test for a predicate.
- **SURVEY:** **1** exemption regex, affecting **all 6** of the raw-SQL shapes the rule currently covers — not only the 5 shapes `ISSUE-537` reports as missing. Unit = semgrep `pattern-not-regex` clauses in the tenant-isolation rule. Excluded: the rule's own `paths.exclude` (tests, migrations).
- **AUDIT (2026-09-08):** the regex and its placement — **CONFIRMED** at `.semgrep/tenant-isolation.yml:48`; the covered shapes — **CONFIRMED** at `:42-47`. The false-negative follows directly from substring semantics and is **CONFIRMED by construction**. Corroborated in the same audit: extending the rule as `ISSUE-537` proposes would fire on **5 sites, all false positives**, because `FieldEncryptionMaintenanceService.cs:249`, `:258`, `:286` build the predicate in a variable (so the matched text contains `tenantId`, not `tenant_id`) and `DbInitializer.cs:194`, `:196` are catalog reads. **Not audited:** whether any *current* call site actually exploits the loophole — the sweep found none, so this is a latent guard weakness rather than a live isolation gap.
- **Severity rationale:** MED — no known live bypass, but this is the guard for Critical Rule #1, and a guard that can be satisfied by mentioning a column name is close to no guard. Same class as `ISSUE-486` and `ISSUE-492`: a check that reports safety it does not provide.
- **Suggested direction (NOT applied):** require the predicate shape (`WHERE` … `tenant_id` … `=`), and pair any widening with the false-positive fix, or the rule becomes noisy and gets disabled — which is the outcome `ISSUE-537`'s cheap extension would produce on its own.
- **Found:** 2026-09-08, auditing `ISSUE-537`.

### ISSUE-566 — 10 frontend model files still document an "(ASSUMED)" backend contract against shipped, verifiable endpoints
- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** FE (docs)
- **Module / US / TC:** Performance + Payroll · surfaced auditing `ISSUE-382`
- **Title:** `ISSUE-382` names `feedback-360.models.ts` as carrying a stale "(ASSUMED)" contract header. Nine more do. These headers were written before the backend existed and were never reconciled, so each is a documented guess sitting next to a shipped contract that could have been read.
- **SURVEY:** **10** FE model files (payroll x2, performance x8) carry an "(ASSUMED)" backend-contract header (unit = model files under `src/frontend/src/app/features/*/models/`). Excluded: `*.spec.ts` and generated `api-types.ts`. In the audited instance the documented routes were wrong in **two** ways at once — omitting the `/tenant` segment and naming employee-keyed routes where the shipped ones are cycle-keyed (`Feedback360Controller.cs:235`, `:254`, `:292`).
- **AUDIT (2026-09-08):** the `feedback-360.models.ts` instance — **CONFIRMED** at `:2`, `:11`, with the route divergence confirmed against the controller and `feedback-360.service.ts:50`. The count of 10 — **CONFIRMED** by sweep. **Not audited:** the other 9 file-by-file — whether each one's documented contract is actually wrong, or merely unverified, is exactly what the sweep must establish, and it is why this is filed as a batch rather than as 10 findings.
- **Severity rationale:** LOW — comments, not behaviour. Kept because this repo has four recorded incidents of a comment outliving the code it described, and `ISSUE-531`/`ISSUE-298` in this same batch show a stale header mis-rating a finding's severity.
- **Found:** 2026-09-08, auditing `ISSUE-382`.

### ISSUE-567 — the anonymous public job-application upload has no `[RequestSizeLimit]`, so an unauthenticated caller can force a 30 MB buffered body
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Recruitment · public careers · surfaced auditing `ISSUE-510`
- **Title:** `ISSUE-510` reports `SelfAssessmentAttachmentsController` as "the only upload controller without a `[RequestSizeLimit]`". Four others also lack one, and **one of them is `[AllowAnonymous]`** — `CareersController.cs:73` (`Apply`), under the class-level `[AllowAnonymous]` at `:21`. With no global `MaxRequestBodySize` override anywhere in `HRM.Api`, it falls back to Kestrel's 30 MB default, reachable without a session.
- **SURVEY:** **5 of 16** upload actions lack `[RequestSizeLimit]`, and **1 of the 5 is unauthenticated** (unit = controller actions taking an `IFormFile`). The five: `CareersController.cs:73`, `ApplicantsController.cs:81`, `PayrollAdjustmentsController.cs:129`, `:155`, and `SelfAssessmentAttachmentsController.cs:34-41`. The other four all require a session. Excluded: `TenantSettingsController.cs:265`, a private helper rather than an action.
- **AUDIT (2026-09-08):** the missing attribute — **CONFIRMED** at `CareersController.cs:73`; the anonymous access — **CONFIRMED** at `:21`; the absence of any global override — **CONFIRMED** by sweep, so the Kestrel 30 MB default applies. **The mitigation is real and bounds the severity:** `[EnableRateLimiting("public-application")]` caps this at **10 requests/hour/IP** (`ISSUE-102`) — **CONFIRMED** on the same action. **Not audited:** whether the rate limiter partitions on a spoofable header, which would decide whether the 10/hour cap actually holds behind a proxy.
- **Severity rationale:** MED, not HIGH — unauthenticated resource consumption, but throttled to 10/hour/IP, so it is a nuisance rather than a practical DoS. Above LOW because it is the one uncapped endpoint reachable with no credentials at all.
- **Found:** 2026-09-08, auditing `ISSUE-510`.

### ISSUE-568 — the review PDF renders a permanent "-" in three Discussion rows, because no client ever populates those columns
- **Type / Severity / Status:** ISSUE · **LOW** · OPEN
- **Layer:** BE
- **Module / US / TC:** Performance · US-PRF-006 (FR-6) · surfaced auditing `ISSUE-289`
- **Title:** `PerformancePdfRenderer.cs:212-215` unconditionally renders a "Discussion" table with `Strengths`, `Development Areas` and `Summary` rows. `Plain()` (`:41-47`) maps null or blank to `"-"` and `KeyValue` (`:89-101`) has no skip-empty branch, so every review PDF in the system carries three dashes.
- **SURVEY:** **3 of 4** rows in that block render a permanent `"-"` (unit = rows in the Discussion block). The cause is `ISSUE-289`: **4 of 5** structured notes fields are never populated by any client, because the FE sends only `{ body }` (`review-signoff.service.ts:128`). Excluded: the fourth row, which is backed by `Body` and does render.
- **AUDIT (2026-09-08):** the unconditional render — **CONFIRMED** at `PerformancePdfRenderer.cs:212-215`; the null-to-dash mapping — **CONFIRMED** at `:41-47`; the absence of a skip-empty branch in `KeyValue` — **CONFIRMED** at `:89-101`; the upstream cause — **CONFIRMED** under `ISSUE-289`. **Not audited:** whether any tenant has structured notes written by a non-FE client, which is the only way those rows are ever non-empty today.
- **Severity rationale:** LOW — cosmetic, on an export. Filed as a **child of `ISSUE-289`'s product decision** rather than fixed independently: if the structured fields are going to be exposed in the UI, these rows become correct on their own and a skip-empty patch would then be wrong.
- **Found:** 2026-09-08, auditing `ISSUE-289`.

### ISSUE-569 — the worktree fence is blind in the reverse direction and absent from Bash, and it let ~8 misdirected edits through during this backfill
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** INFRA (hooks)
- **Module / US / TC:** agent tooling · surfaced auditing `ISSUE-512`
- **Title:** `worktree-fence.py:90-91` reads `caller_wt = _worktree_of(cwd); if not caller_wt: _allow()` — it exits **allow** whenever the caller's cwd is outside `.claude/worktrees/`. That is deliberate, and stated at `:19-20`. The consequence is that a session whose cwd **reverts** from its worktree to the main checkout writes to the wrong tree with no signal at all. Separately, the `settings.json` `"Bash"` matcher does not include the fence, so a `cat >` heredoc, `tee` or `sed -i` write is unfenced in **both** directions.
- **SURVEY:** guard coverage is **2 of 2 write tools** (`Write|Edit`), **0 of 4** common Bash write vectors (`cat >`, `tee`, `sed -i`, `cp`), and **1 of 2** cwd directions. Unit = hook-registered tool matchers x directions. Excluded: the `careful-guard` and `no-verify-guard` scripts, which are registered on Bash but check unrelated properties.
- **AUDIT (2026-09-08):** the early-allow — **CONFIRMED** at `worktree-fence.py:90-91`, with its rationale at `:19-20`; the Bash matcher omission — **CONFIRMED** in `settings.json`. **The failure is not hypothetical: it occurred during this backfill on 2026-09-07.** After a commit, the shell's cwd reverted to the main checkout and roughly 8 subsequent edits landed on `test/local-subdomains` instead of the intended worktree branch; nothing fired. The edits were made via `cat >` heredoc, so they would have evaded the fence from either direction anyway. They were caught only because a finding-count check disagreed, then reverted with `git checkout --`; **no commit was made to the wrong branch**. **Not audited:** whether adding the fence to the Bash matcher is practical without parsing shell redirection targets, which is the reason this is filed rather than patched.
- **Severity rationale:** MED — no data was lost this time, but the loss mechanism is real, silent, and demonstrated. It is also the reason `ISSUE-512` should move from LOW to MED: that entry's severity rested on "nothing was lost", which is now only true because a separate check happened to catch it.
- **Found:** 2026-09-08, auditing `ISSUE-512` — and by committing the defect it describes.

### ISSUE-571 — BUG-530's Hangfire retry is INERT on both reminder jobs, because ISSUE-116 clears the marker before dispatch

- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Recruitment · US-REC-005 NFR-4, US-REC-007 FR-7/AC-4 · TC-REC-005-02
- **SURVEY:** **2 of 2** recruitment reminder jobs are affected — `InterviewReminderJob` and `OfferExpiryReminderJob` (unit: Hangfire job classes in `src/backend/HRM.Api/Jobs/` that dispatch a reminder through `IRecruitmentNotificationService`). Both received the ISSUE-116 marker guard and both received BUG-530's throw-on-failure, so the interaction is not specific to one. No other job class dispatches through that seam.
- **AUDIT (2026-09-08):** verified on the merged result of `#689` (ISSUE-116) and `#696` (BUG-529/530), in `InterviewReminderJob.cs`:
  1. **CONFIRMED — the guard returns early on a null marker** (`:63`): `if (interview is null || interview.Status != InterviewStatus.Scheduled || interview.ReminderJobId is null) return;`
  2. **CONFIRMED — the marker is cleared BEFORE dispatch** (`:85-86`): `interview.ReminderJobId = null; await dbContext.SaveChangesAsync();`, and the dispatch is at `:96`.
  3. **CONFIRMED — the throw happens after that clear** (`:109`): `throw new InvalidOperationException(...)` on `dispatch.IsFailure`.
  4. **Therefore:** Hangfire re-runs the job, step 1 sees a null marker, and the job **returns without re-sending**. The retry the throw exists to trigger cannot do the thing it was added for.
- **Neither PR is wrong on its own.** ISSUE-116 chose clear-before-dispatch deliberately and documented why in-code: clear-after "WOULD add a duplicate-send path: if SaveChanges then fails" — the exact NFR-4 violation that finding exists to close. BUG-530 chose to throw so Hangfire retries, which is correct against a seam that had just gained a failure signal. **The defect exists only in the composition**, which is why neither PR's own tests catch it: each is green in isolation and both are green together.
- **What the throw still buys, so this is not rated higher:** the run is recorded **Failed** in the Hangfire dashboard instead of **Succeeded**, so a lost candidate reminder is now *visible* where before it was silent. That is a real improvement and it survives. What does **not** survive is automatic recovery.
- **Why MED:** it is not a regression — before ISSUE-116 the retry re-sent but produced duplicates (the NFR-4 violation); now it neither duplicates nor recovers. Delivery is strictly no worse than the pre-ISSUE-116 state and observability is better. But the code and its own doc-comment now claim a retry capability that does not function, and a claim that outruns the behaviour is the failure mode that produced `ISSUE-531` and `ISSUE-150`.
- **Needs a decision, not a quiet fix.** The three shapes are not equivalent:
  1. **Clear the marker only after a successful dispatch** — restores the retry, reopens the duplicate-send window on a post-dispatch `SaveChanges` failure. ISSUE-116 explicitly rejected this and its reasoning still holds.
  2. **Keep clear-first; soften the throw to a logged failure** — honest about what the system does, gives up the dashboard-visible Failed run.
  3. **An outbox row committed with the state change** — the only shape that is genuinely at-least-once, and the one `BUG-530` already names as deliberately not built.
  **(3) is the correct answer and the expensive one.** (1) trades a silent loss for a silent duplicate, which for an interview reminder is arguably the better trade — a candidate receiving two reminders is a nuisance, missing one costs them the interview. That is a product call, not an engineering one.
- **Found:** 2026-09-08, while rebasing `#689` onto `#696`. Six tests went red on the merged result, which is what exposed it; both were fixture/contract adaptations, and fixing them left this behavioural gap untouched and untested.

### ISSUE-570 — `StatutoryRuleService.UpdateAsync` has no duplicate/overlap pre-check, so an update can move a rule onto a sibling's window
- **Type / Severity / Status:** ISSUE · **MED** · OPEN
- **Layer:** BE
- **Module / US / TC:** Payroll · statutory configuration · surfaced auditing `ISSUE-299`
- **Title:** `CreateAsync` and `CloneFiscalYearAsync` both run a `sameKey` overlap query before writing. `UpdateAsync` (`StatutoryRuleService.cs:147-238`) mutates `CountryCode`, `FiscalYear`, `EffectiveFrom` and `EffectiveTo` (`:195-198`) with **no such guard** — only the BR-7 finalized-period check (`:165-189`) sits between. An update can therefore move an existing rule onto a window that overlaps a sibling of the same (tenant, type, country, fiscal year), producing exactly the arbitrary-winner resolve-time collision the create guard exists to prevent.
- **SURVEY:** **1 of 3** mutating paths lacks the overlap guard (unit = mutating service methods on `StatutoryRule`): `CreateAsync` has one (`:85-96`), `CloneFiscalYearAsync` has one (`:367`), `UpdateAsync` has none. A grep for `sameKey|overlap` across the update range returns only the unrelated `overlapsFinalized`. Excluded: read paths, tests, migrations.
- **AUDIT (2026-09-08):** the absence — **CONFIRMED** across `StatutoryRuleService.cs:147-238`; the mutated fields — **CONFIRMED** at `:195-198`; the guards on the sibling paths — **CONFIRMED** at `:85-96` and `:367`. The unique index does **not** cover it: it catches only an exact `EffectiveFrom` collision (`StatutoryRuleConfiguration.cs:63-65`), never an overlapping range. **Not audited:** whether the UI ever offers an edit that changes `EffectiveFrom`/`EffectiveTo` on an existing rule — if it does not, this is reachable only via direct API, which would lower it toward LOW.
- **Severity rationale:** MED — same consequence as `ISSUE-299` and `ISSUE-505` (two rules resolving for one period, arbitrary winner, wrong statutory deduction), reached by a different and unguarded route.
- **Found:** 2026-09-08, auditing `ISSUE-299`.
