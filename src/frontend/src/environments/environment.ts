export const environment = {
  production: false,
  // Same-origin: API calls go to the SPA's own host (e.g. techone.myhrm.org:4200) and the Angular
  // dev-server proxy (proxy.conf.json) forwards /api -> http://localhost:5000. Keeping it same-origin
  // means the SameSite=Strict refresh cookie is sent so sessions persist per subdomain.
  apiBaseUrl: '/api/v1',
  // Real subdomain multi-tenancy locally: the tenant is resolved from the hostname
  // (techone.myhrm.org -> "techone") via the hosts-file entries -> 127.0.0.1.
  baseDomain: 'myhrm.org',
  // No dev tenant pin — the hostname is the tenant. Plain myhrm.org (no subdomain) = root context.
  // Override still possible via ?tenant=<subdomain> when on a non-subdomain host.
  tenantSubdomain: '',
  appName: 'YourHRM',
  tokenRefreshBufferSeconds: 60,
  idleWarningSeconds: 300,
};
