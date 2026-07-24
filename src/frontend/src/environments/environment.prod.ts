export const environment = {
  production: true,
  apiBaseUrl: 'https://api.yourhrm.com/api/v1',
  baseDomain: 'yourhrm.com',
  appName: 'YourHRM',
  tokenRefreshBufferSeconds: 60,
  idleWarningSeconds: 300,
  // Public client_id of the vendor-owned multi-tenant Entra app (NOT a secret) used to build
  // the per-tenant admin-consent URL in SSO settings (US-AUTH-012 / US-AUTH-016). Empty until
  // the platform app registration is provisioned; the UI shows a {client-id} placeholder then.
  ssoClientId: '',
};
