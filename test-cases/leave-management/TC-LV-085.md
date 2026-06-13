---
id: TC-LV-085
user_story: US-LV-004
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-085: Pending queue API responds within 300ms P95 using the ix_leave_pending partial index

## 1. Test Objective
Verify that the pending queue endpoint meets its performance SLA: P95 response time <= 300ms under representative load, served via the partial index `ix_leave_pending ON leave_request(tenant_id, start_date) WHERE status = 'Pending'`.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-1
- Data Requirements: Section 7 (ix_leave_pending partial index)

## 3. Preconditions
- Tenant "acme" is active with a realistic dataset: a manager with ~50 direct reports and a few hundred pending requests across the tenant.
- The `ix_leave_pending` partial index exists on `leave_request(tenant_id, start_date) WHERE status = 'Pending'`.
- A load tool (k6/JMeter) and DB query plan inspection are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Direct reports | ~50 | Representative manager |
| Pending rows (tenant) | ~500 | Representative volume |
| Concurrency | 50 virtual users | Sustained |
| SLA | P95 <= 300ms | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm the system, then run a sustained load of 50 concurrent managers calling `GET /api/v1/leaves/pending?page=1` | Throughput is steady; no errors. |
| 2 | Measure response-time percentiles | P95 <= 300ms; P50 well below SLA. |
| 3 | Inspect the query plan (`EXPLAIN ANALYZE`) for the pending query | The plan uses the `ix_leave_pending` partial index (no full sequential scan of `leave_request`). |
| 4 | Run with pagination and a leave-type filter applied | P95 remains <= 300ms; filtering/pagination does not regress to a sequential scan. |
| 5 | Verify tenant + direct-reports scoping is still correct under load | Results remain correctly scoped; no cross-team or cross-tenant rows. |

## 6. Postconditions
- No data mutated.
- Pending queue meets the 300ms P95 SLA backed by the partial index.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
