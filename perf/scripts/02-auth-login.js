// Scenario 2 — auth/login throughput. Login is intentionally expensive (BCrypt),
// so this measures the auth path under concurrency, not raw read speed.
// Ramps to 20 VU over 2 min. SLA: login p95 <= 800ms; error-rate < 1%.
// Run: k6 run perf/scripts/02-auth-login.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE, tenantHeaders } from './lib.js';

const EMAIL = __ENV.EMAIL || 'perfadmin@perf.test';
const PASSWORD = __ENV.PASSWORD || 'Admin@123!';

export const options = {
  scenarios: {
    login_load: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '30s', target: 10 },
        { duration: '60s', target: 20 },
        { duration: '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{name:login}': ['p(95)<800'],
  },
};

export default function () {
  const res = http.post(
    `${BASE}/api/v1/auth/login`,
    JSON.stringify({ email: EMAIL, password: PASSWORD }),
    { headers: tenantHeaders(), tags: { name: 'login' } },
  );
  check(res, {
    'login 200': (r) => r.status === 200,
    'has token': (r) => !!r.json('data.accessToken'),
  });
}
