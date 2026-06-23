export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000/api/v1',
  baseDomain: 'localhost:4200',
  // Dev-only: backend reads X-Tenant-Subdomain header in Development so the SPA
  // can stay on plain `localhost` without a hosts-file entry for *.localhost.
  // In prod the tenant is resolved from the real subdomain instead.
  // Dev-only default tenant. Override at runtime without editing this file via ?tenant=<subdomain>
  // (TenantService persists it) — e.g. ?tenant=techoneglobal for SSO, ?tenant=e2e for E2E tests.
  tenantSubdomain: 'platform',
  appName: 'YourHRM',
  tokenRefreshBufferSeconds: 60,
  idleWarningSeconds: 300,
};
