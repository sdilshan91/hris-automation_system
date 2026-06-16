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

## Statutory deductions configuration (US-PAY-006)

Desktop-focused tenant-admin / HR config page. Adds child route `payroll/statutory`
under the existing `payroll` lazy route (`StatutoryConfigurationComponent`), linked
from the salary-structures header ("Statutory config" button) — NO separate sidebar
nav (same as components/runs, all reached from within `/payroll`).

### Frontend contract — NEW service `StatutoryService` (payroll/services)
Sibling to PayrollService; route strings live ONLY here. Base
`${apiBaseUrl}/payroll/statutory-rules`. Bare payloads (US-PLT-001), PascalCase
enums (US-PLT-003: `StatutoryRuleType` = IncomeTax|EPF|ETF|ProfessionalTax|Custom,
`ApplicableOn` = Basic|Gross|Custom). withCredentials. **ASSUMED contract — BE was
building in parallel and had NOT pinned routes (grep found only `isStatutory`
flags, no statutory-rule entity); reconcile in this one file if BE differs:**
- `GET  /payroll/statutory-rules?fiscalYear=` → `IStatutoryRule[]` (tolerates `{data}`).
- `GET  /payroll/statutory-rules/fiscal-years` → `string[]` (FR-4 FY selector, newest-first).
- `POST/PUT/DELETE /payroll/statutory-rules[/:id]` → CRUD.
- `POST /payroll/statutory-rules/test-calculation` body `{fiscalYear, monthlyGross,
  monthlyBasic?}` → `ITestCalculationResult` (FR-5 — incomeTax/employeeEpf/
  employerEpf/etf/otherDeductions/totalDeductions/netPay).

### Slab validation is a PURE helper (FR-6) — `services/slab-validation.ts`
`validateSlabs(ITaxSlab[])` → `{ issues: Map<origIndex, 'overlap'|'gap'|'invalid'>,
valid }`. Isolated from the component (mirrors BE NFR-5 side-effect-free calc) so
it's trivially unit-testable. Sorts by slabFrom but reports against ORIGINAL index;
gap/overlap flags BOTH adjacent rows; only the last slab may be unlimited
(slabTo=null); 'invalid' wins over contiguity. The component's `slabsValid` computed
gates the Save button; offending rows get `bg-rose-50`/`ring-rose-300` in real time.

### UI decisions (US-PAY-006, §8)
- One page, three TABS (Income Tax / Provident Fund / Other Deductions) as a
  signal `activeTab`, Notion card per tab. NOT a slide-over (this is a config
  surface, not a CRUD list).
- Tax editor = inline add/remove-row table, `ngModel`-bound number inputs; new row
  continues from previous slabTo.
- EPF/social-security = plain form (employee/employer rate, annual ceiling nullable,
  applicableOn select). Other deductions = name + ProfessionalTax|Custom + rate;
  both serialize to `socialSecurity` on the rule request.
- Fiscal-year selector = tab strip of `fiscalYears()` + "Add year"; default FY =
  current (April-start "YYYY-YYYY"); `addFiscalYear` resets editors to a clean slate
  (persists on first save). `effectiveFrom` = `${startYear}-04-01`.
- Test-calc panel = sticky right column (`lg:col-span-1`); version history =
  collapsible newest-first timeline below the form (FR-4, sorted by updatedAt).
- Mobile (<lg) = read-only summary of current slabs + PF with an amber "configure
  on desktop" note; the full editor grid is `hidden lg:grid`.
- `persist()` helper: finds an existing rule of the same type+FY → update, else
  create. `countryCode` hardcoded 'LK' until tenant-jurisdiction config lands (BR-1).
- Slab-validation + service + component specs (42 tests) — full FE suite 2125 green.

## Payroll adjustments (US-PAY-007)

HR/Admin CRUD surface for ad-hoc bonuses / deductions / reimbursements /
corrections. Adds child route `payroll/adjustments` (`PayrollAdjustmentsComponent`)
under the existing `payroll` lazy route (already `roleGuard(['Tenant Admin','HR
Officer'])` + `Payroll.View`) — reached from within `/payroll`, no separate sidebar
nav (same as components/runs/statutory).

### Frontend contract — NEW service `AdjustmentService` (payroll/services)
Sibling to PayrollRunService/PayslipService; route strings live ONLY here. Base
`${apiBaseUrl}/payroll/adjustments`. Bare payloads (US-PLT-001), PascalCase enums
(US-PLT-003: `AdjustmentType` = Bonus|Deduction|Reimbursement|Correction,
`AdjustmentStatus` = Pending|Applied|Cancelled). withCredentials. **ASSUMED contract
— BE was building in parallel and had NOT pinned adjustment routes in this vault
(no adjustment .cs files existed when the FE was written); reconcile in this one
file if BE differs:**
- `GET  /payroll/adjustments?status=&type=&period=&employeeId=` → `IAdjustment[]`
  (tolerates `{data}`). `period` is the wire `YYYY-MM` string (one param carries
  month+year).
- `POST /payroll/adjustments` body `IAdjustmentRequest` → `IAdjustment` (FR-1).
- `POST /payroll/adjustments/:id/cancel` → `IAdjustment` (FR-6; BE rejects non-Pending).
- `POST /payroll/adjustments/bulk` (multipart `file` + `commit` flag) — `commit=false`
  is the dry-run preview (`IBulkAdjustmentPreview`, FR-2), `commit=true` commits
  (`IBulkAdjustmentResult`). SAME endpoint, the flag toggles preview vs commit.
- `POST /payroll/adjustments/:id/document` (multipart) → `IAdjustment` (AC-3; uploaded
  AFTER create, separate call — JSON create carries everything else).
- `GET  /payroll/adjustments/:id/document` (blob) + `GET …/template` (blob CSV) —
  both `responseType:'blob'`+`observe:'response'`, bypass the envelope, anchor-click
  download (local `downloadBlob`/`filenameFromDisposition` copy, same as payslip-list).

### Period + recurrence helpers are PURE (adjustment.models.ts, BR-6)
`periodLabel(m,y)`→"Jun 2026", `toPeriodParam(m,y)`→"2026-06", and
`recurringPeriods(startM,startY,endM,endY)` enumerates every affected future period
inclusive (rolls year over; [] for a reversed/invalid range; capped at 60 as a
typo guard). Isolated from the component so they're trivially unit-tested; the form's
recurrence preview (BR-6) and the table period cell both use them.

### UI decisions (US-PAY-007, §8)
- Page = Notion table with Status/Type/Period/Employee filters + sortable column
  headers (employee/type/amount/period/status, toggle dir) + status/type badges.
  TWO tabs split **Active** (Pending+Cancelled) from **Applied** (§8 "dimmed or moved
  to an Applied tab"); Applied/Cancelled rows ALSO get `opacity-50`. Status-filter
  options are tab-aware (Applied tab → only Applied). Employee filter is free-text
  narrowed client-side (list API filters by employeeId; the box matches name/no).
- "New Adjustment" = right slide-over drawer (same `@drawer`/`@backdrop` pattern as
  component-form/adjustment-form). Employee TYPEAHEAD reuses
  `EmployeeService.searchActiveEmployees(term, n)` (Core HR, cross-feature import
  `../../../core-hr/employees/...`), debounced 250ms, min 2 chars. is_recurring toggle
  reveals a recurrence-end period selector + a live chip preview of every affected
  period (BR-6). Supporting-doc upload validated CLIENT-SIDE (NFR-5: PDF/JPG/PNG,
  ≤5MB via `Object.defineProperty(file,'size',…)` in tests). Doc uploads AFTER the
  create succeeds (best-effort: a failed doc upload warns but still emits the created
  record).
- Bulk upload = collapsible card hosting `AdjustmentBulkUploadComponent`:
  drag-and-drop CSV drop zone + template download link + validation-preview table
  (per-row valid/invalid, FR-2) BEFORE commit; Commit gated on validCount>0.
  **Desktop-only** (`md:hidden` message, same as bulk-salary-assignment). CSV gate =
  `.csv` extension OR a recognized csv mime (generic `text/plain` alone is NOT enough).
- Adjustment/bulk/page/model specs (67 tests) — full FE suite 2192 green.

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

## Payroll approval workflow (US-PAY-008)

Extends the EXISTING run-detail page (US-PAY-003) with an approver workflow + an
approver queue. The `PayrollRunStatus` machine gains **AwaitingApproval** (a stepper
node, after ReviewPending) and **Rejected** (OFF-PATH, like Cancelled — not a stepper
node; HR corrects + re-submits, BR-3/BR-4). Both added to the union + `RUN_STATUS_BADGE`
/`RUN_STATUS_LABELS`/`RUN_STEPPER` in `payroll-run.models.ts`. Stepper order now:
Queued > Processing > Review > Awaiting approval > Approved > Finalized.

### Frontend contract — NEW service `PayrollApprovalService` (payroll/services)
Sibling to `PayrollRunService`; route strings live ONLY here (base
`${apiBaseUrl}/payroll/runs`). Bare payloads (US-PLT-001), PascalCase enums
(US-PLT-003: `ApprovalAction` = Submitted|Approved|Rejected|Returned|Escalated).
withCredentials. **ASSUMED contract — BE was building in parallel and had NOT pinned
payroll-approval routes in this vault (grep found leave/overtime/regularization
approval entities but NO payroll-approval .cs when the FE was written); reconcile in
this one file if BE differs:**
- `POST /payroll/runs/:id/submit`   → `IPayrollRun` (AC-1, ReviewPending→AwaitingApproval; no body).
- `POST /payroll/runs/:id/approve`  → `IPayrollRun` (AC-2; BE owns sequential multi-step AC-4 + maker-checker BR-5).
- `POST /payroll/runs/:id/reject`  body `{ comments }` → `IPayrollRun` (AC-3, reason REQUIRED).
- `POST /payroll/runs/:id/return`  body `{ comments }` → `IPayrollRun` (FR-9, comments REQUIRED, →ReviewPending).
- `POST /payroll/runs/:id/finalize` → `IPayrollRun` (AC-5, locks payslips FR-8; no body).
- `GET  /payroll/runs/:id/approval-summary` → `IApprovalSummary` (FR-4: totals +
  totalStatutory + previousMonthTotalNet + variancePercentage + exceptions[]).
- `GET  /payroll/runs/:id/approval-history` → `IApprovalHistoryEntry[]` (FR-7, tolerates `{data}`).
- `GET  /payroll/runs?status=AwaitingApproval` → `IPayrollRun[]` (the queue; tolerates `{data}`).

### Variance band is a PURE helper (`approval.models.ts`, §8)
`varianceBand(pct|null)` → `decrease|normal|elevated|high`, isolated + unit-tested.
Thresholds: ≤0 (or flat) = green/decrease; >0..5 = neutral; >5..15 = amber/elevated;
>15 = red/high. null (no previous month) → 'normal'. `VARIANCE_BAND_CLASS` maps band →
text colour. The run-detail `varianceBandValue`/`varianceClass`/`varianceLabel`
computeds drive the §8 colour-coded comparison; `varianceLabel` is signed `+33.3%`.

### UI decisions (US-PAY-008, §8)
- Run-detail action bar = STICKY bottom bar (`fixed inset-x-0 bottom-0`), status-driven
  via `@switch(status())`: "Submit for Approval" (ReviewPending), Approve/Reject/Return
  (AwaitingApproval, gated by `auth.hasPermission('Payroll.Approve')` → `canApprove`),
  Finalize (Approved). Reject + Return reveal a REQUIRED comments `<textarea>`
  (`FormControl` + `Validators.required|minLength(3)`); a two-step confirm (start →
  fill → confirm) so the reason is captured before the call. `runAction()` helper flips
  `acting`, updates the run signal, reloads approval data, toasts, clears comments.
- Approval review = the existing US-PAY-003 summary card EXTENDED (not a new split
  layout component): when `approvalSummary()` loaded it adds a statutory/prev-month/
  variance row + an amber/red note for elevated/high bands + an exceptions list
  (Error=rose, Warning=amber). The existing embedded `app-payslip-list` is the FR-5
  drill-down (right side on desktop; child already hides bulk ZIP on mobile).
  `isComplete()` now includes AwaitingApproval so the card shows during review.
- Approval history = vertical timeline at the bottom (`history()` newest-first,
  coloured node per action, comments in quotes). `lastComment()` (first history entry
  with comments) shows on the Rejected off-path banner.
- "Pending Approvals" queue = NEW lazy child route `payroll/approvals` (declared BEFORE
  `runs/:id` so it matches literally, not as a run id), `PendingApprovalsComponent` —
  Notion card grid of AwaitingApproval runs with a header badge count; cards link to
  `runs/:id`. Sidebar nav item "Pending Approvals" gated on `Payroll.Approve` (added to
  main-layout `navItems`); badge count lives IN the queue page, NOT the sidebar (no
  sidebar badge-count infra exists — kept in scope by putting the count on the page).
- Specs: `approval.models.spec` (variance), `payroll-approval.service.spec` (all 9
  routes + envelope), `pending-approvals.component.spec`, and the run-detail spec
  EXTENDED (added PayrollApprovalService/AuthService/ToastrService spies to setup +
  setupProcessing; the component now fetches history+summary in `load()`). Full FE
  suite 2229 green.

## Payroll approval workflow (US-PAY-008)

Adds an approval/finalize gate on top of the US-PAY-003 run lifecycle. **No shared approval-workflow
engine exists** — the story references "technical doc section 34" but US-ADM-007 is not built, and
leave (US-LV-005) + attendance (US-ATT-004) each built their OWN approval-history entity + service.
So this is a **payroll-specific** flow that MIRRORS that pattern (a `PayrollApprovalHistory` BaseEntity
+ `PayrollApprovalService`). Integrating into a future shared engine is a follow-up — replace the
service internals, keep the controller/DTO contract.

### Status machine (additive — existing US-PAY-003..007 tests stay green)
`PayrollRunStatus` gained **`AwaitingApproval`** and **`Rejected`** (Approved/Finalized already existed),
still enum-as-string (`HasConversion<string>()`, global `JsonStringEnumConverter`). BR-4 transitions
(every other transition → 409 `invalid_transition`):
- `ReviewPending --Submit--> AwaitingApproval` (AC-1).
- `AwaitingApproval --Approve-->` advance step OR `Approved` when all steps done (AC-2/AC-4).
- `AwaitingApproval --Reject--> Rejected` (reason ≥10 chars required, AC-3).
- `AwaitingApproval --Return--> ReviewPending` (FR-9, comments ≥10 chars required; closes the instance).
- `Rejected/ReviewPending --Submit--> AwaitingApproval` (BR-3 re-submit = NEW workflow instance id).
- `Approved --Finalize--> Finalized` (AC-5, terminal BR-6). **BR-1: a direct `ReviewPending→Finalized`
  is BLOCKED** (`approval_required` 409) — a run MUST pass ≥1 approval. Re-finalize → `already_finalized`.

### Step-tracking state (added to `PayrollRun`, all nullable — additive migration)
`CurrentWorkflowInstanceId`, `CurrentApprovalStep`, `TotalApprovalSteps` (default 1, BR-2 single-step),
`SubmittedBy`, `SubmittedAt`, `RejectionReason`. The `payroll_approval_history` table matches the §7
data spec (payroll_run_id, workflow_instance_id, step_number, action {Submitted|Approved|Rejected|
Returned|Escalated} as varchar(20) literals — `PayrollApprovalAction` string constants NOT an enum so
wire==data-spec, actor_user_id, comments, acted_at, ip_address). Migration **`Payroll_ApprovalWorkflow`**
(`20260616031209`). FR-7 immutable: insert-only, no update/delete path.

### Maker-checker (BR-5) — eligible-approver count decision
The submitter (`SubmittedBy`) cannot Approve their own run when the tenant has **≥2 eligible approvers**;
`<2` relaxes it (small-team exception). "Eligible approver" = **distinct ACTIVE `UserTenant` whose roles
grant `Payroll.Approve`** (join UserTenant→UserTenantRole→RolePermission; these join tables are NOT
BaseEntity so they're filtered explicitly by `TenantId`, not the global filter). Self-approval with ≥2
approvers → 403 `self_approval`.

### Permissions (reconciled with the catalog — `Payroll.Approve` already existed)
- approve / reject / return → `[RequirePermission("Payroll.Approve")]`.
- submit / finalize / both reads → `[RequirePermission("Payroll.Run")]` (HR/run-owner actions; HR and
  approvers both hold Run). No new permission invented.

### Approval summary (FR-4) — reuses stored run totals
`GET …/approval-summary` returns the run's stored totals (employees/gross/deductions/statutory/net) +
`previousMonthTotalNet` (= the most recent **Finalized** run in an EARLIER period for the tenant) +
`variancePercentage` (`(net−prev)/prev*100`, 2dp; null when no baseline) + `exceptions[]` (skipped count,
negative-net slip count, zero-processed). Tenant-scoped by the global filter (BR-8).

### Notifications + IP
Reuses the log-only `IPayrollNotificationService` seam — added `NotifyApprovalEventAsync(tenant, runId,
eventType)`; real SignalR/email DEFERRED (US-NTF). IP (FR-7) captured from
`HttpContext.Connection.RemoteIpAddress` in the controller and passed through (null in job/test context).

### FE CONTRACT (pin — base `api/v1/payroll`, ApiResponse<T> envelope, PascalCase status strings)
New status wire values: **`AwaitingApproval`**, **`Rejected`** (plus existing `Approved`, `Finalized`).
- `POST runs/{runId}/submit-for-approval` body `{ totalApprovalSteps?, comments? }` → `PayrollApprovalResultDto`.
- `POST runs/{runId}/approve`  body `{ comments? }` → result. 403 `self_approval`, 409 `invalid_transition`.
- `POST runs/{runId}/reject`   body `{ comments }` (the reason, ≥10) → result. 400 `reason_required`.
- `POST runs/{runId}/return`   body `{ comments }` (≥10) → result. 400 `comments_required`.
- `POST runs/{runId}/finalize` (no body) → result. 409 `approval_required` / `already_finalized`.
- `GET  runs/{runId}/approval-history`  → `PayrollApprovalHistoryDto[]` (newest-first): `{ id,
  payrollRunId, workflowInstanceId, stepNumber, action, actorUserId, comments, actedAt, ipAddress }`.
- `GET  runs/{runId}/approval-summary`  → `PayrollApprovalSummaryDto`: `{ runId, payMonth, payYear,
  status, totalEmployees, totalGross, totalDeductions, totalStatutory, totalNet, previousMonthTotalNet,
  variancePercentage, exceptions[] }`.
- `PayrollApprovalResultDto` = `{ runId, status, action, workflowInstanceId, currentApprovalStep,
  totalApprovalSteps }`.

### Deferred (noted per brief; NOT built)
- SLA auto-escalation + backup approver (FR-3); approval delegation (FR-6); the Approved-not-Finalized-
  in-7-days reminder (BR-7) — no scheduler/tenant-config surface. The `Escalated` action constant exists
  but is unused.
- Real SignalR/email delivery (NFR-1) — log-only seam.
- FR-8 payslip immutability: there is no per-slip mutable flag — the **Finalized run status IS the lock**
  (US-PAY-005 reads + US-PAY-003 reprocess guard both already gate on Finalized). No new lock column.
- Finalize writes no history row (not in the §7 action enum; `FinalizedAt` is the audit signal) — kept
  the wire action set aligned to the data spec rather than inventing a "Finalized" action value.

## Payroll reports + analytics (US-PAY-009)

Pure READ/aggregation over the existing `payroll_slip` / `payroll_slip_detail` / `payroll_adjustment`
data from **FINALIZED runs only** (BR-1). **No new entity, no migration** — every figure computed
on-the-fly from persisted slips (the §7 pre-aggregated materialized table is a documented perf
follow-up). Tenant-scoped purely via the **EF global query filter** (AC-5/FR-8) — slips/details/runs/
employees are all BaseEntity, so a cross-tenant row is invisible; no extra WHERE.

### REUSED export infra (no duplicate added)
The leave module (US-LV-012) already built the export plumbing — REUSED, not re-derived:
`IReportExportStorage`/`LocalReportExportStorage` (the tenant-scoped temp-file seam, NO 24h TTL purge
exists → NFR-4 auto-delete noted as a follow-up), **ClosedXML** for `.xlsx`, **CsvHelper** for CSV.
PDF reuses the US-PAY-004 **QuestPDF** setup (`LicenseType.Community`, A4). All three render paths live
in ONE pure function `PayrollReportRenderer.Render(format, PayrollReportResult)` (Infrastructure/Services)
— no DB/tenant/FS, so unit-testable (`%PDF` header / header-row asserts). Reports are SYNCHRONOUS;
async-large via Hangfire (FR-4) is deferred.

### Permission (reconciled with the catalog — `Payroll.Export` already existed)
ALL report endpoints gate on **`Payroll.Export` = "Payroll.Export"** (already in `PermissionCatalog`,
granted to Tenant Admin + HR Officer — the report consumers). No `Reports.*` cross-module permission and
no new permission invented.

### Reports BUILT (7 of 8) + the YET (FR-1) report-type identifiers (the `{reportType}` path value)
`PayrollSummary` (FR-1a, dept breakdown + grand-total row: Department, Employee Count, Total Basic,
Total Allowances, Total Gross, Total Statutory, Total Other Deductions, Total Net), `DepartmentSummary`
(FR-1c — same builder as PayrollSummary), `EmployeeRegister` (FR-1b, one row/employee, one col per
distinct component name + Gross/Deductions/Net), `StatutoryDeduction` (FR-1d, one row per statutory
component), `BankAdvice` (FR-1e/AC-2), `Ctc` (FR-1h, current EARNING-side `employee_salary_component`
annual sum — only EARNING counts, mirrors US-PAY-002 CTC rule), `Variance` (FR-1g/BR-4).
**`YearEndTaxStatement` (FR-1f) is a documented STUB** — returns an empty result + deferral note; the
per-employee PDF + bulk-ZIP + 5,000-scale (AC-3/FR-7/NFR-2) is the deferred follow-up.

### Component split (slip details have a denormalized `ComponentType` string, NOT the enum)
Statutory = `ComponentType == "Statutory"`; Deduction = `"Deduction"`; both are deduction-side. Basic is
matched by component NAME ("Basic"/"Basic Salary") or a BASIC mention in `CalculationBasis` (there is no
BASIC marker on the detail row); everything else earning-side = "allowances".

### Bank advice (FR-1e/AC-2/BR-2) — the EMPLOYEE-BANK-FIELDS GAP
**The Employee entity has NO bank columns** (no bank_name / branch_code / account_number). So Bank Name /
Branch Code / Account Number are emitted EMPTY; Net Amount + Narration ("Salary {Month} {Year}") are
fully derived from the slip. Every result carries a `Note` documenting the gap +
`TODO(US-PAY-009 bank-fields)` to add encrypted bank columns. **BR-2 masking IS implemented + tested**:
`PayrollReportService.MaskAccount` (last-4 only) is applied on the preview path and bypassed on the
export path — the wiring is correct for when the columns land (preview masked, file full).

### Variance (FR-1g/BR-4)
Month-over-month NET per employee vs the previous calendar month (Jan rolls to Dec/prev-year). Flagged
when |change%| > 10% OR the employee newly appears/drops. Sorted flagged-first then biggest abs change.

### Analytics chart-type identifiers (FR-5, chart-lib-agnostic JSON)
`MonthlyTrend` (multi-series Gross/Deductions/Net over last 12 finalized months, shared `categories`
"YYYY-MM"), `DepartmentCostDistribution` (single-series net per dept for the period),
`StatutoryBreakdown` (single-series total per statutory component for the period).

### FE CONTRACT (pin — base `api/v1/payroll`, ApiResponse<T> envelope, `Payroll.Export` gate)
- `GET reports` → `PayrollReportDescriptorDto[]` ({ id, name, description, deferred }) — the sidebar list.
- `GET reports/{reportType}?payMonth=&payYear=&departmentId=&jobTitleId=&employmentType=&employeeSearch=`
  → `PayrollReportResult` ({ reportType, title, payMonth, payYear, columns[], rows[{cells[]}], totalRow?,
  totalCount, note? }). Period defaults to the MOST RECENT finalized run when month/year omitted; no
  finalized run → 404. BankAdvice here is MASKED.
- `GET analytics/{chartType}?...` → `PayrollAnalyticsResult` ({ chartType, points[{label,value}],
  categories[], series[{name, points[]}] }).
- `GET reports/bank-advice/preview?...` → `BankAdvicePreviewDto` ({ payMonth, payYear,
  lines[{employeeNo, employeeName, bankName, branchCode, accountNumber, netAmount, narration}],
  employeeCount, totalNetAmount, note }) — accountNumber MASKED (BR-2).
- `GET reports/{reportType}/export?format=csv|xlsx|pdf&...` → streamed file (FR-2/AC-4). BankAdvice export
  carries FULL account numbers. Export-format wire values: **`csv` / `xlsx` / `pdf`** (case-insensitive).

### Deferred (noted per brief; NOT built)
- Year-end tax statements (FR-1f/AC-3/FR-7): per-employee PDF + bulk ZIP + 5,000-scale — stub only.
- Async-large report generation via Hangfire (FR-4) — reports are synchronous.
- Pre-aggregated materialized dashboard table (§7) — compute on-the-fly.
- Read-replica / Redis caching (NFR-3); tenant-configurable bank formats (FR-6 — generic CSV/Excel/PDF);
  country-specific statutory formatting (BR-6); fiscal-year-start-month config (BR-5 — calendar/Jan assumed).
- NFR-4 export-file 24h auto-delete — `IReportExportStorage` has no TTL purge (shared leave-module gap).

## Payroll reports & analytics (US-PAY-009) — FRONTEND

Adds TWO child routes under the existing `payroll` lazy route (already
`roleGuard(['Tenant Admin','HR Officer'])` + `Payroll.View`): `reports`
(`PayrollReportsComponent`) and `analytics` (`PayrollAnalyticsComponent`). New
sidebar nav item **"Payroll Reports"** → `/payroll/reports`, gated on `Payroll.View`
(same capability as the Payroll parent). Reports page links across to analytics.

### NO charting library — pure SVG/CSS (REUSED the US-ATT-010 approach)
The attendance dashboard (US-ATT-010) draws charts with pure SVG/CSS via the
`buildDonutSegments` helper — there is NO chart.js / ngx-charts / swimlane dep in
`package.json`. US-PAY-009 follows the SAME approach (do NOT add a charting lib):
- Trend LINE chart = an SVG `<polyline>` per series (gross/deductions/net) from pure
  `polylinePoints`/`lineX`/`lineY`/`trendMax` helpers in `payroll-report.models.ts`
  (unit-tested), mirroring the attendance trends chart's `pointX`/`pointY`.
- Department cost distribution = horizontal CSS bars (width %), sorted desc (§8).
- Statutory breakdown = CSS flex-col-reverse stacked bars (segment height = frac of
  month total). Distinct component legend derived first-seen across months.

### Frontend contract — NEW service `PayrollReportService` (payroll/services)
Sibling to PayslipService; route strings live ONLY here. Base
`${apiBaseUrl}/payroll/reports`. Bare payloads (US-PLT-001), PascalCase report-type
ids (US-PLT-003). withCredentials. **ASSUMED REST contract — BE built in parallel and
had NOT pinned report routes in this vault (no reports controller existed when the FE
was written; only `LeaveReportsController` exists). Reconcile in this one file if BE
differs:**
- `GET  /payroll/reports/types` → `IReportTypeMeta[]` (tolerates `{data}`). The FE
  ALSO ships a static `REPORT_TYPES` list it uses for the sidebar (the `/types`
  method exists for parity but the page renders the static list).
- `GET  /payroll/reports/:reportType?period=YYYY-MM&department=` → `IReportResult`
  ({ summary[] stat cards, columns[], rows[], chart? bar series }). Generic
  column/row shape so the FE need not hardcode each report's columns.
- `GET  /payroll/reports/:reportType/export?format=csv|xlsx|pdf&period=&department=`
  → blob (`responseType:'blob'`+`observe:'response'`, bypasses envelope; anchor-click
  download via local `downloadBlob`/`filenameFromDisposition`, same as payslip-list).
- `GET  /payroll/reports/dashboard` → `IDashboardAnalytics` ({ trend[], departmentCosts[],
  statutory[] }), pre-aggregated (NFR-6).
- `GET  /payroll/reports/bank-advice?period=&department=` → `IBankAdvicePreview` with
  account numbers ALREADY MASKED by the BE (`accountNumberMasked`, last-4 only, BR-2).
- `GET  /payroll/reports/bank-advice/download?period=&format=csv|xlsx` → FULL file blob
  (un-masked, BR-2/AC-2).

### Report-type ids (`PayrollReportType`, payroll-report.models.ts)
PascalCase union = `PayrollSummary | EmployeeRegister | DepartmentSummary |
StatutoryDeduction | BankAdvice | Ctc | Variance` (== the `:reportType` path segment).
Export format = lowercase `csv | xlsx | pdf`. `defaultReportPeriod()` = PREVIOUS month
(payroll runs in arrears, same as the run-create default).

### UI decisions (US-PAY-009, §8)
- Reports page = Notion two-pane: LEFT sidebar of report types (renders the static
  `REPORT_TYPES` list, role="tablist"); RIGHT a filter panel (period `<input type=month>`
  + department `<select>` reusing **Core HR `DepartmentService.getDepartments`**,
  cross-feature import `../../../core-hr/departments/...`) + preview. Selecting a report
  CLEARS the stale preview (user re-generates). Generic reports → stat cards + optional
  bar chart + data table; Bank Advice swaps to the masked table + "Download Full File"
  button (no Export menu shown for bank advice). Export = toolbar dropdown (CSV/Excel/PDF).
- Mobile (§8): charts + export/download stay available; the detailed data table is
  `hidden md:block` with a "best viewed on a larger screen" note replacing it. Bank
  advice mobile shows a compact masked-account card list (so AC-2 masking still visible).
- Analytics page = card-based grid (trend full-width, dept + statutory side-by-side);
  charts collapse to one column on mobile; loads on init, error → empty card + toast.
- NOT a slide-over (these are read/preview surfaces, not CRUD lists).

### Specs (47 new) — full FE suite 2276 green
`payroll-report.models.spec` (helpers), `payroll-report.service.spec` (all routes +
envelope + blob/format params), `payroll-analytics.component.spec`,
`payroll-reports.component.spec`. Export/download specs stub `HTMLAnchorElement.prototype.click`.
Both component specs need `provideRouter([])` (RouterLink in templates) + a
PayrollReportService spy + a ToastrService spy; the reports spec also needs a
DepartmentService spy.

### Deferred (noted per the story; NOT built on the FE)
- Year-End Tax Statements (AC-3/FR-7, bulk-ZIP PDF) + async large-report generation
  (FR-4: "we'll notify you" progress) — these are async Hangfire/notification flows;
  the 7 synchronous report types + the bank advice + the dashboard are the FE scope.
  The sidebar omits a "Year-End Tax Statements" entry until the async/notify surface lands.
- Per-tenant bank-advice column/format config (FR-6) and fiscal-year-start config
  (BR-5) are backend concerns; the FE passes `period`/`department` and renders whatever
  the BE returns.

## Bulk payslip email distribution (US-PAY-011) — FRONTEND

EXTENDS the existing run-detail page (US-PAY-003/008). New self-contained child
`PayslipDistributionComponent` (payroll/components/payslip-distribution) embedded in
run-detail with `[runId]`/`[runStatus]`/`[employeeCount]` inside the `@if (isComplete())`
block (same pattern as the embedded `app-payslip-list`). Renders a "Send Payslips"
primary action + confirm dialog + live progress bar + per-employee delivery summary.

### Frontend contract — NEW service `PayslipEmailService` (payroll/services)
Sibling to PayslipService; route strings live ONLY here. Base
`${apiBaseUrl}/payroll/runs`. Bare payloads (US-PLT-001), `EmailDeliveryStatus`
PascalCase string enum (US-PLT-003: `Queued|Sent|Failed|Skipped`). withCredentials.
**ASSUMED contract — BE built in parallel and had NOT pinned routes (grep found no
EmailDeliveryStatus/send-payslips/PayslipEmail .cs when the FE was written);
reconcile in this one file if BE differs:**
- `POST /payroll/runs/:runId/send-payslips` body `{ confirm }` → `IPayslipDistributionStatus`
  (AC-1, 202). `confirm:true` is the FR-7/BR-5 duplicate-send ack (BE rejects a re-send
  without it once `hasSent`).
- `GET  /payroll/runs/:runId/payslip-emails/status` → `IPayslipDistributionStatus`
  ({ isSending, hasSent, total/sent/failed/skipped/queued, started/completedAt,
  recipients[] }). `recipients[]` = `IEmployeeDistribution` (employeeId/name/no,
  recipientEmail, status, failureReason, sentAt) → the expandable Sent/Failed/Skipped lists.
- `POST /payroll/runs/:runId/payslip-emails/resend` body `{ allFailed:true }` OR
  `{ employeeIds:[] }` → refreshed status (FR-4 re-send all-failed / per-employee).

### Progress: POLLING, with the one-method SignalR swap point (§8 real-time bar)
`PayslipEmailService.streamDistributionStatus(id)` = `timer(0,2000)` →
`switchMap(getDistributionStatus)` → `takeWhile(isSending, inclusive:true)` — SAME
shape as PayslipService.streamGenerationStatus / PayrollRunService.streamProgress.
Replace THIS method only for a SignalR distribution hub. fakeAsync test: `tick(0)`
before the FIRST `expectOne` (timer(0,…) doesn't fire on subscribe).

### Send-enablement (AC-1) — PDF readiness derived, not a new endpoint
`canSend = runStatus()==='Finalized' && hasGeneratedPdfs()`. PDF readiness is read
from the EXISTING `PayslipService.getGenerationStatus` (`generatedCount > 0`) on init —
no new "are PDFs ready" endpoint. Disabled-state hint explains which precondition fails.

### UI decisions (US-PAY-011, §8)
- "Send payslips" / "Re-send payslips" primary button (✉ icon, brand-primary). Confirm
  DIALOG (centered modal, NOT a slide-over) with "{count} employees… cannot be undone.
  Continue?"; when `hasSent`, an amber alert + a REQUIRED ack checkbox gates Confirm
  (`canConfirm` = !hasSent || resendAck) and the send passes `confirm:true` (FR-7/BR-5).
- Progress bar while `isSending`; then a summary card: 3 clickable count tiles
  (Sent=emerald / Failed=rose / Skipped=amber) that expand a per-employee list;
  "Re-send all failed" + per-row "Re-send" on Failed rows (FR-4). `isDistributed` =
  `hasSent && !isSending` gates the summary card.
- `resendAllFailed`/`resendOne` both set status from the response then re-enter the
  poll stream so the bar resumes. Mobile: same layout stacks (initiate + view status).

### Specs (service 6 + component 21) — full FE suite 2362 green
The run-detail spec's TWO TestBed configs (`setup` + nested `setupProcessing`) BOTH
needed `PayslipEmailService` + `PayslipService` spies added (the embedded child fires
`getDistributionStatus`+`getGenerationStatus` on init) — `makeChildSpies()` helper +
both providers in each config. Component spec: a send that re-enters an EMPTY `of()`
stream collapses `sending` back to false synchronously (stream completes) — use an
open `Subject` when asserting `sending()` stays true; `detectChanges()` after a signal
update before asserting dialog DOM.

### Deferred (noted per brief; NOT built on the FE)
- §8 push-notification mobile progress — the poll/SignalR status is the only progress
  surface; no web-push wiring.
- Opt-out preference (BR-3) — surfaced only as a Skipped status from the BE; no FE
  preference toggle.
- Rate-limit / template / sender-domain config (FR-3/FR-6/BR-2/BR-4) are backend
  concerns; the FE shows whatever delivery outcomes the status endpoint returns.

## Payroll history + audit trail (US-PAY-012) — FRONTEND (capstone)

Two NEW read surfaces + a run-detail extension, all under the existing `payroll`
lazy route (`roleGuard(['Tenant Admin','HR Officer'])` + `Payroll.View`). NEW
sibling service `AuditService` (payroll/services) — route strings live ONLY here.
Bare payloads (US-PLT-001), PascalCase `action`/`resourceType` wire strings
(US-PLT-003), withCredentials; tenant + RLS server-side (AC-5).

### Frontend contract — `AuditService` (ASSUMED REST — BE built in parallel, NO audit/history controller existed when FE was written; reconcile in this one file)
Base `${apiBaseUrl}/payroll`. No `audit_log` read controller or pinned action-name
values existed in the repo/vault at FE-write time (only the shared `AuditLog`
entity + EmployeeFieldAuditLog). Action names mirror the §7 "Payroll Action Types"
table verbatim.
- `GET /payroll/history?year=&status=` → `IPayrollHistoryRun[]` (tolerates `{data}`).
  History row adds `approvedByName` + `finalizedAt` to the run summary (AC-1 columns).
- `GET /payroll/audit-trail?dateFrom=&dateTo=&action=&resourceType=&actor=`
  → `IAuditEntry[]` (FR-4; every empty filter omitted).
- `GET /payroll/runs/:id/audit-trail` → `IAuditEntry[]` (FR-6 per-run timeline).
- `GET /payroll/audit-trail/export?format=csv|xlsx&...` → blob (`responseType:'blob'`
  +`observe:'response'`, bypasses envelope; anchor-click via local
  `downloadBlob`/`filenameFromDisposition`, same as payroll-reports/payslip-list).

### Diff is a PURE helper (`buildDiff` in audit.models.ts, FR-8)
`buildDiff(before, after)` → `IDiffRow[]` (field, before, after, kind). Union of
keys SORTED so the two columns line up; key only-in-after = `added` (green),
only-in-before = `removed` (red), both-differ = `modified` (amber), else
`unchanged`. null before = create (all-added), null after = delete (all-removed),
both null = []. Nested values → compact JSON, null → "—". Isolated from the
component so trivially unit-tested. `auditActionLabel` de-camelCases unknown future
actions; `auditDotClass` keys timeline-dot colour off the resource PREFIX so a new
verb still colours sensibly.

### UI decisions (US-PAY-012, §8)
- `PayrollHistoryComponent` (route `payroll/history`, declared BEFORE `runs/:id`) =
  Notion table (desktop) / stacked cards (mobile). Columns: Pay Period, Status badge,
  Employees, Total Net, Initiated By, Approved By, Finalized. Sortable headers
  (period/status/employees/net/finalized, toggle dir; default period-desc) + YEAR
  pills (derived from data, newest-first) + STATUS `<select>`. Filtering is
  CLIENT-SIDE over the loaded list (service also accepts year/status params). Row
  click → `runs/:id`. "View audit trail →" link to `payroll/audit`.
- `AuditTrailComponent` is REUSED in two modes via an `input()` `runId`:
  - standalone page (route `payroll/audit`, declared BEFORE `runs/:id`) → filter bar
    (date-range/action/resource/actor, FR-4) + CSV/Excel export (FR-5) + tenant-wide
    timeline. `applyFilters()` snapshots the ngModel filter bar into an `activeFilters`
    signal then reloads (so a stale edit can't be the one exported).
  - embedded in run-detail `<app-audit-trail [runId]="r.id" />` (after the US-PAY-008
    approval-history block; that block's `mb-24` MOVED to the audit wrapper) → per-run
    timeline (FR-6), filter bar + export HIDDEN.
  Vertical timeline, each card "View changes" toggles a SIDE-BY-SIDE before/after diff
  table that is **desktop-only** (`hidden md:block`); mobile shows a "view on desktop"
  note (§8). Timeline itself stays scrollable on mobile.

### Run-detail spec gotcha (embedded child = new injected dep)
The embedded `AuditTrailComponent` injects `AuditService` + fires `getRunAuditTrail`
on init. The run-detail spec has TWO TestBed configs (`setup` + nested
`setupProcessing`) — BOTH need an `AuditService` spy provider (added the spy to the
shared `makeChildSpies()` helper + both providers arrays), same pattern as the
US-PAY-011 PayslipEmail/Payslip child spies. Full FE suite 2418 green (56 new tests).

### Deferred / not-FE (noted per brief)
- AC-3 (write-side audit log creation), FR-2/FR-3 (logging every write op), NFR-1
  async writes, NFR-3 BRIN indexes, NFR-7 cold-storage archival, AC-5 RLS — all BACKEND.
  The FE is pure READ over `audit_log` + the run history.
- 7-year retention (FR-7/NFR-5) — data-retention policy, backend.
