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
| Type | Open | Other | Total |
|---|---|---|---|
| BUG | 54 | 3 retracted | 57 |
| ISSUE | 103 | 0 | 103 |
| ENH | 9 | 0 | 9 |

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
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** BE (absent) + FE
- **Module / US / TC:** Core HR / US-CHR-002 / (new TCs needed)
- **Title:** The employee-profile page has editable Education, Work-History, and Dependents forms, but there is **no backend entity, migration, DTO field, or endpoint** for any of them — `UpdateEmployeeProfileRequest` has no such sections and `EmployeesController` has no routes. They could never save (masked until now by the ISSUE-319 universal 404).
- **Root cause (~99%, confirmed):** FE built ahead of BE; grep for `EmployeeEducation`/`EmployeeWorkHistory`/`EmployeeDependent` entities returns nothing. Surfaced fixing DF-36/ISSUE-319.
- **Reproduction steps:** open an employee profile → the Education/Work-History/Dependents sections offer editing but there is nowhere to persist to.
- **Severity rationale:** MED — three profile sections are non-functional; net-new BE work (entities + migrations + endpoints + DTO fields). FE now shows them read-only (DF-36 / #380) so users aren't misled.
- **Suggested direction (needs-decision, NOT applied):** build Education/WorkHistory/Dependent entities + endpoints (→ DF-39), then re-enable the FE editing. Report only.

### ISSUE-320 — Employee profile-edit: several fields within the (now-working) sections still don't persist or risk invalid enum writes
- **Type / Severity / Status:** ISSUE · MED · OPEN
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
- **Type / Severity / Status:** ISSUE · LOW · DEFERRED platform tech-debt — US-PLT-002 RLS Phase 4)
- **Layer:** DB / INFRA
- **Module / US / TC:** Leave Management · US-LV-001 · TC-LV-ISO-003 (RLS blocks direct DB queries across tenants) — BLOCKED `env` (RLS not implemented). NFR-2.
- **Title:** US-LV-001 NFR-2 + the Data Requirements section ("RLS policy: `tenant_isolation_select` and `tenant_isolation_modify` on `leave_type`") require a database-enforced isolation layer in addition to the EF filter. Live DB inspection: `pg_class` for `leave_types` shows `relrowsecurity=f`, `relforcerowsecurity=f`, no policies on the table, and `SELECT count(*) FROM pg_policy` = **0** for the whole database. So the second, defense-in-depth isolation layer the story specifies does not exist — the EF global query filter is the only thing enforcing tenant scoping, and BUG-026/BUG-003 shows that single layer is bypassable via a spoofed subdomain header.
- **Root cause (~95%, DB confirmed):** RLS is platform-wide deferred work (tracked as US-PLT-002 RLS Phase 4 per project memory) — no migration creates the `tenant_isolation_*` policies named in the US data section, and no app code sets a per-request Postgres session GUC for an RLS predicate to read. This is pre-existing deferred scope, not a regression introduced by US-LV-001.
- **Reproduction steps (DB):** `SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname='leave_types'` → `f|f`; `SELECT polname FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid WHERE c.relname='leave_types'` → empty; `SELECT count(*) FROM pg_policy` → 0.
- **Evidence:** the three queries above; the only isolation in force is `AppDbContext.cs:229` (EF filter). TC-LV-ISO-003 cannot be executed (no RLS to test) → BLOCKED env.
- **Severity rationale:** LOW as a standalone finding (it is documented deferred tech-debt and the EF filter does scope correctly when the tenant is honestly resolved), BUT it removes the defense-in-depth that would have *contained* BUG-026/BUG-003 — with RLS keyed off the token's tenant, a spoofed subdomain alone could not leak data. So closing BUG-003 at the auth layer is the priority; RLS would be the backstop. Tracked here for US-LV-001 NFR-2 traceability, not as new work.
- **Suggested direction (NOT applied):** none — report only. (Deliver the US-PLT-002 RLS phase: enable `ROW LEVEL SECURITY` + `tenant_isolation_select/modify` policies on `leave_types` (and siblings) reading a per-request `app.tenant_id` GUC set from the validated token claim — which also requires the BUG-003 token-vs-subdomain fix to be the source of that GUC.)

### ENH-001 — No on-demand accrual/recalculation trigger endpoint: AC-5's "modify rule → Hangfire recalculates affected balances" and all accrual-effect verification depend on the daily recurring LeaveAccrualJob, which cannot be invoked via the API
- **Type / Severity / Status:** ENH · — · OPEN
- **Type:** ENH
- **Title / Module:** Add an authorized on-demand "recalculate entitlements / run accrual" endpoint · Leave Management · US-LV-002 (AC-5, FR-5)
- **Why it matters:** `LeaveEntitlementsController` exposes rules/overrides/effective but NO endpoint to trigger accrual or a post-rule-change recalculation. `LeaveAccrualJob` is only registered as a Hangfire **recurring** job ("leave-entitlement-accruals", daily midnight UTC, `Program.cs:460`) and `UpdateRuleAsync` does NOT `BackgroundJob.Enqueue` a recalculation. Consequences: (a) AC-5's "a Hangfire background job recalculates affected employees' balances" on rule modify is not wired — editing a rule changes future `/effective` computation but enqueues nothing and writes no adjustment ledger entries; (b) TC-026 steps 8-11, TC-027 (accrual arms), TC-028 steps 7-9/13, TC-029 ledger arms, TC-030 (whole Hangfire+adjustment flow), TC-032 ledger arms, TC-036 (accrual ledger), TC-037 (bulk recalc), and TC-041 (5,000-emp perf) cannot be executed on demand — the *engine math* is verifiable live via `GET /effective`, but the *ledger-writing accrual effect* is only observable after the scheduled job runs. An HR officer also has no way to force a recalculation after a policy change.
- **Suggested direction (NOT applied):** add an authorized `POST /leave-entitlements/recalculate` (and/or enqueue a recalculation from `UpdateRuleAsync`/bulk) that runs `ProcessAccrualsAsync` for the tenant (optionally scoped to a rule/leave type), writing accrual/adjustment ledger entries and (per BUG-028) audit rows. This both fulfils AC-5 and makes the accrual-effect TCs executable.

### ISSUE-036 — Attachment size limit (5MB/file, NFR-3) and tenant-scoped blob storage path are not implemented; the apply API accepts pre-uploaded attachment URLs only, so TC-LV-063's size-cap and storage-path arms cannot be enforced/verified
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Layer:** BE
- **Module / US / TC:** Leave Management · US-LV-003 · TC-LV-063 (steps 1-3 size cap, step 2 storage path), NFR-3, §10.
- **Title:** TC-LV-063 expects the leave-application attachment flow to reject files >5MB ("File exceeds 5 MB limit.") and to store files under a tenant-scoped blob path `{tenantId}/leaves/{requestId}/`. The current implementation does **not** do blob upload at all — `CreateLeaveRequestRequest.Attachments` is a list of **already-uploaded URLs** (the DTO comment states "blob upload is out of scope (NFR-3, deferred)"). Consequently there is no file-size validation (no bytes ever reach the API) and no server-side storage-path construction; attachment URLs are persisted verbatim into `leave_request.attachment_urls`. The **type** (PDF/JPG/PNG) and **count** (max 3) validations on those URLs DO work correctly.
- **Root cause (~95%, code confirmed):** `CreateLeaveRequestRequest` (`LeaveRequestDtos.cs:7-17`) takes `IReadOnlyList<string>? Attachments` (URLs), explicitly noting blob upload is deferred per NFR-3. `CreateLeaveRequestValidator` validates extension allow-list (`.pdf/.jpg/.jpeg/.png`) and `MaxAttachments=3`, but has no size rule and the service does no storage-path generation (`LeaveRequestService.CreateAsync` stores `attachments` as-is). NFR-3 blob storage is a deliberate, documented deferral.
- **Reproduction steps (live):** `POST /api/v1/leaves` with `attachments:["https://blob/acme/leaves/note.exe"]` → 400 "Attachments must be PDF, JPG, or PNG files." (type ✓); `attachments:["a.pdf","b.pdf","c.jpg","d.png"]` → 400 "A maximum of 3 attachments is allowed." (count ✓); `attachments:["https://blob/acme/leaves/certificate.pdf"]` → 201 with the URL stored verbatim. There is no request shape that conveys file bytes/size, so the 5MB cap and tenant storage path cannot be exercised.
- **Evidence:** type/count 400s above; 201 stores the URL unchanged in `leave_request.attachment_urls`; DTO/validator source shows no size rule and deferred blob upload.
- **Severity rationale:** LOW — a documented, deliberate scope deferral (NFR-3 blob storage), not a regression. The implemented validation (type + count) is correct. Flagged so the deferred size-cap + tenant-scoped storage-path acceptance criteria are tracked as not-yet-built rather than silently treated as passing.
- **Suggested direction (NOT applied):** none — report only. (When blob storage is implemented, add a per-file 5MB cap and server-side tenant-scoped path `{tenantId}/leaves/{requestId}/`, then TC-LV-063 steps 1-3 become executable end-to-end.)

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
- **Type / Severity / Status:** ISSUE · LOW · OPEN
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
- **Type / Severity / Status:** ISSUE · — · OPEN

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
- **Type / Severity / Status:** ISSUE · — · OPEN

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
- **Type / Severity / Status:** ENH · — · OPEN
- **Module / US / TC:** Recruitment / US-REC-006 / TC-REC-006-05, -08, ISO-015
- **Why it matters:** (a) The after-lock immutability arm (TC-006-08 step4) cannot be exercised at the API layer: the lock = `interview.StartsAtUtc + 48h` and interviews are validation-blocked from being scheduled in the past, so a locked state is unreachable without DB access or a 48h wait. (b) There is no single-scorecard `GET /scorecards/{id}` endpoint (TC-006-05 step2 / ISO-015 step2 both reference one) — scorecards are only reachable via the interview/applicant list endpoints, so the "fetch a peer's card directly by id" anti-bias/isolation arm has no surface to test (the list-level anti-bias hide IS enforced and PASSes). (c) Version history (BR-4) is explicitly deferred — edits replace the rating set wholesale (`ScorecardService.cs:123-130`); only the edit is audited.
- **Suggested direction:** expose a configurable lock-lead override (test hook) or a recruiter `GET /scorecards/{id}` read to make BR-4 verifiable; consider the deferred version-history for the edit trail. Not a defect — the lock logic itself is code-correct (`if (DateTime.UtcNow >= existing.LockedAt) → 409 scorecard_locked`, `ScorecardService.cs:115-117`).

### BUG-003 NOTE (systemic, already filed — NOT re-filed) — scorecard read/write surface inherits the cross-tenant root, is NOT self-protected
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

### ISSUE-121 · ISSUE · LOW · OPEN · BE — Agreed-action `description` is NOT HTML-sanitized on save (stored XSS reaches the DB), unlike the four rich-text note sections
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module / US / TC:** Performance / US-PRF-006 / TC-PRF-006-10 (FR-2/S10 sanitization)
- **Title:** `<script>evil()</script>do thing` submitted as a meeting-notes agreed-action description is persisted **verbatim/unsanitized** in `review_meeting_notes_action.description`, whereas the Body/Strengths/DevelopmentAreas/Summary rich-text fields ARE correctly sanitized (script/onerror/javascript: stripped).
- **Root cause (confidence 95%):** `UpsertNotesAsync` sanitizes the four section fields (`_sanitizer.Sanitize(input.Body/Strengths/DevelopmentAreas/Summary)`, `ReviewSignoffService.cs:135-138`) but stores each action with only `a.Description.Trim()` — no `_sanitizer.Sanitize(...)` call (`ReviewSignoffService.cs:151-160`). Verified live: stored row `description = '<script>evil()</script>do thing'`. The SQLi-style string (`'; DROP TABLE …;--`) was stored as inert literal text (parameterized persistence — table survived), so only the action-description sanitization is the gap.
- **Reproduction:** acme; HR/manager `PUT …/notes` with `"actions":[{"description":"<script>evil()</script>do thing","deadline":"2026-12-31"}]` → 200; `SELECT description FROM review_meeting_notes_action WHERE description LIKE '%script%'` → unsanitized script tag present.
- **Evidence:** DB row `<script>evil()</script>do thing`; for contrast `body` saved as `<p>ok</p><img src="x">` (script + onerror stripped).
- **Severity rationale:** LOW — defense-in-depth: exploitability depends on whether the FE renders the action description as raw HTML (if rendered as text it's inert); the four primary note fields are sanitized. Should be aligned (sanitize the action description too) since S10 mandates "rich text stored as sanitized HTML" for the notes including FR-2's agreed actions.

### ENH-012 · ENH · BE — Sign-off employee read-path + per-cycle auto-close window have no test/observability hooks; HR resolver signer-name falls back to email
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
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Module / US / TC:** Performance / US-PRF-007 / TC-PRF-007-11 (NFR-1/NFR-3), TC-PRF-007-13 (BR-4), TC-PRF-ISO-028 (cache/refresh tenant-scoping)
- **Title:** NFR-3 + BR-4 + Data §7 specify a `performance_summary` PostgreSQL materialized view, Redis read-through caching, and a tenant-configurable Hangfire refresh job (default 4h) backing the dashboard aggregates. None exist: the `performance_summary` table is absent (`information_schema.tables` count = 0), there is no Redis cache layer for the dashboard, and no dashboard/materialized-view refresh recurring job is registered (the only PRF recurring jobs are `CyclePhaseTransitionJob` and `SelfAssessmentReminderJob`). All aggregates are computed **live** on each request via tenant-scoped EF queries + in-memory GroupBy.
- **Root cause (~99%, code):** `PerformanceDashboardService` class-doc explicitly marks this an extension point ("these aggregates are computed LIVE on each request … A future story can introduce a performance_summary materialized view refreshed every 4 hours by Hangfire + a Redis read-through cache", `PerformanceDashboardService.cs:30-33`). `performance_summary` appears only in comments. No `RecurringJob.AddOrUpdate` for a dashboard refresh in `Program.cs`/`HangfireCyclePhaseScheduler.cs`.
- **Reproduction steps:** (a) `SELECT count(*) FROM information_schema.tables WHERE table_name='performance_summary'` → 0. (b) `grep` recurring-job registrations → no dashboard/materialized-view refresh. (c) Consequently TC-007-13 (refresh seam: "new review not reflected until the view refreshes") is contradicted — the dashboard reflects a new submitted review **immediately** because it reads live data, not a stale view.
- **Evidence (live, today):** `performance_summary` table count 0; recurring-job list = TokenCleanup/DocumentExpiry/…/CyclePhaseTransition/SelfAssessmentReminder (no perf-dashboard refresh). Live overview reflects the seed instantly.
- **Severity rationale:** LOW — explicitly documented as a deferred extension point in the service contract (the DTOs are the stable seam), and functional correctness is preserved (live aggregates are exact and tenant-isolated); the gap is the NFR-3/BR-4 performance/caching MECHANISM, which matters only at the 5,000-employee scale (TC-007-11, not seedable here). Recorded so the deferral is traceable. NFR-2's separately-stated "PostgreSQL RLS on the view" is likewise N/A (no view) — isolation is EF query filters (see BUG-003 note below).
- **Suggested direction (NOT applied):** none — report only.

### ENH-013 · ENH · Performance/US-PRF-007 — Test-enablement + minor scope-rejection polish for the dashboard
- **Type / Severity / Status:** ENH · — · OPEN
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

### ISSUE-135 — FR-7 PIP summary report (PDF) endpoint/seam ABSENT (no `/report`, `/export`, `/pdf` route)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PRF-008 / TC-PRF-008-14
- **Title:** FR-7 requires a PIP summary report (objectives/checkpoints/outcomes/signatures). No export endpoint exists on `PipController` — `GET /pips/{id}/{report|export|pdf|summary}` all 404. The TC's "PDF renderer conditional" caveat presupposes an export *seam* returning the structured model; there is none.
- **Root cause (confidence 96%):** `PipController` exposes only List/Get/Create/Acknowledge/Checkpoints/Outcome/Escalation — no report action; no `IPipReportService`/export handler in the Performance feature. Consistent with the module's deferred-PDF pattern (US-PRF-005/006/007) but here the entire endpoint is missing, not just the renderer.
- **Reproduction:** `GET /api/v1/tenant/performance/pips/{id}/report` (and `/export`,`/pdf`,`/summary`) as HR -> 404.
- **Evidence:** all four export paths -> 404; controller has no report route.
- **Severity rationale:** FR-7 is a should/compliance-nice-to-have reporting feature; the full PIP data model is already retrievable via `GET /pips/{id}` (objectives+checkpoints+events), so the compliance data exists — only the packaged report is absent. LOW.

### ISSUE-136 — PIP write operations write NO central `audit_logs` row (FR-5 satisfied via PIP-internal `pip_event`, but central audit trail is bypassed)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
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
- **Type / Severity / Status:** ISSUE · LOW · OPEN
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
- **Type / Severity / Status:** ENH · — · OPEN
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

### ISSUE-299 — `StatutoryRuleService` create-time overlap check compares the STORED CountryCode raw, so a dirty-cased/whitespace row escapes the duplicate guard
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PAY-006 / (auto-healed OUT-OF-LANE from the TAX-3 normalization guard, PR #301)
- **Title:** `StatutoryRuleService` create-time overlap/duplicate pre-check compares `r.CountryCode == countryCode` (normalized incoming vs RAW stored). A whitespace/case-dirty existing row (e.g. `"lk"`/`"LK "` from a raw/seed/import write) escapes the overlap check, so a second overlapping same-country rule can be created. The resolver's new `upper(btrim(country_code))` match (PR #301) would then see BOTH → `SelectEffectiveByType` picks latest-`EffectiveFrom` arbitrarily — the resolve-time collision the guard was meant to prevent.
- **Root cause (confidence 85%):** pre-existing raw-compare in the create overlap query; the tenant-scoped 5-col unique index is the hard backstop for EXACT-cased dupes, but not for dirty-cased near-dupes. The PR #301 `upper()` match makes a previously-invisible dirty row newly matchable, so it can surface this latent collision.
- **Suggested direction (NOT applied):** normalize the stored side in the overlap query too (`upper(btrim(...))`), and/or a one-off data-cleanup/normalize-on-read for `CountryCode`. Or accept as LOW given the unique index backstop.
- **Severity rationale:** LOW — requires a dirty-cased row (only via a service-bypassing write) AND an overlapping create; no cross-tenant/cross-country mis-tax (still country + tenant scoped).

---

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

### ISSUE-301 — F&F `TenantFnFPolicy` accepts the semantically-dangerous flag combo `IncludeProRatedFinalPay=true` + `FinalPeriodOwnedBySettlement=false` (latent double-pay, not live)
- **Type / Severity / Status:** ISSUE · LOW · OPEN
- **Type:** ISSUE (policy-modeling / needs-decision) · **Severity:** LOW · **Status:** OPEN · **Layer:** BE · **US/TC:** US-PAY-* (F&F) / (auto-healed OUT-OF-LANE from PR #303 integration-enforcer)
- **Title:** `CreateFnFPolicyValidator` has no cross-field rule: a tenant can save a policy where F&F pays the pro-rated final month (`IncludeProRatedFinalPay=true`) while NOT owning the final period (`FinalPeriodOwnedBySettlement=false` → the run-guard won't exclude the employee). Today this is **latent, not live**: `OffboardingService.CompleteAsync` sets `Status=Terminated`+`IsActive=false` in the same transaction before the settlement exists, so the run's `IsActive && (Active||Probation)` filter excludes the employee regardless of the flag.
- **Root cause:** the two policy booleans are independent with no coupling validation. Confidence: HIGH (enforcer traced the flag path).
- **Suggested direction (NOT applied):** add a cross-field rule (`IncludeProRatedFinalPay ⟹ FinalPeriodOwnedBySettlement`) OR document the decoupling intentionally. **⚠ Would become a LIVE double-pay** if offboarding termination timing ever changes (e.g. deferring the status flip to the LWD).
- **Severity rationale:** LOW — not exploitable today (the in-tx termination masks it); a guard against a future refactor + a product decision on the flag semantics.

---

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
- **Type / Severity / Status:** ISSUE · LOW · OPEN (found by the 2026-07-11 RLS validation)
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

### ISSUE-295 — BUG-079 residual clauses: encashment daily-rate BASIC basis, null carry-forward-limit, and gate-vs-year-end forfeitable parity
- **Type / Severity / Status:** ISSUE · LOW · DEFERRED (2026-07-19 — user decision: fold into a focused payroll-model pass, DEFERRED-FOLLOWUPS DF-37; a piecemeal component-keying change risks pay math)
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
- **Type / Severity / Status:** ISSUE · LOW · OPEN
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

### ISSUE-383
- **Type / Severity / Status:** ISSUE · LOW · OPEN

- **Type:** ISSUE · **Severity:** LOW · **Status:** OPEN · **Layer:** Backend (dev-only)
- **Module:** Platform (cross-cutting) · **US:** GAP-033a (§23.4)
- **Found:** 2026-08-21, by `@integration-enforcer` (out-of-lane flag).
- **Summary:** Swagger UI and `swagger.json` responses do not carry the six §23.4 security headers. `UseSwagger`/`UseSwaggerUI` are registered inside the `IsDevelopment()` block **earlier** in the pipeline than the header middleware, so Swagger terminates the request before the headers are written.
- **Severity rationale:** LOW because the whole Swagger block is `IsDevelopment()`-gated and therefore absent in production. It is a correctness wart in the ordering, not a production exposure.
- **Suggested fix:** move the header middleware above the dev-gated Swagger block. Cheap, but deferred out of the E1 PR because it reorders a block E1 did not otherwise touch.

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
- **Type / Severity / Status:** ISSUE · LOW · OPEN

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
- **Type / Severity / Status:** ISSUE · MED · OPEN

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

### ISSUE-403
- **Type / Severity / Status:** ISSUE · HIGH · OPEN

- **Type:** ISSUE · **Severity:** HIGH · **Status:** OPEN · **Layer:** FE
- **Module:** payroll (US-PAY-006) · **Found:** 2026-08-23, D1 payroll migration.
- **Summary:** Two more statutory gaps found alongside BUG-317:
  - **Test-calculation always returns zeros.** The FE never sends `countryCode`, and `StatutoryDeductionResolver.ResolveAsync` deliberately resolves nothing without one ("NEVER apply an arbitrary country's rules"). So FR-5's preview cannot validate a slab config however it is set up.
  - **Exemptions and cumulative PAYE are unreachable.** The backend has both fully built (`PayrollExemptionDto`, `ExemptionCalculationType`, `isCumulative`, with Postgres tests). The FE view models carry no field for either, so every rule is created non-cumulative with zero exemptions — a wrong tax deduction on a real payslip for any tenant needing them.
- **Related:** BUG-317 · D1

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
- **Type / Severity / Status:** ISSUE · MED · OPEN
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
- **Type / Severity / Status:** ISSUE · MED · OPEN
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
- **Type / Severity / Status:** ISSUE · MED · OPEN
- **Layer:** TEST (architecture)
- **Module / US / TC:** cross-module · generalises GAP-022
- **Title:** GAP-022's shape: `PayrollOvertimeCalculator.Compute` gained `fte` and `fteScaledBase` as trailing optionals, `PayrollRunProcessor` was never updated to pass them, the parameters were inert on the only production path, and **the entire suite stayed green** — because the calculator's own unit tests supply the arguments directly. `OvertimeFteBaseTests.cs:10-13` even records in its header that it proves "the MATH, not the plumbing", and it stayed broken anyway. A written admission of an untested seam is not a control.
- **Root cause + confidence (~90%):** nothing asserts that a domain calculator's optional parameters are actually supplied by a non-test caller.
- **Severity rationale:** MED — this is a money-path defect generator. It produced a silent underpayment once already.
- **Suggested direction (NOT applied):** a NetArchTest/architecture rule flagging any domain calculator whose optional parameters are never supplied by a production caller — natural work for queue item `E2` (`HRM.ArchitectureTests`), which does not yet exist. Failing that, a manual sweep of every domain calculator's call sites.

