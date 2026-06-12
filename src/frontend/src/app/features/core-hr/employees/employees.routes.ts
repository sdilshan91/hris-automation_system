import { Routes } from '@angular/router';

/**
 * US-CHR-001 / US-CHR-002 / US-CHR-003 / US-CHR-010 / US-CHR-011: Employee management routes.
 *
 * Lazy-loaded under the 'employees' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 *
 * US-CHR-002: Profile route (:id) accessible to HR Officers, Employees (own profile),
 * and Managers (direct reports). Fine-grained access is enforced by the backend;
 * the route guard allows all authenticated users with the listed roles.
 *
 * US-CHR-010: Bulk import route. Role-guarded by the parent employees route
 * (Tenant Admin, HR Officer). Placed before :id to avoid path collision.
 *
 * US-CHR-011: My Team route. Accessible to Manager, HR Officer, Tenant Admin.
 * Placed before :id to avoid path collision.
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
  {
    path: 'import',
    loadComponent: () =>
      import('./components/bulk-import/bulk-import.component').then(
        (m) => m.BulkImportComponent
      ),
  },
  {
    path: 'my-team',
    loadComponent: () =>
      import('./components/my-team/my-team.component').then(
        (m) => m.MyTeamComponent
      ),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./components/employee-profile/employee-profile.component').then(
        (m) => m.EmployeeProfileComponent
      ),
  },
];
