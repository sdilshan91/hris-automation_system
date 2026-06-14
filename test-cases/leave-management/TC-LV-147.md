---
id: TC-LV-147
user_story: US-LV-007
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-147: Holiday list/range API responds within 200ms P95 (NFR-1; Redis cache DEFERRED -- DB path)

## 1. Test Objective
Verify the holiday list/range API (`GET /api/v1/holidays?year=` and `?from&to`) responds within 200ms (P95) for a year's data. Redis caching (NFR-1) is DEFERRED module-wide; this test measures the DB-backed path served by the `(tenant_id, date)` index and records the cache layer as DEFERRED (not a silent gap).

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-6
- Note: Redis caching DEFERRED (per docs/vault/modules/leave-management.md); DB-backed range read measured against the 200ms target; index `ix_holiday_tenant_id_date`.

## 3. Preconditions
- Tenant "acme" with a representative year of holidays (~30-40 rows for 2026).
- Employee/HR authenticated with `Holiday.View`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Target | <= 200ms P95 | read SLA |
| Sample | 50+ sequential reads | warm DB |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Issue 50+ `GET /api/v1/holidays?year=2026` reads and record latency | P95 <= 200ms on the DB path. |
| 2 | Issue range reads `?from=2026-06-01&to=2026-06-30` | Range read also <= 200ms P95; the `(tenant_id, date)` index serves the predicate. |
| 3 | Confirm the leave-calc seam read (`IHolidayProvider`) | The internal range read used by leave-day calc is similarly bounded. |
| 4 | Record deferral honestly | The Redis-cached fast path is DEFERRED; the DB path meets the target now and is the verified baseline. |

## 6. Postconditions
- Holiday read APIs meet the 200ms P95 target on the DB path; Redis acceleration is DEFERRED.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
