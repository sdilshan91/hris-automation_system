export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000/api/v1',
  baseDomain: 'localhost:4200',
  // Dev-only: backend reads X-Tenant-Subdomain header in Development so the SPA
  // can stay on plain `localhost` without a hosts-file entry for *.localhost.
  // In prod the tenant is resolved from the real subdomain instead.
  // Dev points at the 'techoneglobal' business tenant so "Continue with Microsoft" lands SSO users in a
  // normal HR workspace (CR-AUTH-001); sachithra@techoneglobal.org is its Tenant Owner. Switch back to
  // 'platform' to use the system-admin console / admin@hrm.local.
  tenantSubdomain: 'techoneglobal',
  appName: 'YourHRM',
  tokenRefreshBufferSeconds: 60,
  idleWarningSeconds: 300,
};
