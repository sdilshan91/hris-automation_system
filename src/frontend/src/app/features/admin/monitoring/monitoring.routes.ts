import { Routes } from '@angular/router';

/**
 * US-ADM-002: System Admin Console — platform monitoring routes (lazy-loaded).
 * These live in the platform/system-admin context (admin.yourhrm.com), gated at
 * the app-routes level. Both System Admin and System Support may view (BR-1);
 * privileged quick-actions are hidden in-component for System Support.
 */
export const MONITORING_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import(
        './components/monitoring-dashboard/monitoring-dashboard.component'
      ).then((m) => m.MonitoringDashboardComponent),
  },
  {
    path: 'tenants/:id',
    loadComponent: () =>
      import(
        './components/tenant-monitoring-detail/tenant-monitoring-detail.component'
      ).then((m) => m.TenantMonitoringDetailComponent),
  },
];
