// Shared helpers for the HRM perf (k6) scenarios.
// Parameterized by env vars (k6 -e KEY=VAL):
//   BASE_URL   default http://localhost:5000
//   SUBDOMAIN  default perf
//   EMAIL      default perfadmin@perf.test
//   PASSWORD   default Admin@123!
import http from 'k6/http';
import { check } from 'k6';

export const BASE = __ENV.BASE_URL || 'http://localhost:5000';
export const SUBDOMAIN = __ENV.SUBDOMAIN || 'perf';
const EMAIL = __ENV.EMAIL || 'perfadmin@perf.test';
const PASSWORD = __ENV.PASSWORD || 'Admin@123!';

// dev-mode tenant resolution header (TenantResolutionMiddleware fallback)
export function tenantHeaders(token) {
  const h = { 'X-Tenant-Subdomain': SUBDOMAIN, 'Content-Type': 'application/json' };
  if (token) h['Authorization'] = `Bearer ${token}`;
  return h;
}

// One login -> bearer token. Called from setup() so bcrypt cost is paid once.
export function login() {
  const res = http.post(
    `${BASE}/api/v1/auth/login`,
    JSON.stringify({ email: EMAIL, password: PASSWORD }),
    { headers: tenantHeaders(), tags: { name: 'login' } },
  );
  check(res, { 'login 200': (r) => r.status === 200 });
  const token = res.json('data.accessToken');
  if (!token) throw new Error(`login failed: ${res.status} ${res.body}`);
  return token;
}
