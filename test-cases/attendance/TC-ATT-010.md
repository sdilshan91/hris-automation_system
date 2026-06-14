---
id: TC-ATT-010
user_story: US-ATT-001
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-010: Clock-in API responds within 500ms at P95 under representative load (performance)

## 1. Test Objective
Verify NFR-1: the clock-in endpoint's response time stays at or below 500ms at the 95th percentile under a representative concurrent load, with tenant isolation, permission checks, duplicate detection, and audit writes all active.

## 2. Related Requirements
- User Story: US-ATT-001
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-1, FR-6

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- A pool of distinct active employees (e.g., 200) each holding `Attendance.Clock.Self`, each with NO open clock-in at the start of the run (so each request is a valid first clock-in, exercising the create path, not the duplicate-reject path).
- Test environment representative of production (DB sizing, network, cache layer); a warm-up phase precedes measurement.
- Geo and IP policies set to a representative configuration (document which: optional geo, IP allowlist off) so the measured path reflects the common case.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/attendance/clock-in | Write SLA |
| Concurrent virtual users | 50 (ramped) | Representative |
| Total requests | >= 2,000 | Distinct employees, valid first clock-ins |
| Target | P95 <= 500ms | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a warm-up phase (e.g., 200 requests) and discard its measurements | JIT/cache warmed; not counted. |
| 2 | Execute the load profile (ramp to 50 concurrent users, >= 2,000 valid clock-ins) | All requests complete; record latency per request. |
| 3 | Compute P50, P95, P99 response times | P95 <= 500ms (NFR-1). Report P50/P99 for context. |
| 4 | Verify correctness under load | Each clock-in created exactly one `attendance_log`; no duplicates; no cross-request data bleed; error rate ~0%. |
| 5 | Verify the cache update did not become a bottleneck | Cache write (FR-6) latency is within the overall budget; if the cache layer is not yet wired, record that the DB path was measured and note it as a deviation to re-measure once cache is in place. |

## 6. Postconditions
- Latency percentiles recorded; pass/fail against the 500ms P95 target documented.
- One record per successful clock-in; no integrity violations under load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
