---
id: TC-ATT-064
user_story: US-ATT-005
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-064: Shift management pages load within 2 seconds at P95 (NFR-1)

## 1. Test Objective
Verify NFR-1: the shift-management surfaces -- the shift list page (GET /api/v1/attendance/shifts), the shift-detail/edit view, and the employee shift-resolve (GET .../employees/{id}/shift) -- load within 2 seconds at the 95th percentile under representative load and data volume, while remaining tenant-scoped.

## 2. Related Requirements
- User Story: US-ATT-005
- Non-Functional: NFR-1 (shift management pages load < 2s P95)

## 3. Preconditions
- Tenant "acme" seeded with a realistic shift catalog (e.g. 30-50 shifts incl. SINGLE/ROTATING/FLEXIBLE) and several hundred employee_shift assignments.
- A load tool (k6/JMeter) configured against the running API with a valid HR session.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Shift catalog | 30-50 shifts | mixed types |
| Concurrency | representative (e.g. 20-50 RPS) | sustained window |
| SLA | P95 <= 2000 ms | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Drive sustained load against `GET /api/v1/attendance/shifts` (list) for acme | P95 latency <= 2000 ms; no errors; only acme shifts returned (scope preserved under load). |
| 2 | Load the shift-detail/edit read for a single shift repeatedly | P95 <= 2000 ms. |
| 3 | Drive `GET .../employees/{id}/shift?date=` (resolve, incl. a rotating shift) | P95 <= 2000 ms; rotation computation does not breach the SLA. |
| 4 | Capture P50/P95/P99 and error rate | P95 within SLA; error rate ~0%; latency stable (no degradation/leaks) across the window. |
| 5 | Confirm pagination/filtering | List pagination keeps payloads bounded so the page-load SLA holds as the catalog grows. |

## 6. Postconditions
- Shift management read paths meet the < 2s P95 SLA under load with tenant scope intact.

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
- The Redis shift-definition cache (NFR-4, 1h TTL) is **not assumed wired**. This TC measures the DB-backed read path now; if/when the Redis cache lands, re-run with cache enabled and assert the cache-hit path stays within SLA. **Reported to caller** -- consistent with the module-wide deferred-Redis handling.
