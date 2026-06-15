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
