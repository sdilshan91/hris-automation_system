import { Routes } from '@angular/router';

/**
 * US-LV-001: Leave management routes.
 *
 * Lazy-loaded under the 'leave-types' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 */
export const LEAVE_MANAGEMENT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/leave-type-list/leave-type-list.component').then(
        (m) => m.LeaveTypeListComponent
      ),
  },
];
