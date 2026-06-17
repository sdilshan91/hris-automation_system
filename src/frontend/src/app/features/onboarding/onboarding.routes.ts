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
  {
    // US-ONB-002: assign a checklist to a specific new hire (routed input()).
    path: 'assign/:employeeId',
    loadComponent: () =>
      import(
        './components/checklist-assignment/checklist-assignment.component'
      ).then((m) => m.ChecklistAssignmentComponent),
  },
  {
    // US-ONB-004: HR issues assets to a specific new hire (routed input()).
    // Optional ?taskInstanceId= links it to the originating checklist task (FR-1).
    path: 'assets/issue/:employeeId',
    loadComponent: () =>
      import('./components/asset-issuance/asset-issuance.component').then(
        (m) => m.AssetIssuanceComponent,
      ),
  },
  {
    // US-ONB-003: the logged-in new hire's own checklist (FR-1). No role guard —
    // any authenticated employee with an assigned checklist self-serves here; the
    // backend scopes to the caller's identity + tenant.
    path: 'my-checklist',
    loadComponent: () =>
      import('./components/my-checklist/my-checklist.component').then(
        (m) => m.MyChecklistComponent,
      ),
  },
  { path: '', redirectTo: 'templates', pathMatch: 'full' },
];
