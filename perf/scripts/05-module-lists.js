// P3b — module list-endpoint p95 under load (50 VU / 3 min) against the 5k perf tenant.
// Covers the cross-cutting "list loads p95 < SLA" perf TCs not in 01-hot-reads:
// departments, job-titles, locations, custom-fields, notifications, audit-logs.
// SLA (TC NFR-1 family): list reads p95 < 400ms; audit-log p95 < 800ms; error-rate < 1%.
// Run: k6 run perf/scripts/05-module-lists.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE, tenantHeaders, login } from './lib.js';

export const options = {
  scenarios: { module_lists: { executor: 'constant-vus', vus: 50, duration: '3m' } },
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{name:departments}':  ['p(95)<400'],
    'http_req_duration{name:jobtitles}':    ['p(95)<400'],
    'http_req_duration{name:locations}':    ['p(95)<400'],
    'http_req_duration{name:customfields}': ['p(95)<400'],
    'http_req_duration{name:notifications}':['p(95)<400'],
    'http_req_duration{name:auditlogs}':    ['p(95)<800'],
  },
};

export function setup() { return { token: login() }; }

export default function (data) {
  const h = tenantHeaders(data.token);
  const g = (name, url) => check(http.get(`${BASE}${url}`, { headers: h, tags: { name } }),
    { [`${name} 200`]: (r) => r.status === 200 });
  g('departments',  '/api/v1/tenant/departments');
  g('jobtitles',    '/api/v1/tenant/job-titles');
  g('locations',    '/api/v1/tenant/locations');
  g('customfields', '/api/v1/tenant/custom-fields?entityType=Employee');
  g('notifications','/api/v1/notifications');
  g('auditlogs',    '/api/v1/tenant/audit-logs?page=1&pageSize=50');
  sleep(0.5);
}
