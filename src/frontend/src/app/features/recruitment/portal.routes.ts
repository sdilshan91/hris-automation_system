import { Routes } from '@angular/router';

/**
 * US-REC-008: ANONYMOUS candidate-portal routes (magic link — NO auth/role guard).
 *
 * Lazy-loaded under the top-level `portal` path in app.routes.ts, deliberately
 * OUTSIDE the authenticated MainLayout shell (mirrors the public careers routes).
 * Access is controlled by the `?token=` magic-link query param, resolved by the
 * backend (NFR-3/NFR-4/BR-6). Tenant is still resolved from the subdomain and
 * carried by the tenantInterceptor, so the portal is tenant-scoped without a
 * session (AC-4/BR-4).
 */
export const PORTAL_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import(
        './components/portal/candidate-portal/candidate-portal.component'
      ).then((m) => m.CandidatePortalComponent),
  },
];
