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
    // US-PAY-003 (§8): run detail — status stepper, live progress, summary card.
    path: 'runs/:id',
    loadComponent: () =>
      import(
        './components/payroll-run-detail/payroll-run-detail.component'
      ).then((m) => m.PayrollRunDetailComponent),
  },
  { path: '', redirectTo: 'structures', pathMatch: 'full' },
];
