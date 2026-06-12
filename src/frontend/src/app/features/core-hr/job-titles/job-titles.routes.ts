import { Routes } from '@angular/router';

/**
 * US-CHR-005: Job Title management routes.
 *
 * Lazy-loaded under the 'job-titles' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer']).
 */
export const JOB_TITLE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/job-title-list/job-title-list.component').then(
        (m) => m.JobTitleListComponent
      ),
  },
];
