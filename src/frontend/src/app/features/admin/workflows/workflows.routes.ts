import { Routes } from '@angular/router';

/**
 * US-ADM-007: Tenant Admin approval-workflow routes.
 * Mounted under `/admin/workflows` inside the authenticated MainLayout and gated
 * by roleGuard(['Tenant Admin', 'Tenant Owner']) in app.routes.ts (BR-1).
 *
 * NOTE: the static `new` route is declared BEFORE the `:id` editor route so a
 * fresh-workflow create does not get captured as an id (matching the route
 * ordering discipline used by the other admin features).
 */
export const WORKFLOWS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/workflow-list/workflow-list.component').then(
        (m) => m.WorkflowListComponent
      ),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./components/workflow-editor/workflow-editor.component').then(
        (m) => m.WorkflowEditorComponent
      ),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./components/workflow-editor/workflow-editor.component').then(
        (m) => m.WorkflowEditorComponent
      ),
  },
];
