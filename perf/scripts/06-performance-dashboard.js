// Scenario 6 — Performance-module dashboard against the 5k perf tenant: 20 VU / 3 min.
// ENH-013(b). Requires perf/seed/perf-volume-seed.sql (WS-D) to have been applied — without it the four
// routes below 404 with errorCode `no_cycle`, which is exactly why TC-PRF-007-11 sat blocked.
//
// Covers the four dashboard reads (US-PRF-007):
//   overview · trend (multi-cycle) · department drill-down · export
//
// SLA, taken verbatim from the story — a breach is a FINDING, not a reason to relax the number:
//   NFR-1  dashboard <= 2.5s P95 at 5,000 employees   -> overview / trend / drill-down
//   NFR-5  export    <= 5s   for 5,000 employees      -> export
//
// Why `trend` gets its own worst-case arm: GetTrendAsync re-runs the whole LoadPopulationAsync pipeline
// once PER CYCLE in a foreach, and when `cycleIds` is OMITTED it trends every non-probation cycle. The
// seed lays down four of them, each with reviews for all 5000, so the unpinned arm is a real N+1-by-cycle
// worst case rather than a single-cycle read wearing a different route.
//
// LOAD SIZING — this scenario is deliberately SMALL, and that is not timidity:
//   Program.cs installs a global fixed-window limiter of PermitLimit=300 / Window=1min, partitioned by
//   (tenantId, userId). The perf tenant has exactly ONE user, so the whole script shares one partition and
//   anything over 300 req/min is rejected with 429 in ~2ms. A first run at 20 VU sent 26,182 requests in
//   3 minutes and 901 succeeded — 300 x 3 windows. The p95 it printed was the p95 of being rate-limited,
//   which is worse than no number at all because it looks like a pass.
//   So: 5 VU x ~5.1 requests x one iteration per ~6.6s ~= 234 req/min, safely inside the window. Raising VUs
//   here does NOT increase realism, it just measures the limiter. To load beyond 300/min, seed additional
//   perf users so the partition key varies.
//
// Run: k6 run perf/scripts/06-performance-dashboard.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, tenantHeaders, login } from './lib.js';

// Fixed cycle ids from perf-volume-seed.sql WS-D (literal, not gen_random_uuid, so they pin across re-runs).
const CYCLE_FY2025 = '5f000000-0000-4000-8000-0000000000d3';
const CYCLE_FY2026 = '5f000000-0000-4000-8000-0000000000d4'; // also the default: greatest start_date

export const options = {
  scenarios: { performance_dashboard: { executor: 'constant-vus', vus: 5, duration: '5m' } },
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{name:overview}':      ['p(95)<2500'], // NFR-1
    'http_req_duration{name:overview_filt}': ['p(95)<2500'], // NFR-1, department+employmentType filtered
    'http_req_duration{name:trend_all}':     ['p(95)<2500'], // NFR-1, worst case: all cycles, N+1 by cycle
    'http_req_duration{name:trend_pinned}':  ['p(95)<2500'], // NFR-1, 2 cycles + department series overlay
    'http_req_duration{name:drilldown}':     ['p(95)<2500'], // NFR-1
    'http_req_duration{name:export_csv}':    ['p(95)<5000'], // NFR-5
    'http_req_duration{name:export_xlsx}':   ['p(95)<5000'], // NFR-5
  },
};

// Department ids are gen_random_uuid() in seed-perf-tenant.sql, so they cannot be hardcoded — resolve one
// at setup time. Fails loudly rather than silently drilling into a 404 for the whole run.
export function setup() {
  const token = login();
  const res = http.get(`${BASE}/api/v1/tenant/departments`, { headers: tenantHeaders(token) });
  check(res, { 'departments 200': (r) => r.status === 200 });
  const items = res.json('data.items') || res.json('data') || [];
  const departmentId = items.length ? (items[0].id || items[0].Id) : null;
  if (!departmentId) throw new Error(`no department in perf tenant: ${res.status} ${res.body}`);
  return { token, departmentId };
}

export default function (data) {
  const h = tenantHeaders(data.token);
  const root = `${BASE}/api/v1/tenant/performance/dashboard`;
  const g = (name, url) => {
    const r = http.get(url, { headers: h, tags: { name } });
    // 404 = `no_cycle`: the WS-D seed was not applied. Surface it as a failed check, not a silent pass.
    check(r, { [`${name} 200`]: (x) => x.status === 200 });
    return r;
  };

  // 1. default cycle (FY2026), unfiltered — the NFR-1 headline read over all 5000.
  g('overview', `${root}/overview`);

  // 2. same read with the filters the UI actually sends.
  g('overview_filt', `${root}/overview?departmentId=${data.departmentId}&employmentType=FullTime&topBottomCount=10`);

  // 3. trend, cycleIds omitted -> every non-probation cycle. Worst case by design.
  g('trend_all', `${root}/trend`);

  // 4. trend pinned to 2 cycles WITH the per-department series overlay.
  g('trend_pinned', `${root}/trend?cycleIds=${CYCLE_FY2025}&cycleIds=${CYCLE_FY2026}&includeDepartmentSeries=true`);

  // 5. department drill-down (departmentId is a ROUTE value here, not a query param).
  g('drilldown', `${root}/department/${data.departmentId}?cycleId=${CYCLE_FY2026}`);

  // 6. exports are heavier — run them on a subset of iterations so they do not dominate the profile.
  if (__ITER % 10 === 0) {
    const r = http.get(`${root}/export?format=csv`, { headers: h, tags: { name: 'export_csv' } });
    check(r, { 'export_csv 200': (x) => x.status === 200 });
  }
  if (__ITER % 25 === 0) {
    const r = http.get(`${root}/export?format=xlsx`, { headers: h, tags: { name: 'export_xlsx' } });
    check(r, { 'export_xlsx 200': (x) => x.status === 200 });
  }

  // Pacing, not politeness: keeps aggregate throughput under the 300/min global limiter (see header).
  sleep(6);
}
