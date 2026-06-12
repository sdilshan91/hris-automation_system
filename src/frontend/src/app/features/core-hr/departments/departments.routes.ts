import { Routes } from '@angular/router';

/**
 * US-CHR-004: Department management routes.
 *
 * Lazy-loaded under the 'departments' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 */
export const DEPARTMENT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/department-list/department-list.component').then(
        (m) => m.DepartmentListComponent
      ),
  },
];
