import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/auth.guard';

/**
 * US-RPT-001: Pre-Built HR Reports routes.
 * Mounted under `/reports` inside the authenticated MainLayout and gated by
 * permissionGuard(['Reports.View']) in app.routes.ts. The catalog is the index;
 * the viewer is keyed by report type (`/reports/:type`). The per-route
 * permission guard here is defence-in-depth alongside the parent role gate.
 */
export const REPORTS_ROUTES: Routes = [
  {
    path: '',
    canActivate: [permissionGuard(['Reports.View'])],
    loadComponent: () =>
      import('./components/report-catalog/report-catalog.component').then(
        (m) => m.ReportCatalogComponent
      ),
  },
  {
    path: ':type',
    canActivate: [permissionGuard(['Reports.View'])],
    loadComponent: () =>
      import('./components/report-viewer/report-viewer.component').then(
        (m) => m.ReportViewerComponent
      ),
  },
];
