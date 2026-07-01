---
id: TC-ATT-126
user_story: US-ATT-009
module: Attendance
priority: high
type: performance
status: blocked
exec_note: "S1: needs S2 (scale data seed)."
created: 2026-06-15
---

# TC-ATT-126: Performance -- payroll-data API returns 5,000 employees within 5s; reconciliation view loads within 3s P95; lock operation atomic/consistent under load

## 1. Test Objective
Verify the US-ATT-009 performance + consistency NFRs: the attendance-to-payroll API returns data for up to 5,000 employees within 5 seconds (NFR-1); the reconciliation view loads within 3 seconds at P95 (NFR-5); and the lock operation is atomic with no partial reads during payroll computation (NFR-2/NFR-4).

## 2. Related Requirements
- User Story: US-ATT-009
- Non-Functional: NFR-1 (payroll-data <= 5s for 5,000 employees), NFR-5 (reconciliation <= 3s P95), NFR-2 (lock atomic), NFR-4 (data consistency, no partial reads during payroll computation)
- API: GET /payroll-data?month=; GET /reconciliation?month=; POST /period-lock

## 3. Preconditions
- Tenant "acme" seeded with 5,000 employees and a generated monthly summary for 2026-05.
- Representative production-like dataset (mix of present/absent/lop/overtime).
- Warm + cold runs measured; P95 over a representative sample.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employee count | 5,000 | NFR-1 target |
| payroll-data SLA | <= 5s | full-period pull |
| reconciliation SLA | <= 3s P95 | per §8 view load |
| lock atomicity | all-or-nothing | NFR-2 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /payroll-data?month=2026-05` for all 5,000 employees | Response completes within 5s (NFR-1); all 5,000 rows returned with complete FR-2 fields. |
| 2 | Repeat across runs; record P95 | P95 stays within the 5s budget; no timeouts or truncated payloads. |
| 3 | Load `GET /reconciliation?month=2026-05` for the tenant | View data returns within 3s at P95 (NFR-5). |
| 4 | While a payroll-data pull is in progress, confirm no partial/in-flight writes are read | Consistent snapshot -- no partial reads during the computation window (NFR-4); a concurrent lock does not expose half-locked data. |
| 5 | Concurrent `POST /period-lock` while reads run | The lock is atomic (NFR-2); reads either see fully-unlocked or fully-locked state, never partial. |
| 6 | Pagination/streaming for the 5,000-row pull (if applicable) | Either a single bounded response within SLA or paged retrieval whose aggregate stays within SLA. |

## 6. Postconditions
- payroll-data and reconciliation meet their SLAs at the target scale; lock is atomic; no partial reads occur during payroll computation. No data mutated by the measurements (except the deliberate lock in Step 5, which is released).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The payroll-data and reconciliation reads draw on the materialized monthly summary (US-ATT-007); the SLA assumes the summary is pre-computed (precondition §2). If a Redis cache layer is later added it should improve these numbers -- measured here against the DB-backed materialized path (consistent with TC-ATT-097 / module-wide deferred-Redis handling). **Reported to caller.**
- NFR-4 "no partial reads during payroll computation" is verified at the attendance read-consistency level; end-to-end consistency with the payroll engine's read is exercised when Payroll lands. **Reported to caller.**
