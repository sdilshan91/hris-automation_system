import { Routes } from '@angular/router';

/**
 * US-REC-002: PUBLIC careers routes (anonymous — NO auth/role guard).
 *
 * Lazy-loaded under the top-level `careers` path in app.routes.ts, deliberately
 * OUTSIDE the authenticated MainLayout shell so anonymous applicants can browse
 * and apply (NFR-2). Tenant is still resolved from the subdomain and carried by
 * the tenantInterceptor, so the page is tenant-scoped without a session.
 */
export const CAREERS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/careers/careers-page/careers-page.component').then(
        (m) => m.CareersPageComponent,
      ),
  },
  {
    path: ':id',
    loadComponent: () =>
      import(
        './components/careers/vacancy-detail/vacancy-detail.component'
      ).then((m) => m.CareersVacancyDetailComponent),
  },
];
