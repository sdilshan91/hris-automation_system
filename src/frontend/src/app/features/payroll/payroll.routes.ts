import { Routes } from '@angular/router';

/**
 * US-PAY-001: Payroll feature routes (first Payroll story — establishes the
 * feature folder). Lazy-loaded under the 'payroll' path in app.routes.ts; the
 * parent route applies roleGuard(['Tenant Admin', 'HR Officer']) — salary
 * configuration requires the Payroll.*.All capability. The backend enforces the
 * actual permission (RLS + authz); the route guard scopes by role.
 *
 * Later Payroll stories add sibling child routes here (structure detail with the
 * mock-payslip breakdown, payroll runs, payslips, etc.).
 */
export const PAYROLL_ROUTES: Routes = [
  {
    // AC-3: card-based salary-structures list (the default Payroll screen).
    path: 'structures',
    loadComponent: () =>
      import(
        './components/salary-structures/salary-structures.component'
      ).then((m) => m.SalaryStructuresComponent),
  },
  {
    // AC-1/AC-2/AC-4/AC-5: inline-editable salary-components table + slide-over.
    path: 'components',
    loadComponent: () =>
      import(
        './components/salary-components/salary-components.component'
      ).then((m) => m.SalaryComponentsComponent),
  },
  {
    // US-PAY-002 FR-5/AC-4: spreadsheet-like bulk salary-structure assignment.
    path: 'bulk-assign',
    loadComponent: () =>
      import(
        './components/bulk-salary-assignment/bulk-salary-assignment.component'
      ).then((m) => m.BulkSalaryAssignmentComponent),
  },
  {
    // US-PAY-003 (§8): Notion-style payroll runs table; "New run" modal lives here.
    path: 'runs',
    loadComponent: () =>
      import('./components/payroll-runs/payroll-runs.component').then(
        (m) => m.PayrollRunsComponent,
      ),
  },
  {
    // US-PAY-008 (§8): approver "Pending Approvals" queue — runs in
    // AwaitingApproval, gated by Payroll.Approve at the backend; cards link to
    // runs/:id for the review + approve/reject/return actions. Declared BEFORE
    // 'runs/:id' so it is matched literally (it is a sibling, not a run id).
    path: 'approvals',
    loadComponent: () =>
      import(
        './components/pending-approvals/pending-approvals.component'
      ).then((m) => m.PendingApprovalsComponent),
  },
  {
    // US-PAY-010 (FR-7, §8, AC-3/AC-4): pre-payroll reconciliation — per-employee
    // attendance/leave summary table with color-coded cells + mismatch filter, the
    // attendance-not-finalized warning banner (AC-4), and the leave-encashment
    // trigger drawer (AC-3). Declared BEFORE 'runs/:id' so it is matched literally.
    path: 'reconciliation',
    loadComponent: () =>
      import(
        './components/payroll-reconciliation/payroll-reconciliation.component'
      ).then((m) => m.PayrollReconciliationComponent),
  },
  {
    // US-PAY-003 (§8): run detail — status stepper, live progress, summary card.
    // US-PAY-008 extends it with the approval review layout + action bar + history.
    path: 'runs/:id',
    loadComponent: () =>
      import(
        './components/payroll-run-detail/payroll-run-detail.component'
      ).then((m) => m.PayrollRunDetailComponent),
  },
  {
    // US-PAY-007 (§8): payroll adjustments — Notion table with filters + tabs,
    // "New Adjustment" slide-over, bulk CSV upload, cancel pending.
    path: 'adjustments',
    loadComponent: () =>
      import(
        './components/payroll-adjustments/payroll-adjustments.component'
      ).then((m) => m.PayrollAdjustmentsComponent),
  },
  {
    // US-PAY-006 (§8): statutory configuration — tax slabs, PF, other deductions,
    // fiscal-year versioning, test-calc panel + version history.
    path: 'statutory',
    loadComponent: () =>
      import(
        './components/statutory-configuration/statutory-configuration.component'
      ).then((m) => m.StatutoryConfigurationComponent),
  },
  {
    // US-PAY-009 (§8, AC-1/AC-2/AC-4): Payroll Reports page — Notion-style report-type
    // sidebar, filter panel (period + department), preview table + bar chart, bank-advice
    // preview with masked accounts, and CSV/Excel/PDF blob exports.
    path: 'reports',
    loadComponent: () =>
      import(
        './components/payroll-reports/payroll-reports.component'
      ).then((m) => m.PayrollReportsComponent),
  },
  {
    // US-PAY-009 (FR-5): payroll analytics dashboard — card-based pure-SVG charts
    // (monthly trend line, department cost bars, statutory stacked bars).
    path: 'analytics',
    loadComponent: () =>
      import(
        './components/payroll-analytics/payroll-analytics.component'
      ).then((m) => m.PayrollAnalyticsComponent),
  },
  { path: '', redirectTo: 'structures', pathMatch: 'full' },
];
