---
id: TC-ATT-081
user_story: US-ATT-006
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-081: Overtime approval queue loads within 2 seconds at P95 (performance)

## 1. Test Objective
Verify NFR-4: the overtime approval queue (`GET /api/v1/attendance/overtime/pending`) loads within 2 seconds at P95 for a realistic manager team with pending overtime, while remaining correctly tenant- and team-scoped.

## 2. Related Requirements
- User Story: US-ATT-006
- Non-Functional: NFR-4 (overtime approval queue loads within 2s at P95)

## 3. Preconditions
- Tenant "acme" seeded with a manager who has up to ~50 direct reports each with PENDING overtime (representative of the regularization-queue load profile, US-ATT-004 NFR-1).
- A load harness measuring P95 over a representative request count under concurrency.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| pending overtime in queue | ~50 | representative |
| metric | P95 latency | |
| threshold | <= 2000 ms | NFR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm up, then drive concurrent `GET /overtime/pending` as the manager | P95 response time <= 2s; no errors. |
| 2 | Verify correctness under load | Each response returns only the manager's team PENDING overtime (no cross-team/decided leakage) -- correctness holds at P95. |
| 3 | Verify tenant scoping under load | Only acme rows are returned; the EF global query filter is applied on every request. |
| 4 | Larger queue (stress) | Latency degrades gracefully; record P95 at higher volumes for capacity planning. |

## 6. Postconditions
- The overtime approval queue meets the 2s P95 SLA while preserving team/tenant scoping.

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
- Mirrors US-ATT-004 TC-ATT-049 (regularization approval queue 2s P95). No Redis cache is assumed (DB-backed read path measured now), consistent with the module-wide deferred-Redis handling. **Reported to caller.**
