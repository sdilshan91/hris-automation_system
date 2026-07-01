---
id: TC-ATT-139
user_story: US-ATT-010
module: Attendance
priority: high
type: performance
status: blocked
exec_note: "S1: needs S5 (live WS/SignalR client)."
created: 2026-06-15
---

# TC-ATT-139: Performance -- dashboard < 2s P95 (NFR-1); custom report 5,000 employees / 30 days < 15s (NFR-3); live board < 3s via SignalR (NFR-2, DEFERRED -> polling measured)

## 1. Test Objective
Verify the US-ATT-010 performance NFRs: the attendance dashboard loads within 2s at P95 (NFR-1), a custom report over 5,000 employees and 30 days completes within 15s (NFR-3), and the live board reflects clock-in/out within 3s via SignalR (NFR-2 -- DEFERRED on the real-time infra; the polling-refresh path is measured now).

## 2. Related Requirements
- User Story: US-ATT-010
- Non-Functional: NFR-1 (dashboard < 2s P95), NFR-2 (live board < 3s via SignalR), NFR-3 (report 5,000 emp / 30 days < 15s)
- API: GET /dashboard, /dashboard/live-board, /reports/custom (+ export)

## 3. Preconditions
- Tenant "perf-acme" seeded with 5,000 active employees and 30 days of attendance + a generated monthly summary.
- Measured against the DB-backed/materialized path (Redis CONDITIONAL -- see TC-ATT-138); representative concurrent load.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employees | 5,000 | scale |
| report range | 30 days | NFR-3 |
| dashboard SLA | < 2s P95 | NFR-1 |
| report SLA | < 15s | NFR-3 |
| live-board SLA | < 3s | NFR-2 (SignalR, DEFERRED) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load-test `GET /dashboard` at representative concurrency | P95 latency < 2s (NFR-1) on the DB-computed path; warm-cache target re-measured once Redis lands (TC-ATT-138). |
| 2 | Run a custom report over 5,000 employees / 30 days | completes in < 15s (NFR-3); the response streams/paginates rather than materializing an unbounded payload. |
| 3 | Export the 5,000-employee report (CSV/XLSX) | completes within the export SLA / routes to the async Hangfire path above the sync threshold (TC-ATT-133); no request timeout. |
| 4 | Live board with 5,000 employees | the board query returns within an acceptable bound on the polling path; the < 3s real-time SLA (NFR-2 via SignalR) is DEFERRED and re-measured once SignalR lands. **Reported to caller.** |
| 5 | Trend analytics (12 months) at scale | returns quickly from the pre-aggregated monthly summary (BR-5) -- no full-history raw scan. |
| 6 | Concurrency | the dashboard + report endpoints hold their SLAs under multiple concurrent HR users without lock contention on the summary tables. |

## 6. Postconditions
- Dashboard, report, and trend endpoints meet their SLAs at 5,000-employee scale on the DB-backed path; the SignalR live-board SLA is documented as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **NFR-2 (live board < 3s via SignalR) DEFERRED:** the SignalR real-time infra is not built; the polling-refresh query latency is measured now and the real-time push SLA is re-checked once SignalR lands (consistent with TC-ATT-130). **Reported to caller.**
- **NFR-1 (dashboard < 2s, Redis-cached):** measured against the DB-computed path now (Redis CONDITIONAL per TC-ATT-138); the warm-cache target is re-measured once the cache exists. **Reported to caller.**
- Aligns with the module's perf-TC precedent (US-ATT-007 TC-ATT-097 summary@5,000, US-ATT-009 TC-ATT-126 payroll-data@5,000).
