---
id: TC-ATT-023
user_story: US-ATT-002
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-023: Clock-out API responds within 500ms at P95 under representative load (NFR-1)

## 1. Test Objective
Verify NFR-1: the clock-out endpoint's response time stays at or below 500ms at the 95th percentile under representative concurrent load, with the full work-hours calculation path active (break deduction, overtime/short-day/anomaly evaluation against shift config, audit write, and cache update) and tenant isolation enforced.

## 2. Related Requirements
- User Story: US-ATT-002
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-2, FR-3, FR-4, FR-5

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled.
- A pool of distinct active employees (e.g., 200), each holding `Attendance.Clock.Self`, each with exactly ONE OPEN clock-in record at the start of the run (so each request exercises the close/calculate path, not the no-open-record reject path).
- Each employee assigned to a shift so the overtime/short-day evaluation runs (representative config documented).
- Test environment representative of production; a warm-up phase precedes measurement.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/attendance/clock-out | Write SLA |
| Concurrent virtual users | 50 (ramped) | Representative |
| Total requests | >= 2,000 | Distinct employees, each a valid open->close |
| Target | P95 <= 500ms | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a warm-up phase (e.g., 200 requests) and discard its measurements | JIT/cache warmed; not counted. |
| 2 | Execute the load profile (ramp to 50 concurrent users, >= 2,000 valid clock-outs) | All requests complete; latency recorded per request. |
| 3 | Compute P50, P95, P99 | P95 <= 500ms (NFR-1). Report P50/P99 for context. |
| 4 | Verify correctness under load | Each clock-out closes exactly one record with correct `total_work_minutes`/`status`; no duplicate closes; no cross-request data bleed; error rate ~0%. |
| 5 | Verify the calculation + cache update are not bottlenecks | The shift evaluation and cache write (FR-3/FR-4) stay within the budget; if Redis is not wired, record that the DB path was measured and re-measure once cache exists. |

## 6. Postconditions
- Latency percentiles recorded; pass/fail against the 500ms P95 documented.
- One correctly computed completed record per clock-out; no integrity violations under load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
