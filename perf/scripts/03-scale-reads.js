// Scenario 3 — scale reads / reports / exports against the 5k dataset: 30 VU / 3 min.
// Exercises aggregation paths (report generate over all 5000) + large-page list + export.
// SLA: large-page list p95 <= 400ms; report generate p95 <= 800ms; export p95 <= 2000ms; error-rate < 1%.
// Run: k6 run perf/scripts/03-scale-reads.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE, tenantHeaders, login } from './lib.js';

export const options = {
  scenarios: {
    scale_reads: { executor: 'constant-vus', vus: 30, duration: '3m' },
  },
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{name:list100}':   ['p(95)<400'],
    'http_req_duration{name:headcount}': ['p(95)<800'],
    'http_req_duration{name:deptdist}':  ['p(95)<800'],
    'http_req_duration{name:emptype}':   ['p(95)<800'],
    'http_req_duration{name:export}':    ['p(95)<2000'],
  },
};

export function setup() {
  return { token: login() };
}

export default function (data) {
  const h = tenantHeaders(data.token);
  // large-page list across 5k
  const page = 1 + Math.floor(Math.random() * 50);
  check(http.get(`${BASE}/api/v1/tenant/employees?page=${page}&pageSize=100`, { headers: h, tags: { name: 'list100' } }),
        { 'list100 200': (r) => r.status === 200 });
  // aggregation reports over the full 5000
  check(http.post(`${BASE}/api/v1/reports/headcount/generate`, '{}', { headers: h, tags: { name: 'headcount' } }),
        { 'headcount 200': (r) => r.status === 200 });
  check(http.post(`${BASE}/api/v1/reports/department-distribution/generate`, '{}', { headers: h, tags: { name: 'deptdist' } }),
        { 'deptdist 200': (r) => r.status === 200 });
  check(http.post(`${BASE}/api/v1/reports/employment-type-breakdown/generate`, '{}', { headers: h, tags: { name: 'emptype' } }),
        { 'emptype 200': (r) => r.status === 200 });
  // export less often (heavier; avoids the 3-concurrent FR-10 cap)
  if (__ITER % 10 === 0) {
    const r = http.post(`${BASE}/api/v1/reports/headcount/export`, JSON.stringify({ format: 'csv' }), { headers: h, tags: { name: 'export' } });
    check(r, { 'export 200/429': (x) => x.status === 200 || x.status === 429 });
  }
}
