---
id: TC-LV-125
user_story: US-LV-006
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-125: Balance API responds within 200ms P95 (Redis DEFERRED; DB-fallback path measured)

## 1. Test Objective
Verify that `GET /api/v1/leaves/my-balance` meets the 200ms P95 latency target. NFR-1 specifies Redis-cached reads; since the Redis balance cache is DEFERRED module-wide, this test measures the DB-computed (ledger running-total) path and records the cached path as conditional (NFR-1, FR-5).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-5
- Note: Redis balance cache DEFERRED (per vault); DB-fallback aggregation is the current source.

## 3. Preconditions
- Tenant "acme" active; an employee with a realistic ledger volume (e.g., 12 monthly accruals x several leave types, plus usages/adjustments).
- Performance index on `leave_ledger (tenant_id, employee_id, leave_type_id, leave_year)` present.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Target | P95 <= 200ms | NFR-1 |
| Load | warm steady-state | typical tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Issue a representative load against `my-balance` and record latency percentiles | The DB-computed path's P95 is within (or close to) 200ms for a typical tenant; the query uses the ledger index. |
| 2 | Record the measured P95 against the 200ms target | If the DB-fallback path slightly exceeds 200ms, record as NOTE pending the deferred Redis cache, not an outright failure of the story. |
| 3 | (DEFERRED) Re-measure with the Redis cache enabled | When implemented, cached reads should comfortably meet the 200ms P95 target. Mark CONDITIONAL on the cache layer. |
| 4 | Confirm no N+1 query pattern | A single aggregated query (or bounded set) serves the balances, not per-leave-type round trips. |

## 6. Postconditions
- DB-fallback latency characterized against the 200ms target; cached-path target deferred to the Redis layer.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
