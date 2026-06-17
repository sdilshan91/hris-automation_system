import { Routes } from '@angular/router';

/**
 * US-NTF-003: notification-preferences routes. Lives under the user's Profile area
 * (Profile > Notification Preferences). No extra roleGuard — preferences are
 * personal to the signed-in user; the parent authGuard is sufficient and the
 * backend scopes to identity + tenant membership (BR-4).
 */
export const NOTIFICATION_PREFERENCES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import(
        './components/notification-preferences/notification-preferences.component'
      ).then((m) => m.NotificationPreferencesComponent),
  },
];
