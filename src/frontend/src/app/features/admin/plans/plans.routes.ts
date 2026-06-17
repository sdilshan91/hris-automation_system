import { Routes } from '@angular/router';

/**
 * US-ADM-009: System Admin Console — subscription-plan routes (lazy-loaded).
 * These live in the platform/system-admin context (admin.yourhrm.com), gated at
 * the app-routes level by roleGuard(['System Admin']) (BR-1). Only the System
 * Admin role may create/edit/archive plans; tenant roles cannot reach them.
 *
 * NOTE: the static `new` and `compare` paths are registered BEFORE the dynamic
 * `:id` editor route so they keep priority.
 */
export const PLANS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/plan-list/plan-list.component').then(
        (m) => m.PlanListComponent,
      ),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./components/plan-editor/plan-editor.component').then(
        (m) => m.PlanEditorComponent,
      ),
  },
  {
    path: 'compare',
    loadComponent: () =>
      import('./components/plan-comparison/plan-comparison.component').then(
        (m) => m.PlanComparisonComponent,
      ),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./components/plan-editor/plan-editor.component').then(
        (m) => m.PlanEditorComponent,
      ),
  },
];
