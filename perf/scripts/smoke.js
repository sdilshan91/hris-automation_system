// B0 smoke: 1 VU, login + one read of each hot endpoint. Must be all 200s.
// Run: k6 run perf/scripts/smoke.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE, tenantHeaders, login } from './lib.js';

export const options = { vus: 1, iterations: 1 };

export function setup() {
  return { token: login() };
}

export default function (data) {
  const h = tenantHeaders(data.token);
  const endpoints = [
    ['employees',  `${BASE}/api/v1/tenant/employees?page=1&pageSize=20`],
    ['widgets',    `${BASE}/api/v1/dashboard/widgets`],
    ['context',    `${BASE}/api/v1/tenant/context`],
    ['reports',    `${BASE}/api/v1/reports`],
  ];
  for (const [name, url] of endpoints) {
    const r = http.get(url, { headers: h, tags: { name } });
    check(r, { [`${name} 200`]: (x) => x.status === 200 });
    console.log(`${name}: ${r.status} (${Math.round(r.timings.duration)}ms)`);
  }
}
