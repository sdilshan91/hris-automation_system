// Scenario 1 — hot reads under sustained load: 50 VU / 5 min.
// Endpoints: employee list (paged), dashboard widgets, tenant-context, reports catalog.
// SLA (from TC NFR-1): list-load p95 <= 400ms; other reads p95 <= 800ms; error-rate < 1%.
// Run: k6 run perf/scripts/01-hot-reads.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, tenantHeaders, login } from './lib.js';

export const options = {
  scenarios: {
    hot_reads: { executor: 'constant-vus', vus: 50, duration: '5m' },
  },
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{name:employees}': ['p(95)<400'],
    'http_req_duration{name:widgets}':   ['p(95)<800'],
    'http_req_duration{name:context}':   ['p(95)<400'],
    'http_req_duration{name:reports}':   ['p(95)<800'],
  },
};

export function setup() {
  return { token: login() };
}

export default function (data) {
  const h = tenantHeaders(data.token);
  // employee list — random page across the 5k dataset (250 pages of 20)
  const page = 1 + Math.floor(Math.random() * 250);
  check(http.get(`${BASE}/api/v1/tenant/employees?page=${page}&pageSize=20`, { headers: h, tags: { name: 'employees' } }),
        { 'employees 200': (r) => r.status === 200 });
  check(http.get(`${BASE}/api/v1/dashboard/widgets`, { headers: h, tags: { name: 'widgets' } }),
        { 'widgets 200': (r) => r.status === 200 });
  check(http.get(`${BASE}/api/v1/tenant/context`, { headers: h, tags: { name: 'context' } }),
        { 'context 200': (r) => r.status === 200 });
  check(http.get(`${BASE}/api/v1/reports`, { headers: h, tags: { name: 'reports' } }),
        { 'reports 200': (r) => r.status === 200 });
  sleep(0.5);
}
