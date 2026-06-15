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

### Deferred (NOT in US-PAY-001)
Structure detail w/ mock-payslip breakdown, linking components↔structure with
overrides (FR-3 junction), version history (FR-7), the active-structure earning-
component guard (FR-5). Later Payroll stories add sibling child routes under
`payroll.routes.ts`.
