import { Routes } from '@angular/router';

/**
 * US-ADM-008: Tenant Admin audit-log routes.
 * Mounted under `/admin/audit-log` inside the authenticated MainLayout and gated
 * by roleGuard(['Tenant Admin', 'Tenant Owner', 'Auditor']) in app.routes.ts
 * (BR-1). The Auditor role has read-only access (FR-7).
 */
export const AUDIT_LOG_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/audit-log-list/audit-log-list.component').then(
        (m) => m.AuditLogListComponent
      ),
  },
];
