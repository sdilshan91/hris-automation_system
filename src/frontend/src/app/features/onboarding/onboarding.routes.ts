import { Routes } from '@angular/router';

/**
 * US-ONB-001: Onboarding/Offboarding routes.
 *
 * Lazy-loaded under the 'onboarding' path in app.routes.ts. The parent route
 * applies roleGuard(['Tenant Admin', 'HR Officer', 'HR Manager']) — managing
 * onboarding templates requires the Onboarding.Manage capability; the backend
 * enforces the actual permission + tenant RLS (AC-5), the route guard scopes by
 * role. First Onboarding screen; later stories add sibling child routes here.
 */
export const ONBOARDING_ROUTES: Routes = [
  {
    // Template list with Clone (FR-6) + Activate/Deactivate (FR-7).
    path: 'templates',
    loadComponent: () =>
      import('./components/template-list/template-list.component').then(
        (m) => m.TemplateListComponent,
      ),
  },
  {
    // Builder — blank (?cloneFrom=:id pre-fills a copy, FR-6).
    path: 'templates/new',
    loadComponent: () =>
      import('./components/template-builder/template-builder.component').then(
        (m) => m.TemplateBuilderComponent,
      ),
  },
  { path: '', redirectTo: 'templates', pathMatch: 'full' },
];
