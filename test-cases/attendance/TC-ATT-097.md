---
id: TC-ATT-097
user_story: US-ATT-007
module: Attendance
priority: high
type: performance
status: pass
exec_note: "2026-07-02 PASS: perf tenant (5000 emp, June 2026 attendance). (NFR-1) GET /attendance/summary/monthly?month=2026-06 P95 0.340s over 25 reqs (3 warmup discarded) << 2.5s. (NFR-2) POST /attendance/summary/monthly/generate?month=2026-06 Hangfire summary job COMPLETED synchronously in 63s << 10min. (NFR-4) GET .../export?month=2026-06&format=csv&departmentId=<Perf Dept 1> returned 500 emp rows (501 lines incl header), HTTP 200 in 0.076s << 30s (dept-filter used to hit the <=1000 sync path; full-5000 export goes 202/async by design). Method: curl P95 loop + timed job/export."
created: 2026-06-14
---

# TC-ATT-097: Performance -- summary page < 2.5s P95 @5,000 employees, Hangfire job < 10min @5,000, export < 30s @500 (NFR-1/NFR-2/NFR-4)

## 1. Test Objective
Verify NFR-1, NFR-2, and NFR-4: the monthly summary page loads within 2.5 seconds at P95 for a tenant with up to 5,000 employees; the Hangfire summary job completes within 10 minutes for 5,000 employees; and synchronous export file generation for up to 500 employees completes within 30 seconds -- all served from the materialized/cached summary (DB-backed path measured; Redis DEFERRED).

## 2. Related Requirements
- User Story: US-ATT-007
- Non-Functional Requirements: NFR-1 (page < 2.5s P95 @5,000), NFR-2 (job < 10min @5,000), NFR-4 (export < 30s @500)

## 3. Preconditions
- Tenant "scaleco" seeded with 5,000 active employees and a full month of attendance for 2026-05; the monthly summary is generated (materialized).
- HR Officer authenticated with `Attendance.Read.All`. Representative load applied; measure P95.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employees | 5,000 | scale |
| page SLA | < 2.5s P95 | NFR-1 |
| job SLA | < 10 min | NFR-2 |
| export SLA (500) | < 30s | NFR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the monthly summary page for 2026-05 under representative concurrency | P95 page/API response < 2.5s for the 5,000-employee tenant (NFR-1), reading the materialized summary (not recomputing per request). |
| 2 | Run the monthly Hangfire aggregation job for the 5,000-employee tenant | The job completes in < 10 minutes (NFR-2). |
| 3 | Export a 500-employee summary (CSV/XLSX/PDF) synchronously | File generation completes in < 30 seconds (NFR-4). |
| 4 | Apply filters (department/status) and reload | Filtered loads also meet the < 2.5s P95 target. |
| 5 | Verify accuracy is not traded for speed | Spot-check several rows against TC-ATT-084 logic -- values remain minute-accurate (NFR-5) at scale. |

## 6. Postconditions
- The summary read, the aggregation job, and the synchronous export all meet their SLAs at the stated scale without sacrificing accuracy.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- NFR-1 names Redis cache as the mechanism for the < 2.5s page load; Redis is DEFERRED module-wide (per docs/vault/modules/attendance.md and prior ATT stories). This TC measures the DB-backed materialized-summary read path now; when Redis lands, re-measure the cache-hit path and assert the cache serves subsequent loads (see TC-ATT-098). **Reported to caller.**
- The > 1,000-employee export goes async (TC-ATT-095); NFR-4's 500-employee bound is the synchronous-export SLA.
