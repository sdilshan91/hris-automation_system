export const environment = {
  production: true,
  apiBaseUrl: 'https://api.yourhrm.com/api/v1',
  baseDomain: 'yourhrm.com',
  appName: 'YourHRM',
  tokenRefreshBufferSeconds: 60,
  idleWarningSeconds: 300,
  // Set a prod GlitchTip DSN here when prod error-tracking is stood up. Empty => disabled.
  sentryDsn: '',
};
