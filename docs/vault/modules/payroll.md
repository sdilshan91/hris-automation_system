---
type: module-note
module: payroll
---

# Payroll

Domain rules, edge cases, and decisions for the Payroll module.

## Salary structures + components (US-PAY-001)

First Payroll feature — establishes `features/payroll/`. Tenant-admin / HR-officer
facing config screens. Lazy route `payroll` (roleGuard `Tenant Admin`, `HR Officer`)
with children `structures` (default) and `components`. The sidebar "Payroll" nav item
(`permission: 'Payroll.View'`) already existed in main-layout and points to `/payroll`.

### Frontend contract (`PayrollService`, camelCase, base `/api/v1/payroll`)
PROPOSED — backend was building in parallel; reconcile route strings (all live in
ONE file, `payroll.service.ts`, so a mismatch is a one-line fix). All withCredentials
+ tenant via tenantInterceptor + backend RLS (AC-6). Global ApiResponse unwrap
(US-PLT-001) → services consume BARE payloads.
- `GET    /payroll/salary-components` → `ISalaryComponent[]` (tolerates `{ data }`).
- `POST   /payroll/salary-components` body `ISalaryComponentRequest` → component (AC-1).
- `PUT    /payroll/salary-components/:id` → component (AC-2).
- `DELETE /payroll/salary-components/:id` → 204, OR **409** when in use by active
  employees (AC-5) — body `{ code:'component_in_use', affectedEmployeeCount }`.
  `PayrollService.parseInUseError` matches HTTP 409 OR that body code and pulls the
  count (defaults 0); the table shows it in a blocking alertdialog, NOT a toast.
- `POST   /payroll/salary-components/reorder` body `{ orderedIds: string[] }` →
  re-sequenced `ISalaryComponent[]` (AC-4). FE is optimistic + rolls back on error.
- `POST   /payroll/salary-components/validate-formula` body `{ expression,
  sampleValues:{basic,gross} }` → `{ valid, result?, error? }` — backs the formula
  "Test" button (FR-4). Same safe evaluator payroll uses, so test == runtime.
- `GET    /payroll/salary-structures` → `ISalaryStructure[]` (tolerates `{ data }`).
- `POST   /payroll/salary-structures/:id/clone` → new structure (FR-6).

### Enum casing (US-PLT-003 — critical)
PascalCase string unions = C# member names (global JsonStringEnumConverter), enums
arrive as STRINGS. Source of truth = payroll.models.ts:
- `SalaryComponentType` = `Earning | Deduction | Statutory | Reimbursement`
  (`COMPONENT_TYPE_BADGE`: Earning=emerald, Deduction=rose, Statutory=indigo,
  Reimbursement=amber).
- `CalculationMethod` = `Fixed | PercentageOfBasic | PercentageOfGross | Formula`
  (`CALCULATION_METHOD_LABELS` map the wire value to "% of Basic" etc.).

### UI decisions
- Structures list (AC-3) = Notion card grid with Active/Inactive badge + Default
  chip + Clone action; it is the default `/payroll` screen.
- Components (§8) = inline Notion-style table (NOT cards). Processing order
  reorderable by CDK drag-drop AND up/down buttons (mobile/keyboard a11y
  alternative). Create/edit = right slide-over (`ComponentFormComponent`), same
  drawer pattern as recruitment vacancy-form (fixed wrap `justify-end
  pointer-events-none` + panel `pointer-events-auto`, `@drawer` translateX,
  separate `@backdrop`). NEVER a full-page nav.
- Component form swaps validators on `calculationMethod`: Formula → `formulaExpression`
  required + the Test panel shows, `defaultValue` cleared; otherwise `defaultValue`
  required. On save, the irrelevant field is nulled in the request.
- Tenant primary color via `[style.background-color]="'var(--brand-primary)'"` on
  action buttons / header CTAs (same as careers-branding).

## Assign salary to employee (US-PAY-002)

Adds a 12th **"Compensation"** tab to the Core HR employee profile (sectionList
index 11, `key: 'compensation'`). Implemented as a self-contained child
`EmployeeCompensationComponent` (payroll feature) embedded with
`[employeeId]="employeeId"` — same child pattern as employee-documents /
employee-leave-overrides, so the employee-profile diff is tiny (import + imports
array + sectionList entry + one `@if (activeTab() === 11)` block). NOTE: any new
tab bumps the employee-profile spec's hardcoded `sectionList.length` assertion —
update that count, it is not a weakening.

### Frontend contract — NEW service `EmployeeSalaryService` (payroll/services)
Sibling to PayrollService; route strings live ONLY here. Base `/api/v1/payroll`.
Bare payloads (US-PLT-001 envelope), PascalCase enums (US-PLT-003).
- `POST /payroll/salary-assignments/preview` body `ISalaryAssignmentRequest` →
  `ICtcBreakdown` (FR-3 preview-before-confirm; same body shape as assign so the
  previewed numbers == what is saved). `balanced` flag = FR-6 tolerance check.
- `POST /payroll/salary-assignments` → `ISalaryAssignmentResult` (FR-1).
- `POST /payroll/salary-assignments/bulk` body `IBulkAssignmentRequest` →
  `IBulkAssignmentResult` (per-row `results[]` drive the progress bar + per-row
  status chips, AC-4).
- `GET  /payroll/employees/:id/compensation` → `IEmployeeCompensation`
  (null structure/ctc ⇒ render "Payroll Incomplete" badge, BR-5).
- `GET  /payroll/employees/:id/revision-history` → `ISalaryRevision[]`
  (tolerates `{ data }`; newest-first timeline, FR-4/BR-3).

### UI decisions (US-PAY-002, §8)
- Compensation tab: stat cards (structure/annual/monthly CTC) + Notion inline
  breakdown table (Component | Monthly | Annual, total row). Override lines get
  `bg-brand-50` + an "Override" badge (AC-3 visually distinct).
- Assign/revise = right slide-over drawer (same `@drawer`/`@backdrop` pattern as
  component-form). The form's `valueChanges` clears the preview so a stale
  preview can never be the one confirmed; **Confirm is disabled until a preview
  exists** (enforces FR-3 preview-before-confirm). Overrides are a FormArray in a
  dashed brand-bordered panel.
- Revision history = vertical timeline; each row expands to a 2-col before/after
  comparison (FR-4).
- Bulk assign = dedicated child route `payroll/bulk-assign` (NOT a tab).
  Spreadsheet-like grid: pick one structure + date, then paste two columns
  (employeeId, CTC) into a textarea — `parsePaste` splits on tab OR comma and
  strips currency chars. **Desktop only**: `md:hidden` message replaces the grid
  on mobile (§8). Progress bar + per-row success/failed chips after submit.
- Shared utility classes (`badge`, `skeleton-line`, `btn-spinner`) are
  component-scoped in employee-profile, NOT global — re-declared in each new
  payroll component's `styles` block (ViewEncapsulation).

### Backend implementation (US-PAY-002) — reconciled with the FE contract above
Entities `EmployeeSalaryComponent` + `SalaryRevisionHistory` (both BaseEntity,
tenant-scoped; migration `Payroll_EmployeeSalaryAssignment`). Service
`ISalaryAssignmentService`/`SalaryAssignmentService`. Controller
`EmployeeSalaryController`, base `api/v1/payroll`, all
`[RequirePermission("Payroll.Configure")]` (no separate Assign permission exists).
Routes implemented to MATCH the FE contract: `salary-assignments/preview`,
`salary-assignments`, `salary-assignments/bulk`, `employees/{id}/compensation`,
`employees/{id}/revision-history`. Preview AND assign both return `CtcBreakdownDto`
(same shape ⇒ previewed numbers == saved). `CtcBreakdownDto.Balanced` (bool) is the
FE's FR-6 indicator (`|TotalAnnualEarnings − AnnualCtc| <= 1`).
- FE expected separate `ICtcBreakdown` vs `ISalaryAssignmentResult` and a `balanced`
  flag — backend returns ONE `CtcBreakdownDto` for both with a `Balanced` field; FE
  can alias both interfaces to it. Bulk → `BulkAssignResultDto` { totalRequested,
  succeededCount, failedCount, results[] { employeeId, success, error, errorCode } }.

### CTC breakdown calc (`CtcBreakdownCalculator`, pure domain, `HRM.Domain/Payroll`)
Resolves a structure's effective component rules → annual+monthly for a declared
annual CTC, in ProcessingOrder (earlier components visible to later formulas).
`monthly = annual/12`; all money `Math.Round(.,2,AwayFromZero)` (numeric(18,2)).
Method semantics decided HERE:
- `Fixed` → effective value is a fixed ANNUAL amount.
- `PercentageOfGross` → value% of the declared annual CTC (gross == CTC at assignment).
- `PercentageOfBasic` → value% of the resolved component coded `BASIC`.
- `Formula` → reuses **`SalaryFormula.Evaluate`, NOT NCalc** (the story brief says
  NCalc, but US-PAY-001 rejected NCalc over a NuGet conflict; the existing
  recursive-descent evaluator is the source of truth). Variables = each earlier
  component's resolved ANNUAL amount by code + aliases `gross`/`ctc` (== CTC) and
  `basic` (resolved BASIC). Useful pattern: a residual "Special Allowance"
  `ctc - BASIC - HRA` makes earnings always total the CTC so FR-6 passes at any CTC.
- Per-component override (component id → fixed annual amount) short-circuits the rule,
  is flagged `IsOverride` (AC-3), and IS visible to later formulas.

### Assignment domain rules (decided in US-PAY-002)
- FR-6: |sum(EARNING annual amounts) − CTC| ≤ ±1 (`CtcTolerance=1m`) else
  `ctc_sum_mismatch` 400. Only EARNING components count toward CTC (statutory/
  deduction/reimbursement excluded).
- FR-7: inactive structure → `structure_inactive` 400 (service-side, needs DB). Missing
  structure → `structure_not_found` 404; missing employee → `employee_not_found` 404.
- BR-1/BR-2 supersession: an assign closes the employee's currently-active rows (window
  contains today) at `effectiveFrom − 1`, ONLY shortening (never extending) the window.
  Current/past date supersedes immediately; a FUTURE date closes the old window in the
  future so today's "current" comp is unchanged until the date arrives. New rows start
  with `effective_to = null`. "Current" read = `effective_from <= today &&
  (effective_to == null || effective_to >= today)`.
- BR-3 revision history: every assign appends a `salary_revision_history` row — old/new
  structure + old/new CTC (old = prior CURRENT rows' summed earnings; null on first-ever
  assign), effective date, reason, `changed_by = currentUser.UserId`, `changed_at =
  UtcNow`. Append-only.
- Bulk (AC-4/FR-5): structure validated once up front (whole batch fails fast on a
  bad/inactive structure); per-employee unknown ⇒ SKIPPED with per-row
  `employee_not_found`, valid ones succeed; all staged + one `SaveChanges` (NFR-1).

### Deferred (NOT in US-PAY-002) — confirmed per the story brief
- NFR-5 column-level encryption at rest (pgcrypto) — not built.
- BR-6 backdating-into-finalized-payroll guard — depends on payroll-run US-PAY-003
  (doesn't exist yet).
- DB-level RLS — US-PLT-002 (isolation here is EF global filters + TenantInterceptor).
- NFR-4 before/after audit log — reuses the existing `AuditInterceptor` (stamps audit
  fields on SaveChanges); `salary_revision_history` IS the domain before/after record.
  No new audit infra added.
- BR-4 probation-vs-confirmed distinction, BR-5 "Payroll Incomplete" flag (FE-only
  badge), BR-7 employer-statutory-in/out-of-CTC, CSV upload parsing — not in scope.

## Run monthly payroll (US-PAY-003)

Adds payroll RUNS under the existing `payroll` lazy route: child routes `runs`
(Notion-style table) + `runs/:id` (detail). New sibling service `PayrollRunService`
(payroll/services) — route strings live ONLY here, base `${apiBaseUrl}/payroll/runs`.
Bare payloads (US-PLT-001), `PayrollRunStatus` PascalCase string enum (US-PLT-003).

### Frontend contract — `PayrollRunService` (assumed REST; reconcile in this one file)
- `GET  /payroll/runs` → `IPayrollRun[]` (tolerates `{ data }`).
- `GET  /payroll/runs/:id` → `IPayrollRun`.
- `POST /payroll/runs/validate` body `{ payMonth, payYear }` → `IPayrollRunValidation`
  ({ totalEmployees, readyEmployees, missingSalaryStructure, canRun, blockers[] }) —
  backs the modal pre-run summary; `canRun:false` (e.g. already finalized AC-4) disables Submit.
- `POST /payroll/runs` body `{ payMonth, payYear }` + **`Idempotency-Key` header** (FR-9)
  → `IPayrollRun` (status Queued; BE returns 202). 409 ⇒ period already finalized (AC-4).
- `GET  /payroll/runs/:id/progress` → `IPayrollRunProgress`
  ({ runId, status, processedEmployees, totalEmployees, skippedEmployees }).

### Progress: POLLING, with a one-method SignalR swap point (FR-6)
`PayrollRunService.streamProgress(id)` = `timer(0, 2000ms)` → `switchMap(getProgress)`
→ `takeWhile(Queued|Processing, inclusive:true)`. It emits the first terminal snapshot
then completes; the detail cmp then refetches the run for the summary card. This is THE
isolated method to replace if the BE later exposes a SignalR hub — emit the same
`IPayrollRunProgress` shape, nothing else changes. fakeAsync test: `timer(0,…)` needs a
`tick(0)` before the FIRST `expectOne` (it does NOT fire synchronously on subscribe).

### UI decisions (US-PAY-003, §8)
- Runs list = Notion table (desktop) / stacked cards (mobile); sortable header buttons
  (period/status/employees/net/date, toggle dir) + status filter pills. Row click →
  `/payroll/runs/:id`.
- New run = right slide-over modal (same `@drawer`/`@backdrop` pattern), month+year
  selects, pre-run summary, Submit gated on `canRun`. Default period = PREVIOUS month
  (payroll runs in arrears). On create → route to detail.
- Detail = horizontal stepper Queued>Processing>Review>Approved>Finalized (Cancelled is
  an off-path banner, not a node); live progress bar while active; completion summary
  card (gross/deductions/net + paid/skipped/total) once ReviewPending+. "View details"
  payslip link is a DISABLED stub — full payslip viewing is US-PAY-004/005.

### Deferred (NOT in US-PAY-001)
Structure detail w/ mock-payslip breakdown, linking components↔structure with
overrides (FR-3 junction), version history (FR-7), the active-structure earning-
component guard (FR-5). Later Payroll stories add sibling child routes under
`payroll.routes.ts`.

## Generate individual payslips — PDF (US-PAY-004)

Adds 4 nullable columns to `payroll_slip` (migration `Payroll_PayslipPdfFields`):
`pdf_generated_at` (timestamptz), `pdf_storage_path` (varchar 500),
`pdf_status` (varchar 20: Pending/Generated/Failed — constants in
`HRM.Domain/Payroll/PayslipPdfStatus.cs`, NOT an enum so the wire string == the
data-spec literal), `pdf_file_size_bytes` (int). A slip exists before its PDF, so
all are nullable; null pdf_status == "never generated" == Pending to the FE.

### Architecture (3 seams, mirrors US-PAY-003's run-job split)
- `PayslipPdfRenderer` (static, `HRM.Infrastructure/Services`) — PURE function
  `PayslipDocumentModel → byte[]` via **QuestPDF** (already referenced for
  US-ATT-007; `QuestPDF.Settings.License = LicenseType.Community` set idempotently
  in `Render`). No DB / tenant / FS ⇒ trivially unit-testable (assert `%PDF`
  header). A4, earnings+deductions side-by-side tables, statutory rows labelled
  "(Statutory)", net-pay banner, days line, footer disclaimer, optional YTD column.
- `PayslipDocumentModel` (`HRM.Domain/Payroll`) — denormalized render input built
  from slip + details (BR-2 point-in-time component names) + employee + tenant.
- `IPayslipBatchRenderer` / `PayslipBatchRenderer` — the COMPUTE side: bulk-loads
  slips/employees/departments/jobtitles/details ONCE (no N+1), renders with bounded
  concurrency (`SemaphoreSlim`, MaxConcurrency=10, NFR-3), stores each via
  **`IFileStorage.UploadAsync`** (REUSED — the existing US-CHR-001 abstraction;
  `LocalFileStorage` prefixes `{tenantId}/`, so the path stored is the WITHIN-tenant
  `payroll/{runId}/{employeeId}.pdf`), then one `SaveChanges`. FR-8: a single render
  failure flips THAT slip to Failed + logs, batch continues, retryable on regenerate.
- `IPayslipGenerationService` / `PayslipGenerationService` — ENQUEUE + STATUS +
  LIST + DOWNLOAD. `GenerateAsync` resets all slips to Pending (AC-5 regenerate
  detected via any slip already Generated), enqueues the optional
  `IPayslipGenerationJobScheduler` (Hangfire, in HRM.Api). BR-1 guard: only
  ReviewPending/Approved/Finalized ⇒ else 400 `run_not_ready_for_payslips`.
- `GeneratePayslipsJob` (HRM.Api/Jobs) restores tenant context from job args into a
  fresh DI scope (FR-3 pattern, mirrors `ProcessPayrollRunJob`/AttendanceSummaryExportJob)
  then calls `RenderRunAsync`.

### Path safety + file naming
`PayslipStoragePath` (`HRM.Application/Common/Payroll`): storage path is
GUID-derived ONLY (`payroll/{runId}/{employeeId}.pdf`) — never user input, so
traversal is structurally impossible; `AssertSafe` is belt-and-braces (rejects
`..`, rooted/absolute). Download/ZIP-entry file name is BR-5
`{EmployeeNo}_{PayMonth}_{PayYear}.pdf`, with EmployeeNo sanitized to a safe charset.

### Endpoints (`PayslipsController`, base `api/v1/payroll`, all `[RequirePermission("Payroll.Run")]`)
- `POST runs/{runId}/payslips/generate`   → 202 (AC-1)
- `POST runs/{runId}/payslips/regenerate` → 202 (AC-5; delegates to the same
  `GenerateAsync` — a re-run that resets to Pending)
- `GET  runs/{runId}/payslips`            → `PayslipListItemDto[]` (§8 table:
  slipId, employee no/name, dept, net, pdf_status) — added to satisfy the FE
  `listPayslips`
- `GET  runs/{runId}/payslips/status`     → counts (FR-7 progress bar)
- `GET  runs/{runId}/payslips/{employeeId}/download` → single PDF stream (FR-6)
- `GET  runs/{runId}/payslips/download-all` AND `…/download-zip` (alias) → ZIP
  (FR-6/AC-3, `System.IO.Compression.ZipArchive` in-memory).
- Tenant isolation (AC-4): every read goes through the EF global query filter, so a
  cross-tenant runId/employeeId is simply invisible ⇒ 404. No caller-supplied path
  is ever accepted.

### FE↔BE reconciliation note (one-file FE fix, NOT done here — frontend is out of my lane)
`payslip.service.ts` currently calls `payslips/{slipId}/download` (by SLIP id) and
`…/download-zip`. Backend exposes single download by `{runId}/{employeeId}` and BOTH
`download-all`+`download-zip`. The ZIP route is aligned; the single-download route
differs (slipId vs runId+employeeId). The FE table now has `slipId` in the list DTO,
so the FE can switch to the run/employee route or a future by-slip route — reconcile
in that one FE service file.

### Deferred (noted per brief; NOT built)
- **YTD column (BR-4):** the renderer + `BuildYtdAsync` (sum prior months' same-year
  details by component, earning/deduction buckets) are fully implemented, but
  `TenantYtdEnabled()` returns `false` — no per-tenant YTD-toggle config surface
  exists yet. Flipping it on is a one-line change once a tenant payroll-settings
  entity lands.
- **Tenant branding:** logo URL / address / brand colour / footer disclaimer are
  model fields the renderer honours, but there's no tenant payslip-template config
  surface, so company name = subdomain, address/logo null, default disclaimer. Wire
  to a tenant-template entity when it exists.
- Cloud blob storage (local FS only, Phase 1 — `LocalFileStorage`), drag-drop
  template designer, 5,000-PDF live perf test (NFR-1 — manual harness, NOT a [Skip]).

## Employee self-service payslip view (US-PAY-005)

The EMPLOYEE-facing read view of their OWN payslips (US-PAY-004 built the HR-facing
generation/PDF side). Read-only; no new entity, no migration — reuses `payroll_slip`,
`payroll_slip_detail`, and the PDFs already at `{tenantId}/payroll/{runId}/{employeeId}.pdf`.

### Permission decision (the crux — reconcile vs the story)
The story names `Payroll.Read.Self`, but that string is **not** in `PermissionCatalog`.
The catalog already has `Payroll.ViewOwn = "Payroll.View.Own"`, granted to the built-in
**Employee** role — and the platform's established self convention is `Module.View.Own`
(cf. `Employee.View.Own`, `Leave.View.Own`, `Attendance.View.Own`). So US-PAY-005 reuses
the **registered** `Payroll.View.Own` rather than inventing an unregistered permission.
All three endpoints are `[RequirePermission("Payroll.View.Own")]`. (If the FE/QA were
written against the literal `Payroll.Read.Self`, that is the reconcile point — but the
backend gate is `Payroll.View.Own`.)

### Self-resolution + two-layer isolation
Caller's employee is resolved by `Employee.UserId == ICurrentUser.UserId` (the same
self-pattern AttendanceService uses). `Employee.UserId` is nullable — no link ⇒ **403**
`no_employee_linked` (BR-5, never leaks anyone else's data). Isolation is two layers:
(1) tenant — EF global query filter on every read; (2) employee — explicit `EmployeeId`
filter on the list, and an explicit owner check on detail/PDF.

### AC-4 — cross-employee = 403, NOT 404 (deliberate)
Detail + PDF are addressed by a user-supplied `payslipId`. The service loads the slip
(tenant-scoped) then: not found for tenant ⇒ 404 (`payslip_not_found`; this is also the
cross-tenant case — the global filter hides it); **belongs to another employee ⇒ 403
`forbidden_payslip`** (the story explicitly wants Forbidden on URL manipulation, so the
denial is an authz signal, not a "doesn't exist"); run not Finalized ⇒ 403
`payslip_not_finalized` (BR-1 — exists for the caller but not yet visible). No other
employee's data is returned on any path.

### BR-1 — Finalized runs only
List joins slips to runs and keeps only `PayrollRunStatus.Finalized` (ReviewPending/
Approved/Queued hidden). Detail/PDF re-check the owning run is Finalized.

### Routes (`MyPayslipsController`, base `api/v1/payroll/my-payslips`) — FE contract
- `GET /payroll/my-payslips?year=&page=&pageSize=` → `MyPayslipListDto`
  `{ items[], totalCount, page, pageSize }`; item =
  `{ payslipId, payMonth, payYear, grossEarnings, totalDeductions, netSalary, paidDays, lopDays, pdfAvailable }`.
  Most-recent-first (PayYear desc, PayMonth desc); default `pageSize=12` (clamped 1..100,
  bad values fall back to 12), `page` default 1.
- `GET /payroll/my-payslips/{payslipId}` → `MyPayslipDetailDto`
  `{ payslipId, payMonth, payYear, employee{ name, employeeNo, department, designation },
  earnings[{ componentName, amount, ytdAmount }], deductions[...], grossEarnings,
  totalDeductions, netSalary, workingDays, paidDays, lopDays }`. Earnings = non-deduction
  components; deductions = Deduction+Statutory (same `IsDeductionSide` split as the renderer).
- `GET /payroll/my-payslips/{payslipId}/pdf` → streams the pre-generated PDF
  (`{EmployeeNo}_{PayMonth}_{PayYear}.pdf`, `application/pdf`); 404 `pdf_not_generated`
  when not yet rendered.

### Deferred (noted per brief; NOT built)
- **BR-3 post-termination access policy** (immediate revoke / 30-day / permanent
  read-only): no tenant policy surface exists, so access defaults to read-only for any
  linked employee regardless of `EmployeeStatus`. Wire to a tenant payroll-settings entity
  when it lands (same surface as the YTD toggle).
- **FR-7 tenant YTD toggle:** `MyPayslipService.TenantYtdEnabled()` returns false (shared
  deferral with US-PAY-004 `PayslipBatchRenderer`). The per-component YTD sum (`BuildYtdAsync`,
  scoped to the single self employee) is fully built; flip the flag on once the settings
  entity exists. Until then every `ytdAmount` is null.

### Frontend (US-PAY-005) — reconciled with the BE contract above
NOT under the `/payroll` lazy route (that parent is `roleGuard(['Tenant Admin','HR Officer'])`
+ `Payroll.View`, blocks a plain Employee). NEW top-level route `my-payslips`
(`payroll/my-payslips.routes.ts`, wired in app.routes.ts) guarded
`roleGuard(['Employee','Manager','HR Officer','Tenant Admin'])`. Sidebar nav "My Payslips"
(receipt icon) gated on the **registered** `Payroll.View.Own` (the BE permission, NOT the
story's literal `Payroll.Read.Self` which isn't in the catalog — reconciled to match the gate).
- NEW `MyPayslipService` (payroll/services), base `${apiBaseUrl}/payroll/my-payslips`, route
  strings ONLY here. List paginated (`IMyPayslipPage`, default page=1/pageSize=12, `year`
  omitted when null; `toPage` also tolerates a bare array). Detail `IMyPayslipDetail`. PDF
  blob via HttpResponse<Blob>+Content-Disposition. Bare payloads (US-PLT-001) + withCredentials.
- `MyPayslipsComponent`: Notion table (Pay Period | Gross | Deductions | Net), FE re-sorts
  most-recent-first defensively, amounts `font-mono tabular-nums` right-aligned. Year tabs
  DERIVED from returned payslips' distinct years (desc) on first load. Detail = inline
  EXPANDABLE card lazy-loaded on open (guards stale response if `expandedId` changed);
  earnings `bg-emerald-50/60`, deductions `bg-rose-50/60`; YTD column only when
  `hasYtd(detail)` (any line has ytdAmount — currently never, BE FR-7 deferred). "Download PDF"
  disabled when `!pdfAvailable`; reuses the `downloadBlob`/`filenameFromDisposition` anchor-click
  helpers (local copy, same as payslip-list US-PAY-004). Table → stacked cards at md via
  `.row-label-mobile` + breakdown `overflow-x-auto` for 360px (AC-5).
