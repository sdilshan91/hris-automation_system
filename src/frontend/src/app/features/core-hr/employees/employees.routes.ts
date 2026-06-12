import { Routes } from '@angular/router';

/**
 * US-CHR-001: Employee management routes.
 *
 * Lazy-loaded under the 'employees' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 */
export const EMPLOYEE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/employee-list/employee-list.component').then(
        (m) => m.EmployeeListComponent
      ),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./components/employee-wizard/employee-wizard.component').then(
        (m) => m.EmployeeWizardComponent
      ),
  },
];
