import { Routes } from '@angular/router';

/**
 * US-NTF-001: notifications feature routes. The panel's "View All" link (§8)
 * lands on the full notifications page. Lazy-loaded under the authenticated shell.
 */
export const NOTIFICATIONS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import(
        './components/notification-list-page/notification-list-page.component'
      ).then((m) => m.NotificationListPageComponent),
  },
];
