import { Routes } from '@angular/router';

/**
 * US-CHR-006: Organization Tree routes.
 *
 * Lazy-loaded under the 'org-tree' path in app.routes.ts.
 * The parent route applies roleGuard(['Tenant Admin', 'HR Officer', 'Manager']).
 */
export const ORG_TREE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/org-tree-page/org-tree-page.component').then(
        (m) => m.OrgTreePageComponent
      ),
  },
];
