# Pass A11 — reports requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **Depth:** 4 Must-Have stories at AC level (20 ACs) + 1 Should-Have at story level = **21 rows**
> **Status:** ✅ VALIDATED — 3 of 3 orchestrator spot-checks confirmed.
> **Headline:** **refutes my brief's contract-drift expectation for this module, with evidence** — every reports DTO↔interface pair is aligned. Five ledger findings are stale in the *pessimistic* direction.

> ⚠ *Recovered after turn-budget exhaustion. The auditor states per-story coverage in `## CONFIDENCE`.*

## Orchestrator validation

| Claim | Result |
|---|---|
| ISSUE-197 batch CTC resolver genuinely present | ✅ **Confirmed.** `PayrollReportService.cs:726` calls `ResolveManyAsync(year, month, items, null, ct)`; interface `IStatutoryDeductionResolver.cs:102`. The comment at `:673` states the rationale: *"a 5,000-employee report would issue 5,000 queries; `ResolveManyAsync` loads once per distinct country."* |
| `PayrollReportExport` has no EF filter — **but does have dormant RLS** | ✅ **Confirmed, and this usefully refines Pass E.** Migration `20260713150607_…:53-68` creates a dormant `tenant_isolation` policy under the NEW-TENANT-TABLE RULE. So **RLS covers it when `Rls:Enabled=true`** — the EF layer is what is missing. |
| `GenerateAsync` has neither tenant nor owner check | ✅ **Confirmed.** `PayrollReportExportService.cs:194-197` — `FirstOrDefaultAsync(e => e.Id == exportId)`, then straight to a 404 on null. **No `TenantId`, no `RequestedByUserId`.** |

### 🔵 The auditor refuted my brief — correctly, and with a field-by-field diff

My brief told it *"the single worst instance found so far is a reports feature"* (US-LV-012's `res.items` vs `Columns`/`Rows[].Cells`). It checked and **refuted the expectation for US-RPT**:

- `HrReportResult{Metadata, Charts, Table}` ↔ `IReportResult{metadata, charts, table}`; `HrReportTable{Columns, Rows: object?[][], CellBands}` ↔ `IReportTable{columns, rows: ReportCell[][], cellBands}` — **exact match**, and the viewer iterates `r.table.rows` as a 2-D array, **not** `res.items`.
- Payroll and Dashboard DTO pairs likewise aligned; all FE URLs and verbs match the controller attributes.
- The envelope is unwrapped globally by `api-envelope.interceptor.ts:49`, so `ApiResponse<T>.Ok(...)` on the controller is **correct, not drift**.

**Its conclusion, which I accept: the US-LV-012 defect lives in the leave-management feature's own FE, not in `features/reports/`.** US-RPT-002's leave reports are served through the aligned generic viewer.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| RPT-001 AC-1 | Report catalog: 6 types w/ description, icon, Generate | Must | IMPLEMENTED | `HrReportDtos.cs:56-71`; `HrReportsController.cs:35-39`; FE `report-catalog.component.ts:66-112,301`; route `app.routes.ts:344` | Descriptions are FE-owned i18n keys derived from `type` — **naming drift, not a gap** |
| RPT-001 AC-2 | Headcount Summary + filters, dept bar chart, tenant-scoped | Must | PARTIAL | `HrReportService.cs:133-177`; test `HrReportIntegrationTests.cs:136-164` | **leg2**: the FE department filter is a **free-text comma-separated GUID input** (`report-viewer.component.ts:232-240,1161`) — the AC's "department = Engineering" selection is not achievable in the UI |
| RPT-001 AC-3 | Turnover: separations, vol/invol %, trend, by dept, avg tenure | Must | IMPLEMENTED | `HrReportService.cs:183-271` (BR-3 rate `:211-213`, avg tenure `:232-236`) | Avg-headcount denominator is an **in-code documented approximation** (`:206-211`), not a snapshot table |
| RPT-001 AC-4 | Demographics: gender, 5-yr age histogram, dept, location, **diversity metrics** | Must | PARTIAL | `HrReportService.cs:281-345` | **leg1**: "diversity metrics" is not computed — only counts of gender groups / age bands (`:319-325`). Everything else present |
| RPT-001 AC-5 | Tenant A vs B isolation via RLS + EF filters | Must | IMPLEMENTED | Filter `AppDbContext.cs:299`; `HrReportService.cs:1073-1078`; guard `TenantAccessGuardMiddleware.cs:7,45` wired `Program.cs:595-599`; tests present | **ISSUE-193/BUG-003 (the 2026-06-27 leak) is fixed** |
| RPT-002 AC-1 | Leave Utilization incl. **top-10 highest consumers table** | Must | PARTIAL | `HrReportService.cs:474-528` (bar, dept, pie all present) | **leg1 fails on the top-10 table**: rows come from `LeaveReportType.Utilization`, whose columns are `Department, Leave Type, Total Entitlement, Total Used, Utilization %` — **no per-employee ranking anywhere** |
| RPT-002 AC-2 | Leave Balance table w/ green/yellow/red bands | Must | IMPLEMENTED | `HrReportService.cs:535-609` (BR-1 `:573`, bands `:578`); DTO `CellBands`; FE reads + renders `report-viewer.component.ts:1176-1178,374-384` | **Band matrix aligned field-for-field** |
| RPT-002 AC-3 | Attendance Summary: 6 metrics + OT bar + absenteeism line | Must | IMPLEMENTED | `HrReportService.cs:672-752` | Reads the US-ATT-007 summary table; **no recompute** |
| RPT-002 AC-4 | Manager sees only direct reports; HR sees all | Must | IMPLEMENTED | `HrReportService.cs:1007-1048`; perms `PermissionCatalog.cs:296,312`; **cache keyed by scope** `:89-93`; test `HrLeaveAttendanceReportIntegrationTests.cs:373` | **ISSUE-195 is fixed** — reverse drift |
| RPT-002 AC-5 | Tenant isolation on leave/attendance reports | Must | IMPLEMENTED | `AppDbContext.cs:363,367`; guard `:45` | **BUG-086 fixed** with a Postgres-container regression test |
| RPT-003 AC-1 | Payroll Run Summary + MoM comparison, tenant currency | Must | IMPLEMENTED | `PayrollReportDtos.cs:213-243`; `PayrollReportsController.cs:98-103`; FE `payroll-report.models.ts:122-145`, `payroll-reports.component.ts:271-291` | FE↔BE aligned |
| RPT-003 AC-2 | Department salary distribution: **stacked** bar + per-dept table | Must | PARTIAL | `PayrollReportDtos.cs:22-24`; controller `:98-103,123-127`; FE `payroll-report.service.ts:98` | **The auditor's least-confident row (60%)** — it did not open the department-summary builder to confirm the basic/HRA/allowance split the *stacked* bar requires |
| RPT-003 AC-3 | Statutory Deductions: monthly + YTD cumulative, downloadable | Must | IMPLEMENTED | `PayrollReportService.cs:480`, fiscal-year YTD `:553-591`; export `:190-195` | ISSUE-176 fiscal-year YTD honoured per country |
| RPT-003 AC-4 | Bank Advice, **masked by default**, exportable | Must | IMPLEMENTED | Mask `PayrollReportService.cs:954,990,1019`, `MaskAccount:1720-1722`; reveal gated `[RequirePermission("Payroll.ViewSensitive")]`; **audit** `:165` | NFR-3 PII audit satisfied |
| RPT-003 AC-5 | Tenant B sees only its payroll data | Must | IMPLEMENTED | Guard `:45`; filters `AppDbContext.cs:404,408,412`; test `PayrollReportIntegrationTests.cs:784` | **Caveat:** the export ledger on this path is unfiltered — GAP-1 |
| RPT-004 AC-1 | Export → CSV / xlsx / PDF | Must | IMPLEMENTED | FE `report-viewer.component.ts:945-949`, permission gate `:952`; BE `HrReportsController.cs:79-103` | |
| RPT-004 AC-2 | Excel via ClosedXML incl. **chart sheet** + SignalR notification | Must | PARTIAL | Renderer `HrReportRenderer.cs:88-141` (BR-6 "Filters Applied" `:107-117`); SignalR `HrReportExportService.cs:236-247`; async job present | **leg1 fails on the chart sheet** — `RenderXlsx` writes exactly one worksheet. **`includeCharts` is accepted end-to-end and never read** — the "CRUD'd but never read" class |
| RPT-004 AC-3 | PDF via QuestPDF incl. **tenant logo** + **charts as images** | Must | PARTIAL | `HrReportRenderer.cs:144-205` (header, filters, table, page numbers all present) | **leg1 fails twice**: no chart images (**deliberate, documented** `:26-29` — no server-side renderer) and **no tenant logo** (only the tenant *name* string at `:198`). **The logo is not covered by that deferral — a plain miss** |
| RPT-004 AC-4 | CSV: UTF-8 BOM, RFC-4180 escaping | Must | IMPLEMENTED | `HrReportRenderer.cs:60-70` via shared `CsvExport.cs:17-26`; siblings aligned (`PayrollReportRenderer.cs:51`, `LeaveReportService.cs:1194`, `OvertimeService.cs:713`); **three BOM tests** | **ISSUE-198 is fixed** — reverse drift |
| RPT-004 AC-5 | **Signed** tenant-scoped URL; Tenant B → 403 | Must | PARTIAL | HR path solid: filter `AppDbContext.cs:688-690`, owner check `:319-321`, tenant path + `EncryptingReportExportStorage` | Three shortfalls: **(a) no signed URL / 15-min expiry** (documented deferral `:32-35`); (b) returns **404 not the AC's 403** (deliberate `:316-318`); **(c) the payroll export sibling has no query filter** — GAP-1 |
| **US-RPT-005** | Role-based KPI dashboard | Should | PARTIAL | `DashboardService.cs:120-149`; HR 8 widgets `:184-205`, Manager 6 `:212-245`, Employee 6 `:250+`; **role server-derived** `:159-175`; click-through DTO `:78-84`; cache 3 min; FE + route + 3 test suites | **FR-6 Announcements/Activity-Feed widget does not exist** (zero grep hits). **BR-5 module gating** is a documented deferral. **ISSUE-199 still open** — `hasDirectReports` alone promotes to `"manager"` (`:172-174`) |

---

## CONTRADICTIONS

**All five stories marked `[x]`; four are not fully done.** 7 of 21 rows are PARTIAL. **This is ordinary ledger optimism, not fabrication — the engine is genuinely built.**

### 🔵 Reverse drift — FIVE findings the ledger carries as open that the code shows fixed

`TEST-STATUS.md:215` is stale in the pessimistic direction:

| Ledger claim (verbatim) | Contradicting evidence |
|---|---|
| **ISSUE-193 HIGH** — *"acme JWT + X-Tenant-Subdomain:techoneglobal → every report returns techoneglobal aggregates … all 6 report types"* | `TenantAccessGuardMiddleware.cs:7,45` rejects JWT-tenant ≠ host-tenant; wired `Program.cs:595-599`; regression test present |
| **ISSUE-195 MED** — *"no `Reports.View.Team` perm exists; Manager sees FULL tenant; BR-2 team-scope unimplemented"* | `PermissionCatalog.cs:296,312`; **every** HR report now calls `ResolveScopeAsync` + `ApplyScopeToEmployees` (7 call sites); test `:373` |
| **BUG-086 HIGH** — *"`entry_type='Accrued'` … EF throws at three sites"* | Tolerant converter + **Postgres-Testcontainer regression** `LeaveLedgerLegacyEntryTypeTests.cs:1-13` |
| **ISSUE-198 LOW** — *"BOM inconsistency: payroll + leave CSV writers omit it"* | Shared `CsvExport.Utf8Bom` used by all three writers; **three BOM tests** |
| **ISSUE-197 LOW** — *"CTC employer-contributions col = 0.00"* | Real resolver, **batched once per distinct country** `:683-735`; ×12 annualisation `:734`; batch-vs-single agreement tests. **The no-resolver path deliberately returns empty rather than the old proxy** (`:687-691`): *"A blank column is honest; a wrong one is not."* |

---

## GAPS RANKED

1. **GAP-1 — `PayrollReportExport` has no EF global query filter. HIGH, S.** `AppDbContext.cs:174` declares the DbSet; the only export filter is `HrReportExport` at `:688-690`, and the configuration class adds none. **Two code comments assert a filter that does not exist** (`PayrollReportExportService.cs:22-24,307`). *Mitigated* by a dormant RLS `tenant_isolation` policy (migration `:53-68`) — **so not currently exploitable with RLS on** — but three unfiltered reads exist: `ListAsync:274`, the concurrency count `:101`, and **`GenerateAsync:194`, which has neither tenant nor owner check.** The `IsDeleted` soft-delete filter is absent entirely. *Fix:* one `HasQueryFilter` line beside `:690` + delete the two false comments.
2. **GAP-2 — PDF has no tenant logo. S** (charts-as-images is an **L** and a documented deferral). **The logo is not covered by that deferral.** The tenant lookup already exists at `HrReportExportService.cs:401-410`.
3. **GAP-3 — `includeCharts` is plumbed end-to-end and never read. M / S.** *"Either add a ClosedXML chart sheet, or delete the parameter so the API stops advertising a capability it lacks."*
4. **GAP-4 — no top-10 leave-consumers table. M.**
5. **GAP-5 — department filter is a raw-GUID text box. M.** The AC's flow is **not performable by a user**.
6. **GAP-6 — no Announcements / Activity-Feed widget (FR-6). M.**
7. **GAP-7 — ISSUE-199 dashboard persona is org-position-derived. S.** HR resolution *was* moved onto permissions (`:163-169`), so this is **half-fixed**.
8. **GAP-8 — "diversity metrics" not computed. S** — *needs a BA definition first; the AC term is itself under-specified.*
9. **GAP-9 — storage layout + signed URLs. L.** Documented deferral; the authenticated tenant+owner endpoint substitutes. **Decision, not defect.**

---

## COVERAGE SUMMARY

```
Rows: 21 | IMPLEMENTED: 12 | PARTIAL: 8 | MISSING: 0 | CONTRADICTED: 0 as a row verdict
```

*(All contradictions here are reverse drift plus blanket STATUS.md optimism — **no single AC met the "ledger says done, code says nothing there" bar**, which is why none is tokenised.)*

**Where failures concentrate: leg 1, in the export renderers** — 3 of 8 PARTIALs are `HrReportRenderer` shortfalls (chart sheet, chart images, logo) — **and leg 2 in the Angular filter bar.** Notably **not** in the FE/BE contract. Tenant isolation is strong everywhere except the one unfiltered export ledger.

---

## CONFIDENCE

**Thorough (95%+):** RPT-001 (5 ACs), RPT-002 (5), RPT-004 (5), RPT-005 (story-level, all 5 spot-checked in both layers). All three brief leads settled: contract diff **refuted** (95%), ISSUE-197 **confirmed present** (95%), `PayrollReportExport` filter **confirmed missing** (98%, RLS mitigation established at 90%).

**RPT-003 AC-2 — 60%, the least-confident row.** Report type, route and FE consumer confirmed; the department-summary **builder body was not opened** to check the basic/HRA/allowance split the stacked bar requires. *Settled by:* reading the `DepartmentSummary` branch and the `DepartmentCosts` analytics series.

**RPT-003 AC-1/3/4/5 — 85%:** verified via DTOs, controller attributes, service line evidence and FE, **but those service bodies were read by targeted grep rather than end to end.**

**Pass-B leads settled in passing:** the **training participation report is CONFIRMED UNCOVERED** — no US-RPT AC mentions training, and `TrainingController.cs:33-154` exposes courses/enrollments/history only, **zero report endpoints** (95%). The four platform-reporting capabilities returned zero hits — **though `ApiCallCounter.cs` exists, so "API call volume" may have partial substrate.**

**Limits:** static reading only; nothing executed. **The RLS mitigation for GAP-1 depends on `Rls:Enabled` being true in the target environment, which only a running stack confirms.**

---

## OUT-OF-LANE

- **type:** risk · **severity:** HIGH · **where:** `AppDbContext.cs:174` + `PayrollReportExportService.cs:22-24,307,194` · **what:** no EF filter, no soft-delete filter, two comments asserting a filter that does not exist, and `GenerateAsync` reading by id with neither tenant nor owner check. · **suggested-action:** add the `HasQueryFilter` mirroring `:688-690`, delete the false comments, and add a cross-tenant `ListAsync`/`GenerateAsync` regression alongside the existing HR export tests.
- **type:** doc-drift · **severity:** MED · **where:** `PayrollReportService.cs:737-752` · **what:** the doc-comment above `BuildCtcReportAsync` still describes employer contributions as *"APPROXIMATED as a 1:1 employer match"* — **the exact pre-ISSUE-197 behaviour the resolver replaced.** · **suggested-action:** rewrite to point at the resolver. **This is the same failure mode the team already called out at `PayrollReportDtos.cs:31-38` ("a stale comment turns into wasted or duplicated work") — worth a sweep.**
- **type:** doc-drift · **severity:** MED · **where:** `TEST-STATUS.md:215` · **what:** five findings carried as open that are fixed with bound tests. · **suggested-action:** run `/verify-fix` for each — **the regression tests already exist, so the re-run should be cheap.**
- **type:** test-integrity · **severity:** LOW · **where:** `HrReportService.cs:1015-1018` · **what:** `ResolveScopeAsync` defaults to full-tenant `"All"` when `_currentUser is null`, explicitly for test constructors — so **a unit test built without an `ICurrentUser` exercises the unscoped path while looking like it validated scoping.** · **suggested-action:** have `@test-authenticator` check that the RPT-002 AC-4 scope assertions pass a real `ICurrentUser`.
