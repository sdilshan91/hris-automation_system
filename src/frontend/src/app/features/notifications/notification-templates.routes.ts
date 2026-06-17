import { Routes } from '@angular/router';

/**
 * US-NTF-002: Email Notification Templates per Tenant — Tenant Admin settings.
 *
 * Mounted under `/admin/notification-templates` inside the authenticated
 * MainLayout and gated by roleGuard(['Tenant Admin', 'Tenant Owner']) in
 * app.routes.ts (matching the sibling tenant-admin settings features such as
 * US-ADM-006 company-settings). Two lazy screens:
 *   ''            — the event-type list (AC-1)
 *   ':eventKey'   — the split-pane editor + live preview (AC-2..AC-4)
 */
export const NOTIFICATION_TEMPLATES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import(
        './components/template-list/template-list.component'
      ).then((m) => m.TemplateListComponent),
  },
  {
    path: ':eventKey',
    loadComponent: () =>
      import(
        './components/template-editor/template-editor.component'
      ).then((m) => m.TemplateEditorComponent),
  },
];
