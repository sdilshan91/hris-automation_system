// Scenario 1 — hot reads under sustained load: 50 VU / 5 min.
// Endpoints: employee list (paged), dashboard widgets, tenant-context, reports catalog.
// SLA (from TC NFR-1): list-load p95 <= 400ms; other reads p95 <= 800ms; error-rate < 1%.
//
// ISSUE-534 — LOAD BUDGET (global limiter is 300 req/min per (tenantId, userId)):
//   50 VU x 4 req / 2.0s cadence = 6,000 req/min total
//   6,000 / 30 pool users        =   200 req/min per partition = 67% of the limiter. OK.
//   Longest run here (5 min) also sets the pool ceiling: minting 30 tokens costs ~3.3 min against the
//   10/min/IP auth-login policy, and an access token lives 15 min — 3.3 + 5 = 8.3 min, comfortably inside.
//   A bigger pool would buy throughput but push mint+run past the token TTL. See lib.js.
//
// Run: k6 run perf/scripts/01-hot-reads.js
// Reproduce the defect: k6 run -e PIN_USER=1 perf/scripts/01-hot-reads.js   (expect http_req_failed RED)
import http from 'k6/http';
import { check } from 'k6';
import { BASE, tenantHeaders, loginPool, pickToken, pace, assertBudget, POOL_SIZE } from './lib.js';

const VUS = 50;
const ITER_SECONDS = 2.0;
const REQS_PER_ITER = 4;

export const options = {
  scenarios: {
    hot_reads: { executor: 'constant-vus', vus: VUS, duration: '5m' },
  },
  setupTimeout: '10m', // pool logins are paced under the 10/min/IP auth-login policy: ~1 min per 9 users
  thresholds: {
    // ISSUE-534 detection half: a limiter-dominated run is ~90% 429 and goes RED here.
    'http_req_failed': ['rate<0.01'],
    // `status:200` keeps 2ms 429s out of the latency numbers — otherwise the p95 printed is the p95 of
    // being throttled, which reads FASTER than the real endpoint and looks like a pass.
    'http_req_duration{name:employees,status:200}': ['p(95)<400'],
    'http_req_duration{name:widgets,status:200}':   ['p(95)<800'],
    'http_req_duration{name:context,status:200}':   ['p(95)<400'],
    'http_req_duration{name:reports,status:200}':   ['p(95)<800'],
  },
};

export function setup() {
  assertBudget({ vus: VUS, reqsPerIteration: REQS_PER_ITER, iterationSeconds: ITER_SECONDS, poolSize: POOL_SIZE });
  return { tokens: loginPool() };
}

export default function (data) {
  const t0 = Date.now();
  const h = tenantHeaders(pickToken(data.tokens));
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
  pace(t0, ITER_SECONDS);
}
