---
id: TC-LV-248
user_story: US-LV-012
module: Leave Management
priority: high
type: performance
status: fail
exec_note: "2026-07-02 FAIL (perf tenant, 5000 emp / 6200 leave_request / 13 leave_types seeded). The report types the TC names (Balance Summary, Utilization) DO NOT MEET the 2s P95 target at 5000-employee scale — they time out entirely. GET /leaves/reports/BalanceSummary?Year=2026 -> HTTP 000 client-timeout at 90s; server logged ERR 'Error handling GetLeaveReportQuery after 90078ms' + 500, RequestId 0HNMN7ROLJCHR. Root cause: BuildBalanceSummaryAsync calls ResolveEntitlementAsync PER employee x leave-type (N+1: ~5000 x 13 sequential EF round-trips) -> no full-report SLA possible. GET /leaves/reports/Utilization -> also HTTP 000 at 25s (ComputeUtilizationAggregatesAsync, same non-scaling path). Only the cheap reports respond: Absenteeism P95 = 102ms (n=32, min 40 / med 54 / max 126, warmup 3 discarded) and it/LopSummary cap at 1000 rows. So the API meets 2s for the light aggregate reports but FAILS the two report types this TC calls out at the NFR-1 dataset scale. See BUG (report N+1 timeout). Method: 3 warmup + 32-sample curl P95 loop for the working report; --max-time timeout probe + Serilog RequestId for the failing ones."
created: 2026-06-14
---

# TC-LV-248: Report API responds within 2s P95 for datasets up to 1,000 rows (NFR-1)

## 1. Test Objective
Verify NFR-1: a report query returning up to 1,000 rows responds within 2 seconds at the P95 latency, using server-side aggregation/pagination (and DB views/materialized views where applicable).

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-1
- Data Requirements: §7 (views/materialized views), FR-8 (read replicas)

## 3. Preconditions
- Tenant "acme" seeded with enough employees/leave data to produce a ~1,000-row report.
- Warm DB; representative dataset.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Rows | 1,000 | NFR-1 ceiling |
| Target | P95 ≤ 2,000 ms | report API |
| Samples | ≥ 50 requests | for P95 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Issue ≥50 requests for a 1,000-row report (e.g. Balance Summary / Utilization) | P95 response time ≤ 2,000 ms. |
| 2 | Inspect the query plan | Aggregations use indexes / views (GROUP BY department/type/month); no full unindexed scans for the hot path. |
| 3 | (CONDITIONAL on FR-8) Confirm read-replica routing if configured | Heavy report queries route to a read replica when available; record CONDITIONAL/DEFERRED if no replica is provisioned (primary-DB path measured). |
| 4 | Materialized-view note | If daily-refreshed materialized views back the aggregation (§7/§10), confirm the view is queried; record DEFERRED if not yet built (live-query path measured against the same target). |

## 6. Postconditions
- Report API meets the 2s P95 target for ≤1,000 rows; read-replica/materialized-view optimizations recorded CONDITIONAL where not yet provisioned.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
