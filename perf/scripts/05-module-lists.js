// P3b — module list-endpoint p95 under load (50 VU / 3 min) against the 5k perf tenant.
// Covers the cross-cutting "list loads p95 < SLA" perf TCs not in 01-hot-reads:
// departments, job-titles, locations, custom-fields, notifications, audit-logs.
// SLA (TC NFR-1 family): list reads p95 < 400ms; audit-log p95 < 800ms; error-rate < 1%.
//
// ISSUE-534 — LOAD BUDGET (this is the highest-VU scenario, so it sizes the pool):
//   The global limiter is 300 req/min per (tenantId, userId). Every VU used to be perfadmin, so the whole
//   run shared ONE partition; at 50 VU this script was ~96% 429s and reported the p95 of being throttled.
//   50 VU x 6 req / 3.0s cadence = 6,000 req/min total
//   6,000 / 30 pool users        =   200 req/min per partition  = 67% of the 300/min limiter. OK.
//   Why 3.0s and not the old sleep(0.5): sleep() pads AFTER the requests, so a faster backend produced
//   MORE traffic and silently pushed the run over the ceiling. `pace` fixes total iteration time instead,
//   which is what makes the number above a promise rather than a guess. assertBudget re-checks it at run
//   time and aborts rather than let a bad USER_POOL/VUS combination quietly measure the throttle again.
//
// Run: k6 run perf/scripts/05-module-lists.js
// Reproduce the defect: k6 run -e PIN_USER=1 perf/scripts/05-module-lists.js   (expect http_req_failed RED)
import http from 'k6/http';
import { check } from 'k6';
import { BASE, tenantHeaders, loginPool, pickToken, pace, assertBudget, POOL_SIZE } from './lib.js';

const VUS = 50;
const ITER_SECONDS = 3.0;
const REQS_PER_ITER = 6;

export const options = {
  scenarios: { module_lists: { executor: 'constant-vus', vus: VUS, duration: '3m' } },
  // setup() mints one token per pool user, paced under the 10/min/IP auth-login policy: ~1 min per 9 users.
  setupTimeout: '10m',
  thresholds: {
    // ISSUE-534 detection half. A limiter-dominated run is ~96% 429 and goes RED here even though every
    // p95 below would still read green — 429s return in ~2ms.
    'http_req_failed': ['rate<0.01'],
    // ...and `status:200` keeps those 2ms 429s OUT of the latency numbers, so a p95 that is printed is the
    // p95 of the endpoint rather than of the throttle. (An all-429 tag leaves its submetric EMPTY, which k6
    // scores as passing — that blind spot is exactly what the http_req_failed threshold above covers.)
    'http_req_duration{name:departments,status:200}':  ['p(95)<400'],
    'http_req_duration{name:jobtitles,status:200}':    ['p(95)<400'],
    'http_req_duration{name:locations,status:200}':    ['p(95)<400'],
    'http_req_duration{name:customfields,status:200}': ['p(95)<400'],
    'http_req_duration{name:notifications,status:200}':['p(95)<400'],
    'http_req_duration{name:auditlogs,status:200}':    ['p(95)<800'],
  },
};

export function setup() {
  assertBudget({ vus: VUS, reqsPerIteration: REQS_PER_ITER, iterationSeconds: ITER_SECONDS, poolSize: POOL_SIZE });
  return { tokens: loginPool() };
}

export default function (data) {
  const t0 = Date.now();
  const h = tenantHeaders(pickToken(data.tokens));
  const g = (name, url) => check(http.get(`${BASE}${url}`, { headers: h, tags: { name } }),
    { [`${name} 200`]: (r) => r.status === 200 });
  g('departments',  '/api/v1/tenant/departments');
  g('jobtitles',    '/api/v1/tenant/job-titles');
  g('locations',    '/api/v1/tenant/locations');
  g('customfields', '/api/v1/tenant/custom-fields?entityType=Employee');
  g('notifications','/api/v1/notifications');
  g('auditlogs',    '/api/v1/tenant/audit-logs?page=1&pageSize=50');
  pace(t0, ITER_SECONDS);
}
